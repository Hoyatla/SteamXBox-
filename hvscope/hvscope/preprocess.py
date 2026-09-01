"""Turn a photo of a screen into something Tesseract can read.

Photographing an LCD adds three problems on top of ordinary OCR: moire
banding from the refresh scan, uneven lighting across the panel, and console
text that is light-on-dark when OCR engines expect dark-on-light. Each stage
here targets one of those.
"""

from __future__ import annotations

from typing import Sequence

import numpy as np
from PIL import Image

# Below this local standard deviation a window holds no edge, so it is flat
# background rather than a glyph. Without this guard a local threshold splits
# sensor noise in an empty region into "text".
MIN_CONTRAST = 25.0


def median_stack(images: Sequence[Image.Image]) -> Image.Image:
    """Median-combine frames shot back to back from a fixed camera.

    The refresh scan lands in a different place in each exposure, so the
    median of a few frames keeps the steady pixels and drops the banding.
    """
    if not images:
        raise ValueError("median_stack needs at least one image")
    if len(images) == 1:
        return images[0].convert("RGB")

    reference = images[0].convert("RGB")
    size = reference.size
    frames = [np.asarray(reference, dtype=np.uint8)]
    for image in images[1:]:
        frame = image.convert("RGB")
        if frame.size != size:
            frame = frame.resize(size, Image.BICUBIC)
        frames.append(np.asarray(frame, dtype=np.uint8))

    stacked = np.median(np.stack(frames, axis=0), axis=0)
    return Image.fromarray(stacked.astype(np.uint8), mode="RGB")


def to_gray(image: Image.Image) -> np.ndarray:
    """Grayscale as float32 in 0..255."""
    return np.asarray(image.convert("L"), dtype=np.float32)


def _box_mean(array: np.ndarray, block: int) -> np.ndarray:
    """Mean over a square window, via an integral image.

    Windows are clamped at the edges, so border pixels average over the part
    of the window that exists instead of over padding that does not.
    """
    if block < 3:
        block = 3
    if block % 2 == 0:
        block += 1
    radius = block // 2

    height, width = array.shape
    integral = np.zeros((height + 1, width + 1), dtype=np.float64)
    integral[1:, 1:] = array.cumsum(axis=0).cumsum(axis=1)

    rows = np.arange(height)
    cols = np.arange(width)
    y0 = np.clip(rows - radius, 0, height)
    y1 = np.clip(rows + radius + 1, 0, height)
    x0 = np.clip(cols - radius, 0, width)
    x1 = np.clip(cols + radius + 1, 0, width)

    total = (
        integral[np.ix_(y1, x1)]
        - integral[np.ix_(y0, x1)]
        - integral[np.ix_(y1, x0)]
        + integral[np.ix_(y0, x0)]
    )
    area = np.outer(y1 - y0, x1 - x0).astype(np.float64)
    return (total / np.maximum(area, 1.0)).astype(np.float32)


def local_mean(gray: np.ndarray, block: int) -> np.ndarray:
    """Windowed mean brightness."""
    return _box_mean(gray.astype(np.float64), block)


def local_std(gray: np.ndarray, block: int) -> np.ndarray:
    """Windowed standard deviation, from the mean of squares."""
    values = gray.astype(np.float64)
    mean = _box_mean(values, block).astype(np.float64)
    mean_of_squares = _box_mean(values * values, block).astype(np.float64)
    return np.sqrt(np.maximum(mean_of_squares - mean * mean, 0.0)).astype(np.float32)


def otsu_threshold(gray: np.ndarray) -> float:
    """Global threshold maximising between-class variance.

    When several thresholds tie -- which happens whenever the two tones are
    far apart and nothing lies between them -- the middle of the tied range is
    returned rather than its first entry, so noise on either mode stays on the
    correct side.
    """
    counts, edges = np.histogram(gray, bins=256, range=(0.0, 256.0))
    counts = counts.astype(np.float64)
    total = counts.sum()
    if total <= 0:
        return 128.0

    centers = (edges[:-1] + edges[1:]) / 2.0
    weight_bg = np.cumsum(counts)
    weight_fg = total - weight_bg
    valid = (weight_bg > 0) & (weight_fg > 0)
    if not valid.any():
        return 128.0

    cumulative = np.cumsum(counts * centers)
    mean_bg = np.divide(cumulative, weight_bg, out=np.zeros_like(cumulative), where=weight_bg > 0)
    mean_fg = np.divide(
        cumulative[-1] - cumulative, weight_fg, out=np.zeros_like(cumulative), where=weight_fg > 0
    )
    variance = weight_bg * weight_fg * (mean_bg - mean_fg) ** 2
    variance[~valid] = -np.inf

    best = variance.max()
    tied = np.flatnonzero(variance >= best - 1e-9)
    return float(centers[int(tied[len(tied) // 2])])


def background_is_dark(gray: np.ndarray) -> bool:
    """True for light-on-dark screens (the usual console).

    Decided from which tone covers more of the panel, which survives uneven
    lighting far better than comparing absolute brightness to a fixed level.
    """
    split = otsu_threshold(gray)
    return bool((gray <= split).mean() > 0.5)


def binarize(gray: np.ndarray, mode: str = "adaptive", block: int = 41, offset: float = 10.0,
             polarity: str = "auto", min_contrast: float = MIN_CONTRAST) -> Image.Image:
    """Produce black ink on a white page, whatever the screen's polarity.

    ``polarity`` is one of ``auto``, ``light-on-dark`` (bright console text) or
    ``dark-on-light``. Auto-detection asks which tone dominates the panel.
    """
    if mode == "none":
        return Image.fromarray(np.clip(gray, 0.0, 255.0).astype(np.uint8), mode="L")

    if polarity == "auto":
        dark_bg = background_is_dark(gray)
    elif polarity == "light-on-dark":
        dark_bg = True
    elif polarity == "dark-on-light":
        dark_bg = False
    else:
        raise ValueError(f"unknown polarity: {polarity!r}")

    if mode == "adaptive":
        reference = local_mean(gray, block)
        # A flat window carries no glyph, whatever its mean says.
        textured = local_std(gray, block) >= float(min_contrast)
        if dark_bg:
            ink = (gray > reference + float(offset)) & textured
        else:
            ink = (gray < reference - float(offset)) & textured
    elif mode == "otsu":
        split = otsu_threshold(gray)
        ink = (gray > split) if dark_bg else (gray < split)
    else:
        raise ValueError(f"unknown threshold mode: {mode!r}")

    return Image.fromarray(np.where(ink, 0, 255).astype(np.uint8), mode="L")


def upscale(image: Image.Image, factor: float) -> Image.Image:
    """Enlarge before OCR; Tesseract wants roughly 30px-tall capitals."""
    if factor is None or abs(float(factor) - 1.0) < 1e-3:
        return image
    if float(factor) <= 0:
        raise ValueError(f"upscale factor must be positive, got {factor}")
    width = max(1, int(round(image.width * float(factor))))
    height = max(1, int(round(image.height * float(factor))))
    return image.resize((width, height), Image.LANCZOS)


def change_score(previous: np.ndarray | None, current: np.ndarray) -> float:
    """Percentage-style difference between two grayscale frames.

    Used to tell "the boot is still progressing" from "the machine is wedged",
    and to hold OCR back until the frame settles.
    """
    if previous is None or previous.shape != current.shape:
        return 100.0
    return float(np.abs(previous - current).mean() / 255.0 * 100.0)


def prepare_for_ocr(image: Image.Image, settings: dict) -> Image.Image:
    """Full grayscale -> threshold -> upscale chain."""
    gray = to_gray(image)
    binary = binarize(
        gray,
        mode=settings.get("threshold", "adaptive"),
        block=int(settings.get("block", 41)),
        offset=float(settings.get("offset", 10.0)),
        polarity=settings.get("polarity", "auto"),
        min_contrast=float(settings.get("min_contrast", MIN_CONTRAST)),
    )
    return upscale(binary, float(settings.get("upscale", 2.0)))
