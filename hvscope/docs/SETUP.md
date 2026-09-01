# Setup

Two machines and a tablet:

- **ASUS laptop** — the machine under test. Nothing is installed on it. It only
  has to display something.
- **Galaxy Tab** — a network camera. No code runs on it either; an app does the
  work.
- **PC** — runs `hvscope` and the agent that reads its output.

## 1. Turn the tablet into a network camera

Install any app that serves the camera over HTTP. **IP Webcam** (free) is the
usual choice; anything exposing a still-image or MJPEG URL works.

Start the server in the app and note the URL it shows, e.g.
`http://192.168.1.42:8080`. The endpoints hvscope wants are:

- `http://192.168.1.42:8080/shot.jpg` — one still (use with `kind: "snapshot"`)
- `http://192.168.1.42:8080/video` — MJPEG stream (use with `kind: "mjpeg"`)

Start with `snapshot`. It resynchronises after every network hiccup, and a boot
log does not change fast enough to need the stream.

### App settings that decide whether this works

These matter more than anything in the software:

- **Lock the focus.** Autofocus hunts continuously against a flat screen and
  half your frames come back soft. Set manual focus and adjust it once.
- **Lock the exposure and white balance.** Auto-exposure re-levels the image
  between frames, which weakens median stacking and makes the change detector
  report motion that is not there.
- **Resolution**: 1920x1080 is plenty. Higher costs latency for no gain.
- **Keep the screen awake** while the server runs, and plug the tablet in.

## 2. Position the tablet

The camera should see the ASUS screen filling most of the frame.

- **Square-on beats close.** hvscope corrects perspective, but a steep angle
  compresses the far side of the screen into fewer pixels and the OCR suffers
  there first. Aim for within ~30° of straight on.
- **Kill reflections.** A lamp or window reflected in the panel destroys more
  text than any other single factor. Move the light, not the tablet.
- **Clamp everything.** Any movement invalidates the calibration. A cheap
  gooseneck holder is the difference between this working for an hour and
  working for a minute.
- **Turn the ASUS screen brightness up** and use the largest console font the
  firmware offers. Bigger glyphs beat every software trick here.

## 3. Install hvscope on the PC

```sh
git clone <this repo> && cd hvscope
pip install -e .
```

Tesseract is a separate binary:

```sh
sudo apt install tesseract-ocr        # Debian/Ubuntu
brew install tesseract                # macOS
winget install UB-Mannheim.TesseractOCR   # Windows
```

Check it: `hvscope probe` prints the version it found.

## 4. Configure

```sh
hvscope init          # writes ./hvscope.json in the current directory
```

Set the camera URL:

```json
{
  "source": { "kind": "snapshot", "url": "http://192.168.1.42:8080/shot.jpg" }
}
```

Then:

```sh
hvscope probe
```

It reports the frame size, the round-trip time and whether Tesseract is
present, and saves the frame to `out/raw.png` so you can check the framing.

## 5. Calibrate

```sh
hvscope watch
```

Open `http://localhost:8765/calibrate`, click the four corners of the ASUS
screen in the photo, and press **Save calibration**. Order does not matter. The
quad is written back to `hvscope.json`.

If you already know the corner pixel coordinates:

```sh
hvscope calibrate --points 196,128 1214,205 1160,812 150,726
```

**Recalibrate whenever the tablet moves.** There is no way for the software to
notice that it has.

## 6. Check it

`http://localhost:8765/` shows the flattened screen, the binarised image the
OCR reads, the current text and the transcript, refreshing every two seconds.

The binarised panel is the one to look at when results are poor: it shows
exactly what Tesseract was given.

## Tuning

Only if the dashboard shows a problem.

| Symptom | Setting | Direction |
|---|---|---|
| Faint or broken glyphs | `preprocess.offset` | lower (5) |
| Speckle read as text | `preprocess.min_contrast` | raise (35–50) |
| Noise words in the transcript | `ocr.min_confidence` | raise (55–70) |
| Real lines being dropped | `ocr.min_confidence` | lower (30) |
| Thick blotches, lost thin strokes | `preprocess.block` | raise (61–81) |
| Small text misread | `calibration.output_size`, `preprocess.upscale` | raise |
| Banding survives | `capture.stack` | raise (5–7) |
| "moving" when the screen is still | `capture.stable_threshold` | raise (0.6) |

`preprocess.polarity` is auto-detected. Force it to `light-on-dark` or
`dark-on-light` only if detection is visibly wrong on a mostly-full screen.

## Security

`hvscope watch` binds `0.0.0.0` by default so the PC can reach the tablet's
feed across the LAN — which also means anyone on that LAN can see your screen.
On an untrusted network, set a token:

```sh
hvscope watch --token "$(openssl rand -hex 16)"
```

Requests then need `?token=...` or an `X-Hvscope-Token` header. Or bind to
localhost with `--host 127.0.0.1` if the agent runs on the same PC.

## Reading over USB instead

Two options, both implemented and both better than a camera in their own way:
an HDMI capture dongle (pixels over USB, no perspective or moire to fight), or
a serial/debug port (text over USB, nothing recognised so nothing misread).

`hvscope devices` lists what this machine can see. **`docs/USB.md` covers both,
including what the hypervisor itself has to do to talk over a USB debug port.**

You can also keep the camera but move its feed onto the USB cable with
`adb forward` — same pipeline, no WiFi. That is in `docs/USB.md` too.
