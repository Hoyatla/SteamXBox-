"""Tesseract wrapper.

Kept to a subprocess call so the package needs no OCR binding installed; the
only requirement is the ``tesseract`` binary on PATH.

Reading is done through Tesseract's TSV output rather than its plain text,
for two reasons. Per-word confidence lets low-scoring lines be dropped, which
is what stops the engine inventing words out of camera noise and screen
moire. And word bounding boxes let column alignment be rebuilt, which matters
when the screen holds a register dump rather than prose.
"""

from __future__ import annotations

import csv
import io
import re
import shutil
import statistics
import subprocess
import tempfile
from dataclasses import dataclass
from pathlib import Path

from PIL import Image


class OcrUnavailable(RuntimeError):
    """Raised when the tesseract binary is missing or unusable."""


@dataclass
class Line:
    """One recognised line of text with the engine's confidence in it."""

    text: str
    confidence: float
    top: int
    left: int


def available() -> bool:
    return shutil.which("tesseract") is not None


def version() -> str:
    if not available():
        return "not installed"
    try:
        result = subprocess.run(["tesseract", "--version"], capture_output=True, timeout=15, check=False)
    except (OSError, subprocess.TimeoutExpired) as exc:
        return f"unusable: {exc}"
    output = result.stdout.decode("utf-8", "replace").splitlines()
    return output[0].strip() if output else "unknown"


def _invoke(image: Image.Image, lang: str, psm: int, extra_args: list[str] | None,
            timeout: float, mode: str) -> str:
    if not available():
        raise OcrUnavailable("tesseract is not on PATH (install tesseract-ocr)")

    with tempfile.TemporaryDirectory(prefix="hvscope-ocr-") as tmp:
        source = Path(tmp) / "page.png"
        image.save(source)

        command = ["tesseract", str(source), "stdout", "--psm", str(int(psm)), "-l", lang]
        command += list(extra_args or [])
        if mode:
            command.append(mode)
        try:
            result = subprocess.run(command, capture_output=True, timeout=timeout, check=False)
        except FileNotFoundError as exc:
            raise OcrUnavailable("tesseract disappeared from PATH") from exc
        except subprocess.TimeoutExpired as exc:
            raise OcrUnavailable(f"tesseract timed out after {timeout}s") from exc

        if result.returncode != 0:
            detail = result.stderr.decode("utf-8", "replace").strip()[:400]
            raise OcrUnavailable(f"tesseract failed ({result.returncode}): {detail}")

        return result.stdout.decode("utf-8", "replace")


# Characters that never appear in a hex literal, and the digit each is a
# misreading of. Restricted to unambiguous cases: b and d are valid hex, so
# they are deliberately absent.
_HEX_CONFUSIONS = str.maketrans({
    "O": "0", "o": "0", "Q": "0",
    "l": "1", "I": "1", "i": "1", "|": "1",
    "S": "5", "s": "5",
    "Z": "2", "z": "2",
    "G": "6", "g": "9", "T": "7",
})

_HEX_LITERAL = re.compile(r"\b[O0Qo]x([0-9a-fA-FOoQlIi|SsZzGgT]{2,})\b")


def fix_hex_literals(text: str) -> str:
    """Repair the digit/letter confusions OCR makes inside hex addresses.

    Low-level debug output is mostly addresses, and ``0x`` reads as ``Ox``
    almost every time. Substitution is confined to characters that cannot be
    hex digits at all, so a correct literal is never altered.
    """

    def repair(match: re.Match) -> str:
        return "0x" + match.group(1).translate(_HEX_CONFUSIONS)

    return _HEX_LITERAL.sub(repair, text)


def _rebuild_line(words: list[dict], char_width: float, page_left: int) -> str:
    """Lay words back out on a character grid, preserving column alignment.

    Spacing is derived from the gap between one word's right edge and the
    next word's left edge rather than from absolute columns: a small error in
    the estimated glyph width then costs nothing, instead of accumulating
    into a spurious space on every line.
    """
    ordered = sorted(words, key=lambda w: w["left"])
    if not ordered:
        return ""

    indent = 0
    if char_width > 0:
        indent = max(0, int(round((ordered[0]["left"] - page_left) / char_width)))

    parts: list[str] = [" " * indent, ordered[0]["text"]]
    for previous, current in zip(ordered, ordered[1:]):
        gap = current["left"] - (previous["left"] + previous["width"])
        # Glyph boxes are tighter than advance widths, so an edge-to-edge gap
        # overstates the space count; bias the rounding down to compensate.
        spaces = max(1, int(gap / char_width + 0.25)) if char_width > 0 else 1
        parts.append(" " * spaces)
        parts.append(current["text"])
    return "".join(parts).rstrip()


def read_lines(image: Image.Image, lang: str = "eng", psm: int = 6,
               extra_args: list[str] | None = None, timeout: float = 60.0) -> list[Line]:
    """OCR an image into lines carrying their mean word confidence."""
    raw = _invoke(image, lang, psm, extra_args, timeout, "tsv")

    reader = csv.DictReader(io.StringIO(raw), delimiter="\t", quoting=csv.QUOTE_NONE)
    grouped: dict[tuple, list[dict]] = {}
    for row in reader:
        text = (row.get("text") or "").strip()
        if not text:
            continue
        try:
            confidence = float(row.get("conf", -1))
            word = {
                "text": text,
                "conf": confidence,
                "left": int(row["left"]),
                "top": int(row["top"]),
                "width": int(row["width"]),
            }
            key = (int(row["block_num"]), int(row["par_num"]), int(row["line_num"]))
        except (TypeError, ValueError, KeyError):
            continue
        if confidence < 0:
            continue
        grouped.setdefault(key, []).append(word)

    if not grouped:
        return []

    # A monospace console gives a consistent glyph width; the median across
    # every word is a robust estimate of it even when some words are garbage.
    widths = [w["width"] / len(w["text"]) for words in grouped.values() for w in words if w["text"]]
    char_width = statistics.median(widths) if widths else 0.0

    # Indentation is measured from the leftmost word on the page, so a screen
    # with a uniform margin does not come out indented on every line.
    page_left = min(w["left"] for words in grouped.values() for w in words)

    lines: list[Line] = []
    for words in grouped.values():
        text = _rebuild_line(words, char_width, page_left)
        if not text.strip():
            continue
        lines.append(
            Line(
                text=text,
                confidence=statistics.mean(w["conf"] for w in words),
                top=min(w["top"] for w in words),
                left=min(w["left"] for w in words),
            )
        )

    lines.sort(key=lambda line: (line.top, line.left))
    return lines


def run(image: Image.Image, lang: str = "eng", psm: int = 6, extra_args: list[str] | None = None,
        timeout: float = 60.0, min_confidence: float = 0.0,
        repair_hex: bool = True) -> tuple[str, float]:
    """OCR an image; return its text and the mean confidence of kept lines.

    Lines scoring below ``min_confidence`` are discarded. Camera noise and LCD
    moire do get recognised as words, but with far lower confidence than real
    glyphs, so a threshold around 45 removes them and leaves genuine text.
    """
    lines = read_lines(image, lang, psm, extra_args, timeout)
    kept = [line for line in lines if line.confidence >= float(min_confidence)]
    if not kept:
        return "", 0.0

    # Blank lines are reinserted where the vertical gap is much larger than
    # the usual line pitch, so paragraph breaks on screen survive.
    pitches = [b.top - a.top for a, b in zip(kept, kept[1:]) if b.top > a.top]
    pitch = statistics.median(pitches) if pitches else 0

    rendered: list[str] = [kept[0].text]
    for previous, current in zip(kept, kept[1:]):
        if pitch and (current.top - previous.top) > pitch * 1.8:
            rendered.append("")
        rendered.append(current.text)

    confidence = statistics.mean(line.confidence for line in kept)
    text = "\n".join(rendered)
    if repair_hex:
        text = fix_hex_literals(text)
    return text, confidence


def clean(text: str) -> str:
    """Drop trailing blanks and collapse the runs of empty lines OCR invents."""
    lines = [line.rstrip() for line in text.splitlines()]
    output: list[str] = []
    blank = 0
    for line in lines:
        if line.strip():
            blank = 0
            output.append(line)
        else:
            blank += 1
            if blank == 1 and output:
                output.append("")
    while output and not output[-1].strip():
        output.pop()
    return "\n".join(output)
