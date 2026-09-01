# Reading over USB

"Over USB" covers two entirely different things, and the difference matters
more than any setting in this repo.

| | Family A — USB carries **pixels** | Family B — USB carries **text** |
|---|---|---|
| What moves | The display output | The characters the machine prints |
| Path | HDMI out → capture dongle → USB | Serial / debug port → USB |
| Accuracy | OCR: ~99%, and wrong sometimes | Exact. Nothing is recognised |
| Machine-under-test code | **None** | A driver, written by you |
| Sees firmware/POST | Yes, from the first pixel | Only once your code runs |
| hvscope source | `uvc` | `serial` |

**They are complements, not alternatives.** Family A sees everything but reads
imperfectly. Family B reads perfectly but only sees what your code chooses to
say, and only after it starts saying it. On a hypervisor that dies before it
gets that far, A is the one that tells you.

Both are implemented. Both feed the same transcript, the same `state.json` and
the same HTTP API, so nothing downstream changes when you switch.

---

## Family A — HDMI capture dongle

Buy a UVC ("USB video class") capture dongle — the MS2109 and MS2130 chipsets
are the common ones, €20–30. HDMI out of the machine under test, USB into the
machine running hvscope.

This is a large upgrade over a camera pointed at a screen: the image arrives
already rectangular, evenly lit, and free of moire. No calibration, no
reflections, nothing to knock out of alignment.

```sh
hvscope devices          # find the device string
```

```json
{
  "source": { "kind": "uvc", "device": "/dev/video0", "size": "1920x1080" },
  "calibration": { "quad": null },
  "capture": { "stack": 1 }
}
```

Clear the quad and drop the stack to 1: there is no camera angle to correct
and no refresh banding to median away, so both stages are pure cost.

`warmup` (default 2) discards the first frames of each grab. Capture hardware
needs a moment to settle its exposure and the first frame out of a cold device
is routinely black. Raise it if frames come back dark.

ffmpeg does the reading, so it must be installed. The backend is chosen per
platform (`v4l2` on Linux, `avfoundation` on macOS, `dshow` on Windows);
override it with `backend` if the guess is wrong.

---

## Family B — text over USB

Nothing is recognised here, so nothing can be misread. If you can get the
hypervisor to talk, take it.

```json
{ "source": { "kind": "serial", "port": "/dev/ttyUSB0", "baudrate": 115200 } }
```

```sh
hvscope devices          # lists serial ports too
hvscope probe --seconds 10
```

ANSI colour codes are stripped, `\r\n` and bare `\r` are handled the way a
terminal handles them (a progress counter that overwrites itself is recorded
once, at its final value), and a line still in flight shows up as `pending` in
`state.json` rather than being filed half-written.

### Getting the characters out of the machine under test

This is the part that is not a config change. Two routes:

**A UART, if the machine exposes one.** Straightforward — write bytes to
`0x3F8` and a USB-serial adapter picks them up. The problem is that modern
laptops almost never bring a UART out to anything you can reach. Check before
planning around it.

**The xHCI Debug Capability (DbC).** This is the real answer on a modern
laptop, and worth understanding: the xHCI controller can put *one* of the
machine's own USB3 ports into **device** mode, where it presents itself to
another computer as a USB device with two bulk endpoints. It is the one
mechanism by which a PC — which has only host ports — can be read like a
peripheral.

- On the hvscope machine, Linux's `usb_debug` driver binds it and gives you a
  `/dev/ttyUSB*`. From hvscope's point of view it is an ordinary serial port,
  so the `serial` source already handles it.
- On the machine under test, the hypervisor must initialise the DbC before it
  can send: DbC context, event ring, transfer rings, string descriptors. Real
  work, but bounded, and it runs extremely early — no PCI enumeration, no
  ACPI, no network stack. Linux does exactly this for `earlyprintk=xdbc`
  (`drivers/usb/early/xhci-dbc.c`), which is the reference worth reading.
- You need a **USB 3 debug cable** (a crossover cable — a normal one will not
  do); on some USB-C platforms a plain USB-C to USB-C cable works.
- Check the chipset first. DbC is well-trodden on Intel; on AMD it is far less
  reliable and may be absent.

### If something else already produces the log

```json
{ "source": { "kind": "file", "path": "/var/log/hv-boot.log" } }
{ "source": { "kind": "command-text", "command": "socat - /dev/ttyACM0,b115200" } }
```

`file` follows a growing file and restarts cleanly if the writer truncates it.
`command-text` reads any long-running command's stdout, which covers vendor
debug tools and anything you write yourself for a port hvscope does not know.

---

## Bonus: the tablet over USB instead of WiFi

If you are staying with the camera, you can still move its feed onto the USB
cable. Connect the tablet by USB, enable USB debugging, then:

```sh
adb forward tcp:8080 tcp:8080
```

and point the existing source at the local end:

```json
{ "source": { "kind": "snapshot", "url": "http://127.0.0.1:8080/shot.jpg" } }
```

The camera app keeps serving on the tablet; the frames now travel over USB.
Lower latency, and immune to whatever the WiFi is doing. No new code, and the
tablet charges while it works.

---

## Which to build first

1. **Camera** (already working) — zero code on the machine under test, sees
   firmware and POST. Use it to find out *where* the boot dies.
2. **HDMI dongle** — same reach, far better image, ~€25 and a config change.
   Worth it the moment the camera's misreads start costing you time.
3. **DbC** — perfect text, but only once your code runs and only after you
   write the DbC driver. Do it when you know the hypervisor gets far enough
   for it to matter, and keep 1 or 2 alongside it for everything that happens
   before that point.
