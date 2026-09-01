"""Sources that deliver text directly, with no camera and no OCR in the way.

When the machine under test can be made to *say* something rather than only
display it -- over a USB-serial adapter, or over the xHCI Debug Capability
that turns one of its own USB3 ports into a device-mode debug port -- the
characters arrive exactly as they were sent. No perspective, no lighting, no
misread digits.

Everything downstream of this module is unchanged: the same transcript, the
same de-duplication, the same state file and the same HTTP API. Only the
lossy stage disappears.
"""

from __future__ import annotations

import codecs
import os
import re
import shlex
import subprocess
import threading
from pathlib import Path

# CSI sequences (colour, cursor moves), OSC strings (window titles), and the
# short two-character escapes. Boot loaders emit all three.
_ANSI = re.compile(
    r"\x1b\[[0-?]*[ -/]*[@-~]"
    r"|\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)"
    r"|\x1b[@-Z\\-_]"
)
# Everything else non-printable except tab, which carries column meaning.
_CONTROL = re.compile(r"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]")


class TextSourceError(RuntimeError):
    """Raised when a text stream cannot be opened or read."""


def strip_terminal_codes(text: str) -> str:
    """Remove ANSI escapes and stray control bytes, keeping tabs."""
    return _CONTROL.sub("", _ANSI.sub("", text))


def apply_carriage_returns(line: str) -> str:
    """Resolve a lone CR the way a terminal would: it overwrites the line.

    A spinner or a percentage counter sends ``10%\\r20%\\r30%\\n``. Treating
    each CR as a line break would flood the transcript with states that were
    never separately visible; only the final text was.
    """
    if "\r" not in line:
        return line
    return line.rsplit("\r", 1)[-1]


class LineAssembler:
    """Turn an arbitrarily chunked byte stream into whole lines.

    Serial data arrives split at no particular boundary -- mid-line, mid-UTF-8
    sequence, mid-escape. Partial input is held back until it completes rather
    than being emitted as garbage.
    """

    def __init__(self, max_pending: int = 64 * 1024):
        self._buffer = ""
        # An incremental decoder holds back a multi-byte sequence split across
        # two reads, and substitutes a replacement character for a genuinely
        # corrupt byte instead of stalling on it. Doing this by hand gets the
        # second case wrong.
        self._decoder = codecs.getincrementaldecoder("utf-8")("replace")
        self.max_pending = int(max_pending)

    def feed(self, chunk: bytes) -> list[str]:
        """Add bytes; return the lines that are now complete."""
        if not chunk:
            return []

        self._buffer += self._decoder.decode(chunk).replace("\r\n", "\n")

        lines: list[str] = []
        while "\n" in self._buffer:
            head, self._buffer = self._buffer.split("\n", 1)
            cleaned = strip_terminal_codes(apply_carriage_returns(head)).rstrip()
            lines.append(cleaned)

        # A device that never sends a newline must not grow the buffer forever.
        if len(self._buffer) > self.max_pending:
            self._buffer = self._buffer[-self.max_pending :]
        return lines

    def pending(self) -> str:
        """The partial line received so far, if any."""
        return strip_terminal_codes(apply_carriage_returns(self._buffer)).rstrip()


class TextSource:
    """A thing that yields newly arrived text."""

    name = "text"

    def read(self) -> list[str]:
        raise NotImplementedError

    def pending(self) -> str:
        return ""

    def describe(self) -> str:
        return self.name

    def close(self) -> None:
        pass


class SerialSource(TextSource):
    """Read lines from a serial port.

    Covers a USB-serial adapter, and equally the TTY that the Linux
    ``usb_debug`` driver exposes for a machine attached through the xHCI
    Debug Capability -- from here they are the same thing.

    pyserial is used when installed, since it is the only portable way to set
    a baud rate; on POSIX a termios fallback keeps the dependency optional.
    """

    name = "serial"

    def __init__(self, port: str, baudrate: int = 115200, timeout: float = 0.2):
        if not port:
            raise TextSourceError("serial source needs a port (e.g. /dev/ttyUSB0 or COM3)")
        self.port = port
        self.baudrate = int(baudrate)
        self._assembler = LineAssembler()
        self._serial = None
        self._fd = None
        self._backend = ""

        try:
            import serial  # type: ignore
        except ImportError:
            self._open_posix()
        else:
            try:
                self._serial = serial.Serial(port, self.baudrate, timeout=0)
            except Exception as exc:  # serial.SerialException and friends
                raise TextSourceError(f"cannot open {port} at {self.baudrate} baud: {exc}") from exc
            self._backend = "pyserial"

    def _open_posix(self) -> None:
        if os.name != "posix":
            raise TextSourceError(
                "reading a serial port on this platform needs pyserial (pip install pyserial)"
            )
        try:
            import termios
        except ImportError as exc:
            raise TextSourceError("termios is unavailable; install pyserial instead") from exc

        try:
            self._fd = os.open(self.port, os.O_RDONLY | os.O_NOCTTY | os.O_NONBLOCK)
        except OSError as exc:
            raise TextSourceError(f"cannot open {self.port}: {exc}") from exc

        try:
            settings = termios.tcgetattr(self._fd)
        except termios.error:
            # A pipe or pty slave has no line discipline to configure; reading
            # it raw still works, which is what the tests rely on.
            self._backend = "raw fd"
            return

        speed = getattr(termios, f"B{self.baudrate}", None)
        if speed is None:
            os.close(self._fd)
            self._fd = None
            raise TextSourceError(f"unsupported baud rate {self.baudrate} without pyserial")

        # Raw mode: no echo, no signal handling, no input translation.
        iflag, oflag, cflag, lflag, ispeed, ospeed, cc = settings
        iflag = 0
        oflag = 0
        lflag = 0
        cflag = (cflag & ~termios.CSIZE) | termios.CS8 | termios.CREAD | termios.CLOCAL
        cflag &= ~(termios.PARENB | termios.CSTOPB)
        cc = list(cc)
        cc[termios.VMIN] = 0
        cc[termios.VTIME] = 0
        termios.tcsetattr(
            self._fd, termios.TCSANOW, [iflag, oflag, cflag, lflag, speed, speed, cc]
        )
        self._backend = "termios"

    def read(self) -> list[str]:
        try:
            if self._serial is not None:
                waiting = self._serial.in_waiting
                chunk = self._serial.read(waiting) if waiting else b""
            elif self._fd is not None:
                try:
                    chunk = os.read(self._fd, 65536)
                except BlockingIOError:
                    chunk = b""
            else:
                raise TextSourceError("serial port is closed")
        except OSError as exc:
            raise TextSourceError(f"{self.port} read failed: {exc}") from exc
        return self._assembler.feed(chunk)

    def pending(self) -> str:
        return self._assembler.pending()

    def describe(self) -> str:
        return f"serial {self.port} @ {self.baudrate} ({self._backend})"

    def close(self) -> None:
        if self._serial is not None:
            self._serial.close()
            self._serial = None
        if self._fd is not None:
            os.close(self._fd)
            self._fd = None


class CommandTextSource(TextSource):
    """Read the stdout of a long-running command.

    The general escape hatch for anything that can stream text: a vendor
    debug tool, ``adb logcat``, ``socat`` against an exotic device, or a
    reader written for a debug port this module does not know about.
    """

    name = "command"

    def __init__(self, command: str):
        if not command:
            raise TextSourceError("command text source needs a command")
        self.command = command
        self._assembler = LineAssembler()
        self._chunks: list[bytes] = []
        self._lock = threading.Lock()

        try:
            self._process = subprocess.Popen(
                shlex.split(command), stdout=subprocess.PIPE, stderr=subprocess.DEVNULL
            )
        except FileNotFoundError as exc:
            raise TextSourceError(f"command not found: {command}") from exc
        except OSError as exc:
            raise TextSourceError(f"cannot start {command}: {exc}") from exc

        # A reader thread keeps this portable; non-blocking pipe reads are
        # POSIX-only.
        self._reader = threading.Thread(target=self._pump, name="hvscope-cmd", daemon=True)
        self._reader.start()

    def _pump(self) -> None:
        stream = self._process.stdout
        if stream is None:
            return
        while True:
            chunk = stream.read1(65536) if hasattr(stream, "read1") else stream.read(65536)
            if not chunk:
                return
            with self._lock:
                self._chunks.append(chunk)

    def read(self) -> list[str]:
        with self._lock:
            chunk = b"".join(self._chunks)
            self._chunks.clear()
        lines = self._assembler.feed(chunk)
        if not chunk and self._process.poll() is not None:
            raise TextSourceError(
                f"command exited with code {self._process.returncode}: {self.command}"
            )
        return lines

    def pending(self) -> str:
        return self._assembler.pending()

    def describe(self) -> str:
        return f"command {self.command}"

    def close(self) -> None:
        if self._process.poll() is None:
            self._process.terminate()
            try:
                self._process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self._process.kill()


class FileTailSource(TextSource):
    """Follow a growing file, the way ``tail -f`` does.

    Useful when something else already writes the log and hvscope only has to
    turn it into a transcript an agent can read.
    """

    name = "file"

    def __init__(self, path: str, from_start: bool = True):
        self.path = Path(path).expanduser()
        self._assembler = LineAssembler()
        self._offset = 0
        if not from_start and self.path.exists():
            self._offset = self.path.stat().st_size

    def read(self) -> list[str]:
        if not self.path.exists():
            return []
        try:
            size = self.path.stat().st_size
            # Truncation means the writer restarted; follow it from the top.
            if size < self._offset:
                self._offset = 0
            with self.path.open("rb") as handle:
                handle.seek(self._offset)
                chunk = handle.read()
                self._offset = handle.tell()
        except OSError as exc:
            raise TextSourceError(f"cannot read {self.path}: {exc}") from exc
        return self._assembler.feed(chunk)

    def pending(self) -> str:
        return self._assembler.pending()

    def describe(self) -> str:
        return f"file {self.path}"


TEXT_KINDS = {"serial", "command-text", "file"}


def is_text_kind(kind: str) -> bool:
    return (kind or "").lower() in TEXT_KINDS


def build_text_source(config: dict) -> TextSource:
    """Instantiate the text source named by a config block."""
    kind = (config.get("kind") or "").lower()
    if kind == "serial":
        return SerialSource(
            config.get("port", ""),
            int(config.get("baudrate", 115200)),
        )
    if kind == "command-text":
        return CommandTextSource(config.get("command", ""))
    if kind == "file":
        return FileTailSource(config.get("path", ""), bool(config.get("from_start", True)))
    raise TextSourceError(f"unknown text source kind: {kind!r} (expected {', '.join(sorted(TEXT_KINDS))})")
