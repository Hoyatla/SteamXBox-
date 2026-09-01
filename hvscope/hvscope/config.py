"""Configuration loading, with defaults that work before calibration."""

from __future__ import annotations

import copy
import json
from pathlib import Path

DEFAULT_CONFIG: dict = {
    "source": {
        # Point this at the camera app on the tablet, e.g.
        # http://192.168.1.42:8080/shot.jpg for IP Webcam.
        "kind": "snapshot",
        "url": "http://192.168.1.42:8080/shot.jpg",
        "path": "",
        "command": "",
        "timeout": 5.0,
    },
    "capture": {
        "interval": 1.0,
        # Median-stacking a few exposures cancels the LCD refresh banding.
        "stack": 3,
        "stack_delay": 0.12,
        # Below this frame-to-frame difference the screen counts as settled.
        "stable_threshold": 0.35,
        # Re-run OCR when the settled frame differs from the last OCR'd one
        # by more than this, or when refresh_seconds elapses.
        "ocr_threshold": 0.20,
        "refresh_seconds": 30.0,
    },
    "calibration": {
        # Four screen corners in source-image pixels, any order. Null until
        # `hvscope calibrate` has run; frames are then used un-dewarped.
        "quad": None,
        "output_size": [1280, 720],
    },
    "preprocess": {
        "threshold": "adaptive",
        "block": 41,
        "offset": 10.0,
        "polarity": "auto",
        "min_contrast": 25.0,
        "upscale": 2.0,
    },
    "ocr": {
        "enabled": True,
        "lang": "eng",
        "psm": 6,
        # Lines the engine is less sure of than this are dropped; it is what
        # keeps camera noise and screen moire out of the transcript.
        "min_confidence": 45.0,
        "extra_args": [],
        "timeout": 60.0,
    },
    "logbook": {
        "window": 240,
        "min_length": 2,
    },
    "output": {
        "dir": "out",
        "history": 60,
    },
    "server": {
        # 0.0.0.0 so the Claude Code machine elsewhere on the LAN can read it.
        "host": "0.0.0.0",
        "port": 8765,
    },
}

CONFIG_NAME = "hvscope.json"


def deep_merge(base: dict, override: dict) -> dict:
    """Recursive merge; ``override`` wins on scalars."""
    result = copy.deepcopy(base)
    for key, value in (override or {}).items():
        if isinstance(value, dict) and isinstance(result.get(key), dict):
            result[key] = deep_merge(result[key], value)
        else:
            result[key] = copy.deepcopy(value)
    return result


def default_path(explicit: str | None = None) -> Path:
    if explicit:
        return Path(explicit).expanduser()
    local = Path.cwd() / CONFIG_NAME
    if local.exists():
        return local
    return Path.home() / ".config" / "hvscope" / CONFIG_NAME


def load(path: str | None = None) -> tuple[dict, Path]:
    """Load config merged over the defaults; missing file is not an error."""
    resolved = default_path(path)
    if not resolved.exists():
        return copy.deepcopy(DEFAULT_CONFIG), resolved
    try:
        stored = json.loads(resolved.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise ValueError(f"{resolved} is not valid JSON: {exc}") from exc
    if not isinstance(stored, dict):
        raise ValueError(f"{resolved} must contain a JSON object")
    return deep_merge(DEFAULT_CONFIG, stored), resolved


def save(config: dict, path: str | Path) -> Path:
    resolved = Path(path).expanduser()
    resolved.parent.mkdir(parents=True, exist_ok=True)
    tmp = resolved.with_suffix(resolved.suffix + ".tmp")
    tmp.write_text(json.dumps(config, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    tmp.replace(resolved)
    return resolved


def output_dir(config: dict, base: Path | None = None) -> Path:
    """Resolve the output directory, relative to the config file's folder."""
    raw = Path(config.get("output", {}).get("dir", "out")).expanduser()
    if raw.is_absolute():
        return raw
    return (base or Path.cwd()) / raw
