"""Text arriving over a wire: assembly, terminal codes, and real serial reads."""

import os
import pty
import shutil
import time

import pytest

from hvscope.textsources import (
    CommandTextSource,
    FileTailSource,
    LineAssembler,
    SerialSource,
    TextSourceError,
    apply_carriage_returns,
    build_text_source,
    is_text_kind,
    strip_terminal_codes,
)


def test_strip_terminal_codes_removes_colour_and_titles():
    assert strip_terminal_codes("\x1b[1;31mPANIC\x1b[0m: halted") == "PANIC: halted"
    assert strip_terminal_codes("\x1b]0;window title\x07text") == "text"
    assert strip_terminal_codes("a\x00b\x07c") == "abc"
    # Tabs carry column meaning and must survive.
    assert strip_terminal_codes("a\tb") == "a\tb"


def test_carriage_return_overwrites_rather_than_breaking_the_line():
    assert apply_carriage_returns("10%\r50%\r100% done") == "100% done"
    assert apply_carriage_returns("no cr here") == "no cr here"


def test_assembler_holds_back_a_partial_line():
    assembler = LineAssembler()
    assert assembler.feed(b"PANIC: vmla") == []
    assert assembler.pending() == "PANIC: vmla"
    assert assembler.feed(b"unch failed\n") == ["PANIC: vmlaunch failed"]
    assert assembler.pending() == ""


def test_assembler_handles_crlf_and_bare_lf():
    assert LineAssembler().feed(b"one\r\ntwo\nthree\r\n") == ["one", "two", "three"]


def test_assembler_survives_utf8_split_across_reads():
    payload = "héllo wörld\n".encode()
    assembler = LineAssembler()
    assert assembler.feed(payload[:6]) == []
    assert assembler.feed(payload[6:]) == ["héllo wörld"]


def test_assembler_recovers_from_an_invalid_byte():
    # A corrupt byte on the wire must not wedge the stream forever.
    assembler = LineAssembler()
    assembler.feed(b"\xff\xfe")
    assert assembler.feed(b"good line\n")[-1].endswith("good line")


def test_assembler_caps_a_stream_that_never_sends_a_newline():
    assembler = LineAssembler(max_pending=100)
    assembler.feed(b"x" * 5000)
    assert len(assembler.pending()) <= 100


def test_is_text_kind_separates_the_two_families():
    assert is_text_kind("serial") and is_text_kind("file")
    assert not is_text_kind("snapshot") and not is_text_kind("uvc")


def test_build_text_source_rejects_unknown_kinds():
    with pytest.raises(TextSourceError, match="unknown text source kind"):
        build_text_source({"kind": "nonsense"})


def test_serial_source_needs_a_port():
    with pytest.raises(TextSourceError, match="needs a port"):
        SerialSource("")


def test_serial_source_reports_a_missing_port():
    with pytest.raises(TextSourceError, match="cannot open"):
        SerialSource("/dev/ttyDoesNotExist99")


@pytest.mark.skipif(os.name != "posix", reason="pty is POSIX only")
def test_reads_a_real_serial_device():
    # A pty is a genuine TTY, so this exercises the same code path a
    # USB-serial adapter would take.
    master, slave = pty.openpty()
    try:
        source = SerialSource(os.ttyname(slave), 115200)
        os.write(master, b"\x1b[32mhvtest 0.4.1\x1b[0m\r\nCPU0: VMX enabled\r\n")
        time.sleep(0.2)
        assert source.read() == ["hvtest 0.4.1", "CPU0: VMX enabled"]

        # A line still in flight is visible but not yet filed.
        os.write(master, b"PANIC: vmlaunch fai")
        time.sleep(0.2)
        assert source.read() == []
        assert source.pending() == "PANIC: vmlaunch fai"

        os.write(master, b"led, error 7\r\n")
        time.sleep(0.2)
        assert source.read() == ["PANIC: vmlaunch failed, error 7"]
        assert source.read() == []
        source.close()
    finally:
        os.close(master)
        os.close(slave)


@pytest.mark.skipif(not shutil.which("printf"), reason="needs printf")
def test_command_text_source_reads_stdout():
    source = CommandTextSource(r"printf 'line one\nline two\n'")
    deadline = time.monotonic() + 5
    collected = []
    while time.monotonic() < deadline and len(collected) < 2:
        try:
            collected.extend(source.read())
        except TextSourceError:
            break
        time.sleep(0.05)
    source.close()
    assert collected == ["line one", "line two"]


def test_command_text_source_reports_a_missing_command():
    with pytest.raises(TextSourceError, match="command not found"):
        CommandTextSource("definitely-not-a-real-binary-xyz")


def test_file_tail_follows_appends(tmp_path):
    target = tmp_path / "boot.log"
    target.write_text("first\n", encoding="utf-8")
    source = FileTailSource(str(target))
    assert source.read() == ["first"]
    assert source.read() == []

    with target.open("a", encoding="utf-8") as handle:
        handle.write("second\n")
    assert source.read() == ["second"]


def test_file_tail_restarts_when_the_writer_truncates(tmp_path):
    target = tmp_path / "boot.log"
    target.write_text("old content here\n", encoding="utf-8")
    source = FileTailSource(str(target))
    source.read()
    # The machine rebooted and the log was recreated shorter.
    target.write_text("new\n", encoding="utf-8")
    assert source.read() == ["new"]


def test_file_tail_can_skip_existing_content(tmp_path):
    target = tmp_path / "boot.log"
    target.write_text("history\n", encoding="utf-8")
    source = FileTailSource(str(target), from_start=False)
    assert source.read() == []
