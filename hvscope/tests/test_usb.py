"""The two USB paths: a capture device delivering pixels, a wire delivering text."""

import json
import os
import pty
import shutil
import time

import pytest

from hvscope.config import DEFAULT_CONFIG, deep_merge
from hvscope.daemon import Scope, TextScope, build_scope
from hvscope.sources import SourceError, UvcSource, build_source, default_backend

needs_ffmpeg = pytest.mark.skipif(not shutil.which("ffmpeg"), reason="ffmpeg not installed")
needs_pty = pytest.mark.skipif(os.name != "posix", reason="pty is POSIX only")


# -- USB carrying pixels (HDMI capture dongle) --------------------------------

def test_uvc_is_reachable_through_the_source_factory():
    source = build_source({"kind": "uvc", "device": "/dev/video0", "size": "1920x1080"})
    assert isinstance(source, UvcSource)
    assert "/dev/video0" in source.describe()


def test_unknown_kind_names_the_usb_options():
    with pytest.raises(SourceError, match="uvc"):
        build_source({"kind": "nonsense"})


@needs_ffmpeg
def test_uvc_builds_a_sane_ffmpeg_command(tmp_path):
    source = UvcSource(device="/dev/video2", size="1280x720", input_format="mjpeg", warmup=3)
    command = source._command(tmp_path / "f.png")
    assert command[:1] == ["ffmpeg"]
    assert "-f" in command and default_backend() in command
    assert "/dev/video2" in command
    assert "1280x720" in command and "mjpeg" in command
    # Warm-up frames are discarded inside the same invocation.
    assert any("select=gte(n" in part for part in command)


@needs_ffmpeg
def test_uvc_grabs_a_frame_from_a_real_device():
    # lavfi stands in for the dongle: same ffmpeg code path, no hardware.
    source = UvcSource(device="testsrc=size=640x480:rate=10", backend="lavfi", warmup=3)
    frame = source.grab()
    assert frame.size == (640, 480)


@needs_ffmpeg
def test_uvc_reports_a_missing_device_clearly():
    with pytest.raises(SourceError, match="could not read"):
        UvcSource(device="/dev/video99", backend="v4l2", timeout=10).grab()


@needs_ffmpeg
def test_uvc_times_out_on_a_device_that_never_delivers():
    with pytest.raises(SourceError, match="timed out"):
        UvcSource(device="testsrc=rate=1", backend="lavfi", warmup=100000, timeout=3).grab()


# -- USB carrying text (serial / debug port) ----------------------------------

@needs_pty
def test_build_scope_picks_the_text_loop_for_a_serial_source(tmp_path):
    master, slave = pty.openpty()
    try:
        config = deep_merge(DEFAULT_CONFIG, {
            "source": {"kind": "serial", "port": os.ttyname(slave), "baudrate": 115200},
        })
        scope = build_scope(config, tmp_path / "out")
        assert isinstance(scope, TextScope)
        assert scope.mode == "text"
        scope.close()
    finally:
        os.close(master)
        os.close(slave)


def test_build_scope_picks_the_camera_loop_for_an_image_source(tmp_path):
    config = deep_merge(DEFAULT_CONFIG, {"source": {"kind": "uvc", "device": "/dev/video0"}})
    if not shutil.which("ffmpeg"):
        pytest.skip("ffmpeg not installed")
    scope = build_scope(config, tmp_path / "out")
    assert isinstance(scope, Scope) and scope.mode == "camera"
    scope.close()


@needs_pty
def test_text_scope_transcribes_a_serial_boot_log(tmp_path):
    master, slave = pty.openpty()
    try:
        config = deep_merge(DEFAULT_CONFIG, {
            "source": {"kind": "serial", "port": os.ttyname(slave), "baudrate": 115200},
        })
        scope = build_scope(config, tmp_path / "out")

        os.write(master, b"\x1b[1;32mhvtest hypervisor 0.4.1\x1b[0m\r\nCPU0: VMX enabled\r\n")
        time.sleep(0.2)
        first = scope.step()
        # Colour codes are stripped; the text is exact, not recognised.
        assert first["new_lines"] == ["hvtest hypervisor 0.4.1", "CPU0: VMX enabled"]

        os.write(master, b"loading 10%\rloading 100%\r\nPANIC: vmlaunch failed, error 7\r\n")
        time.sleep(0.2)
        second = scope.step()
        # The progress counter overwrote itself; only the final state existed.
        assert second["new_lines"] == ["loading 100%", "PANIC: vmlaunch failed, error 7"]

        transcript = (tmp_path / "out" / "log.txt").read_text(encoding="utf-8")
        assert "PANIC: vmlaunch failed, error 7" in transcript
        assert "loading 10%" not in transcript
        scope.close()
    finally:
        os.close(master)
        os.close(slave)


@needs_pty
def test_text_mode_state_omits_the_camera_only_fields(tmp_path):
    master, slave = pty.openpty()
    try:
        config = deep_merge(DEFAULT_CONFIG, {
            "source": {"kind": "serial", "port": os.ttyname(slave)},
        })
        scope = build_scope(config, tmp_path / "out")
        os.write(master, b"hello\r\npartial line")
        time.sleep(0.2)
        state = scope.step()

        assert state["mode"] == "text"
        # Nothing was recognised, so there is no confidence to report.
        for camera_only in ("confidence", "stable", "change_score", "calibrated", "ocr_available"):
            assert camera_only not in state
        # What a reader wants instead is the half-line still in flight.
        assert state["pending"] == "partial line"
        scope.close()
    finally:
        os.close(master)
        os.close(slave)


@needs_pty
def test_text_mode_writes_no_images(tmp_path):
    master, slave = pty.openpty()
    try:
        config = deep_merge(DEFAULT_CONFIG, {
            "source": {"kind": "serial", "port": os.ttyname(slave)},
        })
        scope = build_scope(config, tmp_path / "out")
        os.write(master, b"line\r\n")
        time.sleep(0.2)
        scope.step()

        produced = {entry.name for entry in (tmp_path / "out").iterdir()}
        assert produced == {"screen.txt", "log.txt", "state.json"}
        state = json.loads((tmp_path / "out" / "state.json").read_text(encoding="utf-8"))
        assert state["transcript"]["lines"] == 1
        scope.close()
    finally:
        os.close(master)
        os.close(slave)


def test_text_scope_keeps_a_rolling_tail(tmp_path):
    # screen.txt is the stream's analogue of "what is on screen now".
    log = tmp_path / "boot.log"
    log.write_text("".join(f"line {i}\n" for i in range(50)), encoding="utf-8")
    config = deep_merge(DEFAULT_CONFIG, {
        "source": {"kind": "file", "path": str(log)},
        "output": {"tail": 10},
    })
    scope = build_scope(config, tmp_path / "out")
    scope.step()
    tail = (tmp_path / "out" / "screen.txt").read_text(encoding="utf-8").strip().splitlines()
    assert tail == [f"line {i}" for i in range(40, 50)]
    # The transcript keeps everything, even though the tail does not.
    assert scope.logbook.total_lines == 50
    scope.close()


def test_text_source_failure_is_recorded_not_raised(tmp_path):
    from hvscope.textsources import TextSourceError

    log = tmp_path / "boot.log"
    log.write_text("one\n", encoding="utf-8")
    config = deep_merge(DEFAULT_CONFIG, {"source": {"kind": "file", "path": str(log)}})
    scope = build_scope(config, tmp_path / "out")

    def explode():
        raise TextSourceError("cable unplugged")

    scope.source.read = explode
    state = scope.step()
    assert state["errors"] >= 1
    assert "cable unplugged" in state["last_error"]
    scope.close()
