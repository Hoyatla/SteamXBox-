"""Where frames come from.

The capture backend is deliberately pluggable. Today the tablet runs an IP
camera app and this fetches JPEG snapshots over the LAN; swapping in an HDMI
capture dongle later is a config change, not a rewrite.
"""

from __future__ import annotations

import io
import platform
import re
import shlex
import shutil
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
from pathlib import Path

from PIL import Image, UnidentifiedImageError


class SourceError(RuntimeError):
    """Raised when a frame cannot be obtained."""


class FrameSource:
    """A thing that yields still frames on demand."""

    name = "source"

    def grab(self) -> Image.Image:
        raise NotImplementedError

    def describe(self) -> str:
        return self.name

    def close(self) -> None:
        pass


def _decode(payload: bytes, origin: str) -> Image.Image:
    if not payload:
        raise SourceError(f"{origin} returned an empty body")
    try:
        image = Image.open(io.BytesIO(payload))
        image.load()
    except (UnidentifiedImageError, OSError) as exc:
        raise SourceError(f"{origin} did not return a decodable image: {exc}") from exc
    return image


class SnapshotSource(FrameSource):
    """One HTTP GET per frame.

    Works with any camera app exposing a still-image endpoint -- IP Webcam's
    ``/shot.jpg``, a UVC bridge, or a plain file server. Simple and it
    resynchronises after every network hiccup, which matters more than
    framerate when a boot log only changes a few times a second.
    """

    name = "snapshot"

    def __init__(self, url: str, timeout: float = 5.0):
        if not url:
            raise SourceError("snapshot source needs a url")
        self.url = url
        self.timeout = float(timeout)

    def grab(self) -> Image.Image:
        # Cache-busting: some camera apps happily serve a stale still.
        separator = "&" if "?" in self.url else "?"
        request = urllib.request.Request(
            f"{self.url}{separator}_t={int(time.time() * 1000)}",
            headers={"User-Agent": "hvscope/1.0", "Cache-Control": "no-cache"},
        )
        try:
            with urllib.request.urlopen(request, timeout=self.timeout) as response:
                payload = response.read()
        except (urllib.error.URLError, OSError, TimeoutError) as exc:
            raise SourceError(f"cannot reach {self.url}: {exc}") from exc
        return _decode(payload, self.url)

    def describe(self) -> str:
        return f"snapshot {self.url}"


class MjpegSource(FrameSource):
    """Pull frames out of a multipart MJPEG stream.

    Higher framerate than snapshotting because the connection stays open, at
    the cost of reconnect handling. Useful once you care about catching a
    panic that flashes past.
    """

    name = "mjpeg"
    _MAX_FRAME = 24 * 1024 * 1024

    def __init__(self, url: str, timeout: float = 10.0):
        if not url:
            raise SourceError("mjpeg source needs a url")
        self.url = url
        self.timeout = float(timeout)
        self._stream = None
        self._buffer = b""

    def _connect(self) -> None:
        request = urllib.request.Request(self.url, headers={"User-Agent": "hvscope/1.0"})
        try:
            self._stream = urllib.request.urlopen(request, timeout=self.timeout)
        except (urllib.error.URLError, OSError, TimeoutError) as exc:
            raise SourceError(f"cannot open stream {self.url}: {exc}") from exc
        self._buffer = b""

    def grab(self) -> Image.Image:
        if self._stream is None:
            self._connect()

        # Scan for a JPEG SOI/EOI pair rather than trusting the part headers,
        # which vary between camera apps.
        while True:
            start = self._buffer.find(b"\xff\xd8")
            end = self._buffer.find(b"\xff\xd9", start + 2) if start >= 0 else -1
            if start >= 0 and end > start:
                frame = self._buffer[start : end + 2]
                self._buffer = self._buffer[end + 2 :]
                return _decode(frame, self.url)

            if len(self._buffer) > self._MAX_FRAME:
                self.close()
                raise SourceError(f"no JPEG boundary found in {self._MAX_FRAME} bytes from {self.url}")

            try:
                chunk = self._stream.read(65536)
            except (OSError, TimeoutError) as exc:
                self.close()
                raise SourceError(f"stream {self.url} broke: {exc}") from exc
            if not chunk:
                self.close()
                raise SourceError(f"stream {self.url} ended")
            self._buffer += chunk

    def close(self) -> None:
        if self._stream is not None:
            try:
                self._stream.close()
            finally:
                self._stream = None

    def describe(self) -> str:
        return f"mjpeg {self.url}"


class DirectorySource(FrameSource):
    """Replay images from a folder, cycling forever.

    Lets the whole pipeline be developed and tested with no hardware attached.
    """

    name = "dir"
    _SUFFIXES = {".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff"}

    def __init__(self, path: str):
        self.path = Path(path).expanduser()
        if not self.path.is_dir():
            raise SourceError(f"not a directory: {self.path}")
        self._index = 0

    def _files(self) -> list[Path]:
        files = sorted(p for p in self.path.iterdir() if p.suffix.lower() in self._SUFFIXES)
        if not files:
            raise SourceError(f"no images in {self.path}")
        return files

    def grab(self) -> Image.Image:
        files = self._files()
        chosen = files[self._index % len(files)]
        self._index += 1
        try:
            image = Image.open(chosen)
            image.load()
        except (UnidentifiedImageError, OSError) as exc:
            raise SourceError(f"cannot read {chosen}: {exc}") from exc
        return image

    def describe(self) -> str:
        return f"dir {self.path}"


class CommandSource(FrameSource):
    """Run a command that writes one image, then read it.

    The escape hatch: ``termux-camera-photo``, ``ffmpeg -f v4l2`` against a
    capture dongle, ``adb exec-out screencap`` -- anything that can produce a
    file. ``{out}`` in the command is replaced by the target path.
    """

    name = "command"

    def __init__(self, command: str, timeout: float = 30.0):
        if not command:
            raise SourceError("command source needs a command")
        self.command = command
        self.timeout = float(timeout)

    def grab(self) -> Image.Image:
        with tempfile.TemporaryDirectory(prefix="hvscope-") as tmp:
            target = Path(tmp) / "frame.jpg"
            rendered = self.command.replace("{out}", str(target))
            try:
                result = subprocess.run(
                    shlex.split(rendered),
                    capture_output=True,
                    timeout=self.timeout,
                    check=False,
                )
            except FileNotFoundError as exc:
                raise SourceError(f"command not found: {rendered}") from exc
            except subprocess.TimeoutExpired as exc:
                raise SourceError(f"command timed out after {self.timeout}s: {rendered}") from exc

            if result.returncode != 0:
                detail = result.stderr.decode("utf-8", "replace").strip()[:400]
                raise SourceError(f"command failed ({result.returncode}): {rendered}\n{detail}")
            if not target.exists() or target.stat().st_size == 0:
                raise SourceError(f"command produced no image at {target}: {rendered}")
            return _decode(target.read_bytes(), rendered)

    def describe(self) -> str:
        return f"command {self.command}"


# ffmpeg needs a different capture backend on each platform, and none of them
# can be guessed from the device string alone.
_FFMPEG_BACKENDS = {"Linux": "v4l2", "Darwin": "avfoundation", "Windows": "dshow"}
_DEFAULT_DEVICE = {"Linux": "/dev/video0", "Darwin": "0", "Windows": "video=Integrated Camera"}


def default_backend() -> str:
    return _FFMPEG_BACKENDS.get(platform.system(), "v4l2")


def ffmpeg_available() -> bool:
    return shutil.which("ffmpeg") is not None


class UvcSource(FrameSource):
    """Grab stills from a USB video-class device through ffmpeg.

    This is the path an HDMI capture dongle takes: the machine under test
    sends its display out over HDMI, the dongle presents it to this machine
    as a webcam, and the frames arrive already rectangular, evenly lit and
    free of moire. Calibration should be cleared when using it -- there is no
    camera angle left to correct.
    """

    name = "uvc"

    def __init__(self, device: str = "", backend: str = "", size: str = "",
                 input_format: str = "", warmup: int = 2, timeout: float = 30.0,
                 extra_args: list[str] | None = None):
        if not ffmpeg_available():
            raise SourceError("ffmpeg is not on PATH; it is what reads the USB capture device")
        self.backend = backend or default_backend()
        self.device = device or _DEFAULT_DEVICE.get(platform.system(), "/dev/video0")
        self.size = size
        self.input_format = input_format
        # Capture hardware needs a few frames to settle its exposure; the
        # first one out of a cold device is routinely black or blown out.
        self.warmup = max(0, int(warmup))
        self.timeout = float(timeout)
        self.extra_args = list(extra_args or [])

    def _command(self, target: Path) -> list[str]:
        command = ["ffmpeg", "-hide_banner", "-loglevel", "error", "-nostdin", "-y"]
        command += ["-f", self.backend]
        if self.input_format:
            command += ["-input_format", self.input_format]
        if self.size:
            command += ["-video_size", self.size]
        command += self.extra_args
        command += ["-i", self.device]
        if self.warmup:
            # Discard the settling frames inside the same invocation rather
            # than paying to open the device twice.
            command += ["-vf", f"select=gte(n\,{self.warmup})", "-vsync", "0"]
        command += ["-frames:v", "1", "-f", "image2", str(target)]
        return command

    def grab(self) -> Image.Image:
        with tempfile.TemporaryDirectory(prefix="hvscope-uvc-") as tmp:
            target = Path(tmp) / "frame.png"
            try:
                result = subprocess.run(
                    self._command(target), capture_output=True, timeout=self.timeout, check=False
                )
            except FileNotFoundError as exc:
                raise SourceError("ffmpeg disappeared from PATH") from exc
            except subprocess.TimeoutExpired as exc:
                raise SourceError(
                    f"ffmpeg timed out after {self.timeout}s reading {self.device}; "
                    "is the capture device streaming?"
                ) from exc

            if result.returncode != 0 or not target.exists() or target.stat().st_size == 0:
                detail = result.stderr.decode("utf-8", "replace").strip()[:500]
                raise SourceError(f"ffmpeg could not read {self.backend}:{self.device}\n{detail}")
            return _decode(target.read_bytes(), self.device)

    def describe(self) -> str:
        return f"uvc {self.backend}:{self.device}"


def list_video_devices() -> list[dict]:
    """Enumerate USB capture devices, so the user need not guess the name."""
    system = platform.system()

    if system == "Linux":
        devices = []
        for node in sorted(Path("/dev").glob("video*")):
            label = ""
            name_file = Path("/sys/class/video4linux") / node.name / "name"
            if name_file.exists():
                label = name_file.read_text(errors="replace").strip()
            devices.append({"device": str(node), "name": label, "backend": "v4l2"})
        return devices

    if not ffmpeg_available():
        return []

    backend = default_backend()
    probe = "" if system == "Darwin" else "dummy"
    try:
        result = subprocess.run(
            ["ffmpeg", "-hide_banner", "-f", backend, "-list_devices", "true", "-i", probe],
            capture_output=True, timeout=30, check=False,
        )
    except (OSError, subprocess.TimeoutExpired):
        return []

    # Both backends report the listing on stderr and then exit non-zero.
    text = result.stderr.decode("utf-8", "replace")
    devices = []
    if system == "Darwin":
        for match in re.finditer(r"\[(\d+)\]\s+(.+)", text):
            index, label = match.group(1), match.group(2).strip()
            if "AVFoundation" in label or not label:
                continue
            devices.append({"device": index, "name": label, "backend": backend})
    else:
        for match in re.finditer(r'"([^"]+)"\s*\(video\)', text):
            devices.append({"device": f"video={match.group(1)}", "name": match.group(1),
                            "backend": backend})
    return devices


def build_source(config: dict) -> FrameSource:
    """Instantiate the source named by a config block."""
    kind = (config.get("kind") or "snapshot").lower()
    timeout = float(config.get("timeout", 5.0))

    if kind == "snapshot":
        return SnapshotSource(config.get("url", ""), timeout)
    if kind == "mjpeg":
        return MjpegSource(config.get("url", ""), max(timeout, 10.0))
    if kind == "dir":
        return DirectorySource(config.get("path", ""))
    if kind == "command":
        return CommandSource(config.get("command", ""), max(timeout, 30.0))
    if kind == "uvc":
        return UvcSource(
            device=config.get("device", ""),
            backend=config.get("backend", ""),
            size=config.get("size", ""),
            input_format=config.get("input_format", ""),
            warmup=int(config.get("warmup", 2)),
            timeout=max(timeout, 30.0),
            extra_args=list(config.get("extra_args", [])),
        )
    raise SourceError(
        f"unknown source kind: {kind!r} "
        "(expected snapshot, mjpeg, uvc, dir or command)"
    )


def grab_burst(source: FrameSource, count: int, delay: float) -> list[Image.Image]:
    """Grab several frames back to back, for median stacking."""
    frames: list[Image.Image] = []
    for index in range(max(1, int(count))):
        if index and delay > 0:
            time.sleep(delay)
        frames.append(source.grab())
    return frames
