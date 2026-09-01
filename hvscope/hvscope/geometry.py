"""Perspective correction between a camera photo and the flat screen inside it.

A tablet propped in front of a laptop never sees the screen straight on: the
result is a trapezoid, not a rectangle. Calibration records where the four
screen corners land in the camera image; :func:`dewarp` maps that quad back
onto a rectangle so the text lines up horizontally for the OCR stage.
"""

from __future__ import annotations

import math
from typing import Iterable, Sequence

import numpy as np
from PIL import Image

Point = tuple[float, float]
Quad = list[Point]


def order_quad(points: Iterable[Sequence[float]]) -> Quad:
    """Order four points as top-left, top-right, bottom-right, bottom-left.

    Callers click the screen corners in whatever order they please, so the
    quad is normalised here rather than trusted.
    """
    pts = [(float(p[0]), float(p[1])) for p in points]
    if len(pts) != 4:
        raise ValueError(f"a quad needs exactly 4 points, got {len(pts)}")

    cx = sum(p[0] for p in pts) / 4.0
    cy = sum(p[1] for p in pts) / 4.0
    # Image coordinates put y downwards, so ascending atan2 walks clockwise.
    ordered = sorted(pts, key=lambda p: math.atan2(p[1] - cy, p[0] - cx))
    start = min(range(4), key=lambda i: ordered[i][0] + ordered[i][1])
    return ordered[start:] + ordered[:start]


def perspective_coeffs(dst_quad: Sequence[Point], src_quad: Sequence[Point]) -> tuple[float, ...]:
    """Solve the eight coefficients of PIL's PERSPECTIVE transform.

    PIL samples the *source* for every destination pixel, so the homography
    runs destination -> source, which is the inverse of the direction the
    calibration points are usually thought about.
    """
    rows: list[list[float]] = []
    rhs: list[float] = []
    for (dx, dy), (sx, sy) in zip(dst_quad, src_quad):
        rows.append([dx, dy, 1.0, 0.0, 0.0, 0.0, -dx * sx, -dy * sx])
        rows.append([0.0, 0.0, 0.0, dx, dy, 1.0, -dx * sy, -dy * sy])
        rhs.append(sx)
        rhs.append(sy)

    matrix = np.asarray(rows, dtype=np.float64)
    if abs(np.linalg.det(matrix)) < 1e-9:
        raise ValueError("degenerate calibration quad (collinear or duplicate corners)")
    solved = np.linalg.solve(matrix, np.asarray(rhs, dtype=np.float64))
    return tuple(float(v) for v in solved)


def dewarp(image: Image.Image, quad: Sequence[Sequence[float]], output_size: tuple[int, int]) -> Image.Image:
    """Flatten the calibrated quad of ``image`` into a rectangle."""
    width, height = int(output_size[0]), int(output_size[1])
    if width < 2 or height < 2:
        raise ValueError(f"output_size too small: {output_size}")

    dst: Quad = [(0.0, 0.0), (float(width), 0.0), (float(width), float(height)), (0.0, float(height))]
    coeffs = perspective_coeffs(dst, order_quad(quad))
    return image.transform((width, height), Image.PERSPECTIVE, coeffs, Image.BICUBIC)


def suggest_output_size(quad: Sequence[Sequence[float]], max_width: int = 1600) -> tuple[int, int]:
    """Pick a rectangle that roughly preserves the quad's aspect ratio."""
    tl, tr, br, bl = order_quad(quad)

    def dist(p: Point, q: Point) -> float:
        return math.hypot(p[0] - q[0], p[1] - q[1])

    width = max(dist(tl, tr), dist(bl, br))
    height = max(dist(tl, bl), dist(tr, br))
    if width < 1.0 or height < 1.0:
        return (1280, 720)

    out_w = min(max_width, int(round(width)))
    out_h = int(round(out_w * height / width))
    # Even dimensions keep downstream scaling free of half-pixel drift.
    return (max(out_w - out_w % 2, 2), max(out_h - out_h % 2, 2))
