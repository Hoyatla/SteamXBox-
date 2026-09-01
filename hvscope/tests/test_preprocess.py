import numpy as np
from PIL import Image

from hvscope.preprocess import (
    background_is_dark,
    binarize,
    change_score,
    local_mean,
    local_std,
    median_stack,
    otsu_threshold,
    upscale,
)


def test_local_mean_is_exact_on_a_flat_field_including_borders():
    flat = np.full((40, 60), 77.0, dtype=np.float32)
    assert np.allclose(local_mean(flat, 11), 77.0)


def test_local_std_is_zero_on_a_flat_field():
    assert np.allclose(local_std(np.full((30, 30), 12.0, dtype=np.float32), 9), 0.0, atol=1e-3)


def test_otsu_picks_the_middle_of_a_tied_range():
    # Nothing lies between the two modes, so every split in between ties;
    # the midpoint is the safe pick, not the first index.
    values = np.concatenate([np.full(5000, 20.0), np.full(5000, 200.0)]).astype(np.float32)
    assert 100.0 < otsu_threshold(values) < 120.0


def test_binarize_yields_black_ink_for_both_polarities():
    dark = np.full((100, 100), 10.0, dtype=np.float32)
    dark[40:50, 10:30] = 240.0
    assert background_is_dark(dark) is True
    out = np.asarray(binarize(dark))
    assert out[45, 20] == 0 and out[5, 5] == 255

    light = np.full((100, 100), 240.0, dtype=np.float32)
    light[40:50, 10:30] = 10.0
    assert background_is_dark(light) is False
    out = np.asarray(binarize(light))
    assert out[45, 20] == 0 and out[5, 5] == 255


def test_binarize_survives_a_strong_lighting_gradient():
    # What a camera actually delivers: the panel is brighter on one side.
    gradient = np.tile(np.linspace(5, 90, 200, dtype=np.float32), (150, 1))
    gradient[60:70, 20:60] += 150.0
    out = np.asarray(binarize(gradient))
    assert out[65, 40] == 0
    assert out[10, 190] == 255 and out[140, 10] == 255


def test_binarize_finds_no_ink_in_pure_noise():
    # The contrast guard's whole job: an empty screen must stay empty.
    rng = np.random.default_rng(0)
    noise = (np.full((120, 120), 30.0) + rng.normal(0, 3.0, (120, 120))).astype(np.float32)
    assert float((np.asarray(binarize(noise)) == 0).mean()) < 0.01


def test_median_stack_rejects_the_odd_frame_out():
    base = Image.new("RGB", (20, 20), (100, 100, 100))
    spike = Image.new("RGB", (20, 20), (255, 0, 0))
    stacked = np.asarray(median_stack([base, base, spike]))
    assert stacked[10, 10].tolist() == [100, 100, 100]


def test_median_stack_resizes_mismatched_frames():
    stacked = median_stack([Image.new("RGB", (20, 20), (10, 10, 10)),
                            Image.new("RGB", (40, 40), (10, 10, 10))])
    assert stacked.size == (20, 20)


def test_change_score_is_zero_for_identical_frames_and_max_for_none():
    frame = np.full((10, 10), 50.0, dtype=np.float32)
    assert change_score(frame, frame) == 0.0
    assert change_score(None, frame) == 100.0
    assert change_score(np.zeros((5, 5), dtype=np.float32), frame) == 100.0  # shape change


def test_upscale_is_a_no_op_at_unit_factor():
    image = Image.new("L", (10, 10))
    assert upscale(image, 1.0) is image
    assert upscale(image, 2.0).size == (20, 20)
