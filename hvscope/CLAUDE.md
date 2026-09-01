# hvscope — notes for the agent reading this output

This repo runs a camera pointed at another machine's screen and turns what it
shows into text. If you are debugging something that has no other way to report
— a hypervisor before any OS, a firmware hang, a kernel panic on a box with no
serial port — this is your eye on it.

## Read this first

**The output is OCR. It is lossy, and it is confidently wrong sometimes.**

- Never trust a single character. `0`/`O`, `1`/`l`, `5`/`S`, `8`/`B` are
  routinely confused.
- Long runs of identical digits are the worst case. `0x0000000000000000` may
  come back with a stray digit changed.
- Before acting on an address or an error code, read it twice and check that
  both readings agree.
- Check `confidence` in `state.json`. Below ~60 treat the reading as a hint,
  not a fact.

Hex literals written `0x…` are auto-repaired for characters that cannot be hex
digits at all, so those are more reliable than bare hex.

## Where the output is

Files under `out/` — read them directly, this is the cheap path:

- **`out/log.txt`** — every distinct line the screen has shown since start,
  timestamped. **Start here.** It holds the messages that scrolled away.
- `out/screen.txt` — only what is on screen right now.
- `out/state.json` — is the machine still progressing, and how good is the read.
- `out/screen.png` — the binarised image the OCR was given. Look at this when
  the text makes no sense; it usually shows why immediately.
- `out/screen-color.png` — the flattened screen, for anything OCR cannot carry
  (a graphical glitch, a colour, a progress bar).
- `out/history/` — a ring buffer of recent screens, `NNNN.png` with matching
  `NNNN.txt`. Where to look for the frame just before a crash.

Or over HTTP, if hvscope runs on another host: `/text`, `/log?n=200`,
`/state.json`, `/frame.png`.

## Reading state.json

```json
{
  "stable": true,          // frame settled; OCR only runs when this is true
  "change_score": 0.08,    // % pixel difference from the previous frame
  "confidence": 86.4,      // mean OCR confidence of the lines kept
  "screen_changed_at": "...",  // last time the screen actually changed
  "last_ocr_at": "...",
  "errors": 0,
  "last_error": null,
  "calibrated": true       // false => nobody has clicked the corners yet
}
```

The distinction that matters when a boot hangs:

- `change_score` near zero and `screen_changed_at` not advancing → **the screen
  is frozen.** The machine is wedged, or waiting.
- `change_score` fluctuating → something is still redrawing. Not hung.
- `confidence` low and falling → suspect the camera (focus drift, someone
  nudged the tablet, a light changed), not the machine under test.

## Failure modes to recognise

| What you see | What it usually means |
|---|---|
| `calibrated: false` | Nobody clicked the corners. Text will be poor or absent. |
| `last_error` mentions a URL | The tablet's camera app stopped or its IP changed. |
| Confidence collapses suddenly | The tablet moved. Calibration is now wrong. |
| Text present but garbled | Look at `out/screen.png` before touching any setting. |
| Transcript stops growing | Either the screen genuinely stopped, or OCR is being held back because the frame never settles — check `stable`. |
| Empty `screen.txt`, high frame count | Everything is being filtered out; `ocr.min_confidence` may be too high. |

## Do not

- Do not report an address, opcode or error code to the user without saying it
  came from OCR and may be misread.
- Do not conclude a machine is hung from one frame. Check `screen_changed_at`
  across a few seconds.
- Do not tune thresholds before looking at `out/screen.png`. Almost every bad
  read is visible there, and is usually a camera problem rather than a
  software one.

## Layout

```
hvscope/
  geometry.py    perspective correction (homography, corner ordering)
  preprocess.py  median stacking, local thresholding, change detection
  sources.py     frame sources: snapshot, mjpeg, dir, command
  ocr.py         tesseract via TSV; confidence filter, column rebuild, hex repair
  logbook.py     the append-only transcript and its de-duplication
  daemon.py      the capture loop
  server.py      HTTP API and the calibration page
  cli.py         init / probe / calibrate / shot / watch / state
tests/
  synth.py       generates fake camera photos; the suite needs no hardware
```

Run the tests with `pytest`. They cover the maths and the full pipeline
against a synthesised photo, so a change that breaks recognition fails loudly.
