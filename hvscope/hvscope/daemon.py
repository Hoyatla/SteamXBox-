"""The capture loop: camera frame in, readable text out.

One cycle is: burst-grab, median-stack, dewarp to the calibrated rectangle,
threshold, decide whether the screen has settled, OCR when it has, and file
any new lines in the transcript.
"""

from __future__ import annotations

import threading
import time
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
from PIL import Image

from . import geometry, ocr, preprocess
from .logbook import LogBook, write_json, write_text
from .sources import FrameSource, SourceError, build_source, grab_burst


def _now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


@dataclass
class ScopeState:
    """Everything a reader needs to judge what the screen is doing."""

    frames: int = 0
    errors: int = 0
    last_error: str | None = None
    last_frame_at: str | None = None
    screen_changed_at: str | None = None
    last_ocr_at: str | None = None
    change_score: float = 100.0
    stable: bool = False
    calibrated: bool = False
    source: str = ""
    ocr_available: bool = False
    text_lines: int = 0
    confidence: float = 0.0
    transcript: dict = field(default_factory=dict)

    def as_dict(self) -> dict:
        payload = {
            "updated_at": _now(),
            "frames": self.frames,
            "errors": self.errors,
            "last_error": self.last_error,
            "last_frame_at": self.last_frame_at,
            "screen_changed_at": self.screen_changed_at,
            "last_ocr_at": self.last_ocr_at,
            "change_score": round(self.change_score, 4),
            "stable": self.stable,
            "calibrated": self.calibrated,
            "source": self.source,
            "ocr_available": self.ocr_available,
            "text_lines": self.text_lines,
            "confidence": round(self.confidence, 1),
            "transcript": self.transcript,
        }
        return payload


class Scope:
    """Owns the source, the pipeline and the output directory."""

    def __init__(self, config: dict, out_dir: Path):
        self.config = config
        self.out = Path(out_dir)
        self.out.mkdir(parents=True, exist_ok=True)
        (self.out / "history").mkdir(exist_ok=True)

        self.source: FrameSource = build_source(config.get("source", {}))
        log_conf = config.get("logbook", {})
        self.logbook = LogBook(
            self.out / "log.txt",
            window=int(log_conf.get("window", 240)),
            min_length=int(log_conf.get("min_length", 2)),
        )

        self.state = ScopeState(source=self.source.describe(), ocr_available=ocr.available())
        self.text = ""
        self.lock = threading.Lock()

        self._previous_gray: np.ndarray | None = None
        self._ocr_gray: np.ndarray | None = None
        self._last_ocr_monotonic = 0.0
        self._history_index = 0

    # -- pipeline stages ------------------------------------------------

    def _capture(self) -> Image.Image:
        capture = self.config.get("capture", {})
        frames = grab_burst(
            self.source,
            int(capture.get("stack", 3)),
            float(capture.get("stack_delay", 0.12)),
        )
        return preprocess.median_stack(frames)

    def _flatten(self, raw: Image.Image) -> Image.Image:
        """Dewarp if calibrated; otherwise pass the frame straight through."""
        calibration = self.config.get("calibration", {})
        quad = calibration.get("quad")
        if not quad:
            self.state.calibrated = False
            return raw.convert("RGB")
        self.state.calibrated = True
        size = calibration.get("output_size") or geometry.suggest_output_size(quad)
        return geometry.dewarp(raw.convert("RGB"), quad, (int(size[0]), int(size[1])))

    def _should_ocr(self, gray: np.ndarray, stable: bool) -> bool:
        if not self.config.get("ocr", {}).get("enabled", True):
            return False
        if not ocr.available():
            return False
        if not stable:
            # OCR of a half-redrawn screen produces confident nonsense, which
            # is worse than no reading at all.
            return False
        if self._ocr_gray is None:
            return True

        capture = self.config.get("capture", {})
        drift = preprocess.change_score(self._ocr_gray, gray)
        if drift > float(capture.get("ocr_threshold", 0.20)):
            return True
        elapsed = time.monotonic() - self._last_ocr_monotonic
        return elapsed >= float(capture.get("refresh_seconds", 30.0))

    def _archive(self, image: Image.Image, text: str) -> None:
        """Ring buffer of recent screens, so a crash can be looked at after."""
        limit = int(self.config.get("output", {}).get("history", 60))
        if limit <= 0:
            return
        slot = self._history_index % limit
        self._history_index += 1
        stem = self.out / "history" / f"{slot:04d}"
        image.save(stem.with_suffix(".png"))
        write_text(stem.with_suffix(".txt"), f"# {_now()}\n{text}\n")

    # -- one cycle ------------------------------------------------------

    def step(self) -> dict:
        """Run a single capture cycle and refresh the output files."""
        try:
            raw = self._capture()
        except SourceError as exc:
            with self.lock:
                self.state.errors += 1
                self.state.last_error = str(exc)
                write_json(self.out / "state.json", self.state.as_dict())
            return self.state.as_dict()

        flat = self._flatten(raw)
        gray = preprocess.to_gray(flat)
        score = preprocess.change_score(self._previous_gray, gray)
        stable = score <= float(self.config.get("capture", {}).get("stable_threshold", 0.35))

        binary = preprocess.prepare_for_ocr(flat, self.config.get("preprocess", {}))

        new_lines: list[str] = []
        did_ocr = False
        ocr_error: str | None = None
        if self._should_ocr(gray, stable):
            settings = self.config.get("ocr", {})
            try:
                text, confidence = ocr.run(
                    binary,
                    lang=settings.get("lang", "eng"),
                    psm=int(settings.get("psm", 6)),
                    extra_args=list(settings.get("extra_args", [])),
                    timeout=float(settings.get("timeout", 60.0)),
                    min_confidence=float(settings.get("min_confidence", 45.0)),
                )
                text = ocr.clean(text)
                did_ocr = True
                self.state.confidence = confidence
                self._ocr_gray = gray
                self._last_ocr_monotonic = time.monotonic()
                new_lines = self.logbook.ingest(text)
                with self.lock:
                    self.text = text
            except ocr.OcrUnavailable as exc:
                ocr_error = str(exc)

        with self.lock:
            self.state.frames += 1
            self.state.last_frame_at = _now()
            self.state.change_score = score
            self.state.stable = stable
            self.state.ocr_available = ocr.available()
            self.state.text_lines = len(self.text.splitlines()) if self.text else 0
            self.state.transcript = self.logbook.stats()
            if score > float(self.config.get("capture", {}).get("stable_threshold", 0.35)):
                self.state.screen_changed_at = _now()
            if did_ocr:
                self.state.last_ocr_at = _now()
            if ocr_error:
                self.state.errors += 1
                self.state.last_error = ocr_error
            elif not did_ocr and stable:
                self.state.last_error = None

            raw.save(self.out / "raw.png")
            flat.save(self.out / "screen-color.png")
            binary.save(self.out / "screen.png")
            write_text(self.out / "screen.txt", self.text + ("\n" if self.text else ""))
            write_json(self.out / "state.json", self.state.as_dict())
            snapshot = self.state.as_dict()

        if did_ocr and new_lines:
            self._archive(flat, self.text)

        self._previous_gray = gray
        snapshot["new_lines"] = new_lines
        return snapshot

    # -- loop -----------------------------------------------------------

    def run(self, stop: threading.Event | None = None, on_cycle=None) -> None:
        """Capture until ``stop`` is set."""
        stop = stop or threading.Event()
        interval = float(self.config.get("capture", {}).get("interval", 1.0))
        while not stop.is_set():
            started = time.monotonic()
            try:
                snapshot = self.step()
            except Exception as exc:  # keep the loop alive through any surprise
                with self.lock:
                    self.state.errors += 1
                    self.state.last_error = f"{type(exc).__name__}: {exc}"
                snapshot = self.state.as_dict()
            if on_cycle:
                on_cycle(snapshot)
            remaining = interval - (time.monotonic() - started)
            if remaining > 0:
                stop.wait(remaining)

    def close(self) -> None:
        self.source.close()
