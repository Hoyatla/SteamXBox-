import math

import numpy as np
import pytest
from PIL import Image

from hvscope.geometry import dewarp, order_quad, perspective_coeffs, suggest_output_size


def test_order_quad_normalises_any_starting_corner():
    corners = [(10, 10), (320, 15), (300, 200), (5, 210)]
    expected = order_quad(corners)
    for shift in range(4):
        rotated = corners[shift:] + corners[:shift]
        assert order_quad(rotated) == expected
    assert order_quad(list(reversed(corners))) == expected


def test_order_quad_rejects_wrong_count():
    with pytest.raises(ValueError, match="exactly 4"):
        order_quad([(0, 0), (1, 1), (2, 2)])


def test_perspective_coeffs_map_destination_corners_onto_source():
    src = [(20.0, 12.0), (200.0, 30.0), (190.0, 150.0), (12.0, 130.0)]
    dst = [(0.0, 0.0), (100.0, 0.0), (100.0, 60.0), (0.0, 60.0)]
    a, b, c, d, e, f, g, h = perspective_coeffs(dst, src)

    for (dx, dy), (sx, sy) in zip(dst, src):
        denominator = g * dx + h * dy + 1.0
        assert (a * dx + b * dy + c) / denominator == pytest.approx(sx, abs=1e-6)
        assert (d * dx + e * dy + f) / denominator == pytest.approx(sy, abs=1e-6)


def test_perspective_coeffs_rejects_degenerate_quad():
    collapsed = [(0.0, 0.0), (0.0, 0.0), (0.0, 0.0), (0.0, 0.0)]
    dst = [(0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0)]
    with pytest.raises(ValueError, match="degenerate"):
        perspective_coeffs(dst, collapsed)


def test_dewarp_recovers_a_rectangle_from_a_skewed_view():
    # A bright marker in a known corner must land back in that same corner.
    source = Image.new("RGB", (400, 300), (0, 0, 0))
    for x in range(0, 40):
        for y in range(0, 30):
            source.putpixel((x, y), (255, 255, 255))

    quad = [(50.0, 40.0), (350.0, 70.0), (330.0, 260.0), (40.0, 240.0)]
    coeffs = perspective_coeffs(
        quad, [(0.0, 0.0), (400.0, 0.0), (400.0, 300.0), (0.0, 300.0)]
    )
    photo = source.transform((400, 300), Image.PERSPECTIVE, coeffs, Image.BICUBIC)

    flat = np.asarray(dewarp(photo, quad, (400, 300)).convert("L"), dtype=np.float32)
    assert flat[10, 10] > 200          # marker restored to the top-left
    assert flat[150, 200] < 60         # and the rest is still dark


def test_suggest_output_size_preserves_aspect_ratio():
    assert suggest_output_size([(0, 0), (1000, 0), (1000, 560), (0, 560)]) == (1000, 560)
    width, height = suggest_output_size([(0, 0), (4000, 0), (4000, 2250), (0, 2250)])
    assert width == 1600
    assert math.isclose(width / height, 16 / 9, rel_tol=0.02)
