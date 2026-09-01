# hvscope

Read a machine's screen through a camera, and serve what it says as **text** —
so an AI coding agent on another machine can see a boot screen it has no other
way to reach.

Built for debugging a bare-metal hypervisor: the machine under test has no OS,
no serial port and no network stack, so nothing on it can report what went
wrong. A tablet camera pointed at its screen can.

It also reads over USB — either an HDMI capture dongle delivering pixels, or a
serial/debug port delivering text with no OCR at all. See `docs/USB.md`.

```
ASUS laptop (hypervisor, no OS)
      │  light
      ▼
Galaxy Tab running an IP camera app  ──HTTP──┐
                                             │  LAN
                                             ▼
                          PC running hvscope + Claude Code
                             ├── dewarp  (undo the camera angle)
                             ├── OCR     (with confidence filtering)
                             └── writes  out/screen.txt, out/log.txt
```

## Why text, not video

A 1080p screenshot costs an agent thousands of tokens and cannot be diffed. The
same screen as text costs a couple of hundred, can be grepped, and — because
`out/log.txt` accumulates every distinct line ever shown — preserves the boot
messages that scrolled past two seconds ago. That transcript is the thing you
actually debug from.

## Where the input comes from

| `source.kind` | What it reads | Notes |
|---|---|---|
| `snapshot` | one HTTP GET per frame | a phone/tablet camera app |
| `mjpeg` | a multipart HTTP stream | same, higher framerate |
| `uvc` | a USB video device | **HDMI capture dongle**; needs ffmpeg |
| `dir` | images from a folder | testing, no hardware |
| `command` | a command that writes an image | escape hatch |
| `serial` | **a serial or USB debug port** | no OCR; text is exact |
| `command-text` | a long-running command's stdout | vendor tools, `socat` |
| `file` | a growing log file | follows like `tail -f` |

The first six are read as images and go through OCR. The last three arrive as
text already, so nothing can be misread — `docs/USB.md` explains what it takes
to get a hypervisor to talk that way, and why you probably want both.

`hvscope devices` lists the USB capture devices and serial ports it can see.

## Install

```sh
pip install -e .
sudo apt install tesseract-ocr      # or: brew install tesseract
```

## Use

```sh
hvscope init                        # writes ./hvscope.json
$EDITOR hvscope.json                # set source.url to your camera
hvscope probe                       # one frame; checks the camera and OCR
hvscope watch                       # capture loop + HTTP API
```

Then open `http://<pc>:8765/calibrate` once and click the four corners of the
screen in the photo. Calibration is saved back to `hvscope.json`.

`docs/SETUP.md` covers the tablet side and the camera settings that matter.
`CLAUDE.md` is written for the agent that consumes the output.

## Output

Both as files (in `out/`) and over HTTP:

| File | Endpoint | What it is |
|---|---|---|
| `screen.txt` | `/text` | what the screen says right now |
| `log.txt` | `/log` | every distinct line since start, timestamped |
| `state.json` | `/state.json` | stability, confidence, error, frame count |
| `screen.png` | `/frame.png` | the binarised image the OCR actually read |
| `screen-color.png` | `/screen-color.png` | the flattened screen |
| `raw.png` | `/raw.png` | the untouched camera frame |

`/` is a dashboard; `/calibrate` is the corner picker.

## How it works

1. **Burst + median stack** — several exposures are combined. The LCD refresh
   scan falls in a different place in each, so the median removes the banding.
2. **Dewarp** — a homography maps the calibrated screen quad back to a
   rectangle, so text sits on horizontal lines.
3. **Binarise** — a local-mean threshold handles uneven room lighting, guarded
   by a local-contrast floor so flat regions cannot be mistaken for glyphs.
   Polarity is detected, so light-on-dark consoles need no configuration.
4. **OCR** — Tesseract in TSV mode. Per-word confidence lets low-scoring lines
   be dropped, which is what stops the engine reading words out of noise. Word
   boxes let column alignment be rebuilt, so register dumps stay in columns.
5. **Transcribe** — new lines are appended to `log.txt`. Repeats are detected
   by folding each character onto its OCR-confusable class, so `enab1ed`
   matches `enabled` while `0x1000` and `0x9000` stay distinct.

## Accuracy, honestly

On a simulated camera photo (perspective, moire, lighting gradient, sensor
noise) the pipeline recovers every line with ~99% character accuracy and
invents nothing. Long runs of identical digits are the weak spot —
`0x0000000000000000` is where misreads concentrate — and confidence drops
accordingly, which is why `state.json` reports it.

**Never trust a single character from OCR.** Check `confidence`, and confirm
an address against more than one reading before acting on it.

## Two modes

`state.json` carries a `mode` field. In `camera` mode it also reports
`stable`, `change_score`, `calibrated` and `confidence`; in `text` mode those
do not exist — there is nothing to stabilise and nothing to be uncertain about
— and `pending` holds the line still in flight instead.

## Tests

```sh
pip install -e ".[dev]"
pytest
```

The suite includes an end-to-end test that synthesises a camera photo of a
console screen — warp, moire, lighting gradient and noise — and asserts the
text comes back. No hardware needed.

## Licence

MIT.
