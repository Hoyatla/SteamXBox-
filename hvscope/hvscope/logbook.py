"""Accumulate every distinct line the screen has shown.

A single frame only holds what is on screen right now. A boot log scrolls,
so the interesting line is often the one that left the screen two seconds
ago. This keeps an append-only transcript instead.

The hard part is OCR jitter: the same physical line re-read on the next frame
often differs by a character or two, so exact comparison would append it again
every second.

Edit distance is the wrong tool for the job here. ``0x1000`` and ``0x9000``
differ by one character and are entirely different addresses, while ``enabled``
and ``enab1ed`` differ by one character and are the same word -- and a boot log
is mostly addresses and counters. What separates the two cases is not how many
characters differ but *which*: OCR confuses ``0`` with ``O`` and ``1`` with
``l``, never ``1`` with ``9``. Lines are therefore compared with each character
folded onto its confusable class, and must then match exactly. Jitter folds
away; a changed digit does not.
"""

from __future__ import annotations

import json
import re
import time
from collections import deque
from pathlib import Path

_WHITESPACE = re.compile(r"\s+")

# Glyph shapes an OCR engine genuinely mixes up, each folded onto one
# representative. Pairs that merely look similar to a human (c/e, u/v) are
# left out: folding them would hide real differences.
_CONFUSABLE = str.maketrans({
    "o": "0", "q": "0",
    "l": "1", "i": "1", "|": "1", "!": "1",
    "s": "5",
    "b": "8",
    "z": "2",
    "g": "9",
    "t": "7",
})


def normalise(line: str) -> str:
    """Collapse whitespace and case so cosmetic OCR wobble compares equal."""
    return _WHITESPACE.sub(" ", line).strip().lower()


def canonical(line: str) -> str:
    """Fold a line onto its confusable-class form for repeat detection.

    Two lines share a canonical form exactly when they could be the same text
    misread twice. ``0x1000`` and ``0x9000`` do not; ``enabled`` and
    ``enab1ed`` do.
    """
    return normalise(line).translate(_CONFUSABLE)


class LogBook:
    """Append-only transcript of screen lines, de-duplicated."""

    def __init__(self, path: Path, window: int = 240, min_length: int = 2):
        self.path = Path(path)
        self.window = int(window)
        self.min_length = int(min_length)
        self._recent: deque[str] = deque(maxlen=self.window)
        self._recent_set: set[str] = set()
        self.total_lines = 0
        self.repeats = 0

    def _remember(self, key: str) -> None:
        if len(self._recent) == self._recent.maxlen and self._recent:
            evicted = self._recent[0]
            # Only forget it if no other slot still holds the same text.
            if list(self._recent).count(evicted) == 1:
                self._recent_set.discard(evicted)
        self._recent.append(key)
        self._recent_set.add(key)

    def ingest(self, text: str, when: float | None = None) -> list[str]:
        """Add the unseen lines of ``text``; return exactly those added."""
        stamp = time.strftime("%H:%M:%S", time.localtime(when if when is not None else time.time()))
        added: list[str] = []

        for raw in text.splitlines():
            line = raw.rstrip()
            key = canonical(line)
            if len(key) < self.min_length:
                continue
            if key in self._recent_set:
                self.repeats += 1
                continue
            self._remember(key)
            added.append(line)
            self.total_lines += 1

        if added:
            self.path.parent.mkdir(parents=True, exist_ok=True)
            with self.path.open("a", encoding="utf-8") as handle:
                for line in added:
                    handle.write(f"[{stamp}] {line}\n")

        return added

    def tail(self, count: int = 80) -> str:
        """Last ``count`` transcript lines, for the HTTP API."""
        if not self.path.exists():
            return ""
        lines = self.path.read_text(encoding="utf-8", errors="replace").splitlines()
        return "\n".join(lines[-int(count):])

    def stats(self) -> dict:
        return {"lines": self.total_lines, "repeats_skipped": self.repeats, "path": str(self.path)}

    def reset(self) -> None:
        self._recent.clear()
        self._recent_set.clear()
        self.total_lines = 0
        self.repeats = 0
        if self.path.exists():
            rotated = self.path.with_suffix(self.path.suffix + f".{int(time.time())}")
            self.path.rename(rotated)


def write_json(path: Path, payload: dict) -> None:
    """Write JSON atomically so a reader never sees a half-written file."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")
    tmp.replace(path)


def write_text(path: Path, payload: str) -> None:
    """Write text atomically, same reasoning as :func:`write_json`."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(payload, encoding="utf-8")
    tmp.replace(path)
