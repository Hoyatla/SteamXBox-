"""Synthesise a camera photo of a console screen.

Used by the tests so the whole pipeline can be exercised with no tablet, no
camera and no hypervisor: the same perspective, moire, lighting and noise
problems are injected deliberately, and the ground-truth text is known.
"""

from __future__ import annotations

import numpy as np
from PIL import Image, ImageDraw, ImageFont

MONO_CANDIDATES = [
    "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
    "/usr/share/fonts/truetype/liberation/LiberationMono-Regular.ttf",
    "/Library/Fonts/Menlo.ttc",
    "C:/Windows/Fonts/consola.ttf",
]

BOOT_LINES = [
    "hvtest hypervisor 0.4.1 (x86_64)",
    "CPU0: vendor GenuineIntel family 6 model 154",
    "VMX: capability MSR 0xda040000000fff",
    "EPT: 4-level paging enabled, 1G pages supported",
    "VMCS: region allocated at 0x000000007ff21000",
    "APIC: xAPIC mode, base 0xfee00000",
    "MEM: 16384 MB usable, 8 e820 entries",
    "GUEST: entry point 0x00000000001000ec",
    "PANIC: vmlaunch failed, error 7",
]


def load_font(size: int) -> ImageFont.FreeTypeFont:
    for path in MONO_CANDIDATES:
        try:
            return ImageFont.truetype(path, size)
        except OSError:
            continue
    raise RuntimeError("no monospace TTF found for the synthetic screen")


def render_screen(lines=BOOT_LINES, size=(1280, 720), font_size=26) -> Image.Image:
    """A light-on-dark console, the way the panel itself would show it."""
    image = Image.new("RGB", size, (8, 10, 14))
    draw = ImageDraw.Draw(image)
    font = load_font(font_size)
    y = 40
    for line in lines:
        draw.text((48, y), line, font=font, fill=(228, 233, 238))
        y += int(font_size * 1.55)
    return image


def photograph(screen: Image.Image, canvas=(1400, 1000), quad=None, *, moire=14.0,
               gradient=55.0, noise=4.0, seed=0) -> tuple[Image.Image, list[tuple[float, float]]]:
    """Warp, dim and dirty a screen the way a tablet camera would.

    Returns the fake photo and the true corner quad inside it, so a test can
    calibrate exactly and still exercise the real dewarp maths.
    """
    width, height = canvas
    if quad is None:
        # A plausible off-axis, slightly rotated view.
        quad = [(196.0, 128.0), (1214.0, 205.0), (1160.0, 812.0), (150.0, 726.0)]

    # Forward-map the screen into the quad. PIL samples destination->source,
    # so the coefficients run from the photo back to the flat screen.
    from hvscope.geometry import perspective_coeffs

    src_rect = [(0.0, 0.0), (float(screen.width), 0.0),
                (float(screen.width), float(screen.height)), (0.0, float(screen.height))]
    coeffs = perspective_coeffs(quad, src_rect)
    warped = screen.transform((width, height), Image.PERSPECTIVE, coeffs, Image.BICUBIC)

    array = np.asarray(warped, dtype=np.float32)
    rng = np.random.default_rng(seed)

    # Uneven room lighting across the panel.
    ramp_x = np.linspace(-1.0, 1.0, width, dtype=np.float32)
    ramp_y = np.linspace(-1.0, 1.0, height, dtype=np.float32)
    glow = (ramp_y[:, None] * 0.6 + ramp_x[None, :] * 0.4) * gradient
    array += glow[:, :, None]

    # Refresh-scan banding: horizontal, and it moves between exposures.
    phase = rng.uniform(0, 2 * np.pi)
    bands = np.sin(np.linspace(0, 60 * np.pi, height, dtype=np.float32) + phase) * moire
    array += bands[:, None, None]

    array += rng.normal(0.0, noise, array.shape).astype(np.float32)
    return Image.fromarray(np.clip(array, 0, 255).astype(np.uint8), mode="RGB"), quad
