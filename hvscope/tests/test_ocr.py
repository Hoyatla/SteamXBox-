import pytest
from PIL import Image, ImageDraw

from hvscope import ocr
from synth import load_font

needs_tesseract = pytest.mark.skipif(not ocr.available(), reason="tesseract not installed")


def test_fix_hex_literals_repairs_only_hex_context():
    assert ocr.fix_hex_literals("base Oxfee00000") == "base 0xfee00000"
    assert ocr.fix_hex_literals("at Ox00l0f5") == "at 0x0010f5"
    # Valid hex digits must survive untouched.
    assert ocr.fix_hex_literals("0xdeadbeef") == "0xdeadbeef"
    assert ocr.fix_hex_literals("0xabcdef") == "0xabcdef"
    # An ordinary word starting with "Ox" is not an address.
    assert ocr.fix_hex_literals("the Oxygen box") == "the Oxygen box"


def test_clean_collapses_blank_runs_and_trailing_space():
    assert ocr.clean("a\n\n\n\nb   \n\n") == "a\n\nb"
    assert ocr.clean("") == ""


def test_version_reports_missing_binary(monkeypatch):
    monkeypatch.setattr(ocr.shutil, "which", lambda _: None)
    assert ocr.version() == "not installed"
    with pytest.raises(ocr.OcrUnavailable, match="not on PATH"):
        ocr.run(Image.new("L", (10, 10), 255))


def _render(lines, size=(900, 320)):
    image = Image.new("L", size, 255)
    draw = ImageDraw.Draw(image)
    font = load_font(28)
    for index, line in enumerate(lines):
        draw.text((20, 20 + index * 44), line, font=font, fill=0)
    return image


@needs_tesseract
def test_reads_clean_console_text():
    lines = ["PANIC: vmlaunch failed", "error code 7"]
    text, confidence = ocr.run(_render(lines))
    assert "PANIC" in text and "vmlaunch" in text
    assert "error code 7" in text
    assert confidence > 60


@needs_tesseract
def test_preserves_column_alignment():
    text, _ = ocr.run(_render(["RAX 0001    RBX 0002"], size=(700, 90)))
    # The wide gap between the two columns must not collapse to one space.
    assert "  " in text.strip()


@needs_tesseract
def test_confidence_filter_drops_low_scoring_lines():
    image = _render(["REAL LINE HERE"], size=(700, 120))
    kept, _ = ocr.run(image, min_confidence=0.0)
    assert "REAL" in kept
    # Nothing clears an impossible bar, and that is reported as empty.
    dropped, confidence = ocr.run(image, min_confidence=99.9)
    assert dropped == "" and confidence == 0.0


@needs_tesseract
def test_read_lines_returns_confidence_per_line():
    lines = ocr.read_lines(_render(["alpha bravo", "charlie delta"]))
    assert len(lines) >= 2
    assert all(0 <= line.confidence <= 100 for line in lines)
    assert lines[0].top <= lines[1].top
