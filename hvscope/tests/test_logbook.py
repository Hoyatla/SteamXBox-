import time

from hvscope.logbook import LogBook, canonical, normalise, write_json, write_text


def test_normalise_collapses_case_and_whitespace():
    assert normalise("  CPU0:   VMX  enabled ") == "cpu0: vmx enabled"


def test_first_ingest_keeps_every_line(tmp_path):
    book = LogBook(tmp_path / "log.txt")
    added = book.ingest("Booting hypervisor v0.3\nCPU0: VMX enabled")
    assert added == ["Booting hypervisor v0.3", "CPU0: VMX enabled"]
    assert book.total_lines == 2


def test_rereading_the_same_screen_adds_nothing(tmp_path):
    book = LogBook(tmp_path / "log.txt")
    screen = "Booting hypervisor v0.3\nCPU0: VMX enabled\nEPT: 4-level paging"
    book.ingest(screen)
    assert book.ingest(screen) == []


def test_ocr_jitter_is_not_mistaken_for_a_new_line(tmp_path):
    # The same physical line, re-read with one character misrecognised.
    book = LogBook(tmp_path / "log.txt")
    book.ingest("CPU0: VMX enabled\nEPT: 4-level paging")
    assert book.ingest("CPU0: VMX enab1ed\nEPT: 4-level paging") == []
    assert book.repeats >= 2


def test_a_genuinely_new_line_still_gets_through(tmp_path):
    book = LogBook(tmp_path / "log.txt")
    book.ingest("EPT: 4-level paging")
    assert book.ingest("EPT: 4-level paging\nVMCS: allocated 4096 bytes") == [
        "VMCS: allocated 4096 bytes"
    ]


def test_similar_but_meaningfully_different_lines_are_both_kept(tmp_path):
    # Differing addresses matter; de-duplication must not swallow them.
    book = LogBook(tmp_path / "log.txt")
    book.ingest("MAP: region 0x1000 -> 0x2000")
    added = book.ingest("MAP: region 0x9000 -> 0xa000")
    assert added == ["MAP: region 0x9000 -> 0xa000"]


def test_transcript_file_is_timestamped_and_appended(tmp_path):
    path = tmp_path / "log.txt"
    book = LogBook(path)
    book.ingest("first line", when=time.time())
    book.ingest("second line", when=time.time())
    lines = path.read_text(encoding="utf-8").splitlines()
    assert len(lines) == 2
    assert lines[0].endswith("first line")
    assert lines[0].startswith("[") and lines[0][3] == ":"


def test_short_noise_fragments_are_dropped(tmp_path):
    book = LogBook(tmp_path / "log.txt", min_length=2)
    assert book.ingest("a\n.\n \nreal line here") == ["real line here"]


def test_tail_returns_the_last_entries(tmp_path):
    book = LogBook(tmp_path / "log.txt")
    book.ingest("\n".join(f"line number {i}" for i in range(10)))
    assert len(book.tail(3).splitlines()) == 3
    assert "line number 9" in book.tail(3)


def test_reset_rotates_the_old_transcript(tmp_path):
    path = tmp_path / "log.txt"
    book = LogBook(path)
    book.ingest("something")
    book.reset()
    assert not path.exists()
    assert list(tmp_path.glob("log.txt.*"))
    assert book.total_lines == 0
    # A line from before the reset is new again afterwards.
    assert book.ingest("something") == ["something"]


def test_eviction_keeps_duplicates_of_a_still_present_line(tmp_path):
    # Two slots hold the same text; evicting one must not forget it.
    book = LogBook(tmp_path / "log.txt", window=3)
    book.ingest("alpha\nbravo\nalpha")
    book.ingest("charlie")
    assert book.ingest("alpha") == []


def test_atomic_writers_leave_no_temp_file(tmp_path):
    write_json(tmp_path / "state.json", {"a": 1})
    write_text(tmp_path / "screen.txt", "hello")
    assert (tmp_path / "state.json").read_text().strip().startswith("{")
    assert (tmp_path / "screen.txt").read_text() == "hello"
    assert not list(tmp_path.glob("*.tmp"))


def test_canonical_folds_confusable_glyphs_but_not_distinct_digits():
    assert canonical("CPU0: VMX enabled") == canonical("CPUO: VMX enab1ed")
    assert canonical("base 0xfee00000") == canonical("base Oxfee00000")
    # A different digit is a different line, however few characters differ.
    assert canonical("region 0x1000") != canonical("region 0x9000")
    assert canonical("retry 3 of 8") != canonical("retry 4 of 8")


def test_counters_that_differ_only_by_a_digit_are_all_kept(tmp_path):
    # The failure mode that matters: a progress counter must not collapse.
    book = LogBook(tmp_path / "log.txt")
    added = book.ingest("\n".join(f"page {i} mapped" for i in range(10)))
    assert len(added) == 10


def test_addresses_differing_in_one_hex_digit_are_kept(tmp_path):
    book = LogBook(tmp_path / "log.txt")
    book.ingest("MAP 0x00007ff0 -> 0x00001000")
    assert book.ingest("MAP 0x00007ff1 -> 0x00001000") != []
