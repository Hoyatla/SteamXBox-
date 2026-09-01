"""End-to-end: a simulated camera photo of a console screen, read back as text."""

import difflib
import json

import pytest

from hvscope import ocr
from hvscope.config import DEFAULT_CONFIG, deep_merge
from hvscope.daemon import Scope
from synth import BOOT_LINES, photograph, render_screen

needs_tesseract = pytest.mark.skipif(not ocr.available(), reason="tesseract not installed")


def build_scene(tmp_path, lines=BOOT_LINES, exposures=3, **photo_kwargs):
    """Write a burst of fake photos and return a Scope wired to read them."""
    frames = tmp_path / "frames"
    frames.mkdir()
    screen = render_screen(lines)
    quad = None
    for index in range(exposures):
        photo, quad = photograph(screen, seed=index, **photo_kwargs)
        photo.save(frames / f"{index:02d}.jpg", quality=92)

    config = deep_merge(DEFAULT_CONFIG, {
        "source": {"kind": "dir", "path": str(frames)},
        "capture": {"stack": exposures, "stack_delay": 0.0, "stable_threshold": 100.0},
        "calibration": {"quad": [list(p) for p in quad], "output_size": [1280, 720]},
    })
    return Scope(config, tmp_path / "out"), config


def similarity(expected, got):
    pairs = list(zip(expected, got))
    if not pairs:
        return 0.0
    return sum(difflib.SequenceMatcher(None, a, b).ratio() for a, b in pairs) / len(pairs)


@needs_tesseract
def test_reads_a_photographed_boot_screen(tmp_path):
    scope, _ = build_scene(tmp_path)
    state = scope.step()

    text = (tmp_path / "out" / "screen.txt").read_text(encoding="utf-8")
    got = [line.rstrip() for line in text.splitlines() if line.strip()]

    # Every line recovered, none invented out of moire or sensor noise.
    assert len(got) == len(BOOT_LINES)
    assert similarity(BOOT_LINES, got) > 0.95
    assert state["confidence"] > 70
    assert state["calibrated"] is True


@needs_tesseract
def test_finds_the_panic_line(tmp_path):
    # The whole point of the tool: the failure must survive the round trip.
    scope, _ = build_scene(tmp_path)
    scope.step()
    text = (tmp_path / "out" / "screen.txt").read_text(encoding="utf-8")
    assert "PANIC" in text
    assert "vmlaunch" in text


@needs_tesseract
def test_column_alignment_survives_the_camera(tmp_path):
    registers = [
        "RAX 0000000000000000    RBX 00000000deadbeef",
        "CR0 0000000080050033    CR4 00000000003406f8",
    ]
    scope, _ = build_scene(tmp_path, registers)
    scope.step()
    for line in (tmp_path / "out" / "screen.txt").read_text(encoding="utf-8").splitlines():
        if "RAX" in line or "CR0" in line or "CRO" in line:
            assert "   " in line.strip()


@needs_tesseract
def test_writes_every_output_artefact(tmp_path):
    scope, _ = build_scene(tmp_path)
    scope.step()
    out = tmp_path / "out"
    for name in ("raw.png", "screen.png", "screen-color.png", "screen.txt", "state.json", "log.txt"):
        assert (out / name).exists(), name

    state = json.loads((out / "state.json").read_text(encoding="utf-8"))
    assert state["frames"] == 1
    assert state["errors"] == 0
    assert state["transcript"]["lines"] == len(BOOT_LINES)
    assert not list(out.glob("*.tmp"))


@needs_tesseract
def test_transcript_grows_only_when_the_screen_changes(tmp_path):
    scope, _ = build_scene(tmp_path)
    scope.step()
    first = scope.logbook.total_lines
    # Same screen re-photographed: the transcript must not double.
    scope.config["capture"]["ocr_threshold"] = 0.0   # force a second OCR pass
    scope.step()
    assert scope.logbook.total_lines == first


def test_uncalibrated_scope_still_produces_output(tmp_path):
    # Before calibration the raw frame is used as-is rather than failing.
    scope, _ = build_scene(tmp_path)
    scope.config["calibration"]["quad"] = None
    state = scope.step()
    assert state["calibrated"] is False
    assert (tmp_path / "out" / "raw.png").exists()


def test_source_failure_is_recorded_not_raised(tmp_path):
    from hvscope.sources import SourceError

    scope, _ = build_scene(tmp_path)

    def explode():
        raise SourceError("camera unplugged")

    scope.source.grab = explode
    state = scope.step()
    assert state["errors"] >= 1
    assert "camera unplugged" in state["last_error"]


@needs_tesseract
def test_ocr_is_held_back_until_the_frame_settles(tmp_path):
    scope, _ = build_scene(tmp_path)
    # A moving screen must not be OCR'd: half-drawn text reads as confident
    # nonsense, which is worse than no reading at all.
    scope.config["capture"]["stable_threshold"] = 0.0
    state = scope.step()
    assert state["stable"] is False
    assert state["last_ocr_at"] is None
