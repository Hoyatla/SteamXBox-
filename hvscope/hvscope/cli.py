"""Command line entry points."""

from __future__ import annotations

import argparse
import json
import signal
import sys
import threading
import time
from pathlib import Path

from . import __version__, config as config_module, geometry, ocr
from .daemon import build_scope
from .server import serve
from .sources import SourceError, build_source, list_video_devices
from .textsources import TextSourceError, build_text_source, is_text_kind


def _load(args) -> tuple[dict, Path, Path]:
    config, path = config_module.load(args.config)
    base = path.parent if path.exists() else Path.cwd()
    return config, path, config_module.output_dir(config, base)


def cmd_init(args) -> int:
    # Default to the working directory rather than to the home fallback:
    # writing a global config here would leave a later `probe` reading a file
    # the user is not the one editing.
    if args.config:
        path = Path(args.config).expanduser()
    elif args.user:
        path = Path.home() / ".config" / "hvscope" / config_module.CONFIG_NAME
    else:
        path = Path.cwd() / config_module.CONFIG_NAME
    if path.exists() and not args.force:
        print(f"{path} already exists (use --force to overwrite)", file=sys.stderr)
        return 1
    written = config_module.save(config_module.DEFAULT_CONFIG, path)
    print(f"wrote {written}")
    print("Next: set source.url to your camera, then run 'hvscope probe'.")
    return 0


def _probe_text(config, path, out, seconds: float) -> int:
    """Listen on a text source and report what arrives."""
    try:
        source = build_text_source(config["source"])
    except TextSourceError as exc:
        print(f"source error: {exc}", file=sys.stderr)
        return 2

    print(f"config     {path}")
    print(f"source     {source.describe()}")
    print(f"listening  {seconds:.0f}s ...")

    collected: list[str] = []
    deadline = time.monotonic() + seconds
    try:
        while time.monotonic() < deadline:
            try:
                collected.extend(source.read())
            except TextSourceError as exc:
                print(f"FAILED     {exc}", file=sys.stderr)
                return 2
            time.sleep(0.1)
    finally:
        pending = source.pending()
        source.close()

    print(f"received   {len(collected)} lines")
    for line in collected[-15:]:
        print(f"  > {line}")
    if pending:
        print(f"  ~ {pending}   (partial, no newline yet)")
    if not collected and not pending:
        print("nothing arrived. Check the wiring, the baud rate, and that the "
              "machine under test is actually sending.", file=sys.stderr)
        return 1
    return 0


def cmd_probe(args) -> int:
    """Grab one frame -- or listen for text -- and report what came back."""
    config, path, out = _load(args)
    if is_text_kind(config.get("source", {}).get("kind", "")):
        return _probe_text(config, path, out, float(args.seconds))

    try:
        source = build_source(config["source"])
    except SourceError as exc:
        print(f"source error: {exc}", file=sys.stderr)
        return 2

    print(f"config     {path}")
    print(f"source     {source.describe()}")
    started = time.monotonic()
    try:
        frame = source.grab()
    except SourceError as exc:
        print(f"FAILED     {exc}", file=sys.stderr)
        return 2
    elapsed = (time.monotonic() - started) * 1000

    out.mkdir(parents=True, exist_ok=True)
    target = out / "raw.png"
    frame.convert("RGB").save(target)
    print(f"frame      {frame.width}x{frame.height} {frame.mode} in {elapsed:.0f} ms")
    print(f"saved      {target}")
    print(f"tesseract  {ocr.version()}")
    quad = config.get("calibration", {}).get("quad")
    print(f"calibrated {'yes' if quad else 'no  (run hvscope watch, then open /calibrate)'}")
    source.close()
    return 0


def cmd_devices(args) -> int:
    """List the USB capture devices and serial ports on this machine."""
    print("video capture devices (HDMI dongles, webcams)")
    video = list_video_devices()
    if video:
        for entry in video:
            label = f"  {entry['device']}"
            if entry.get("name"):
                label += f"   {entry['name']}"
            print(f"{label}   [backend {entry['backend']}]")
        print('\n  use with:  "source": {"kind": "uvc", "device": "%s"}' % video[0]["device"])
    else:
        print("  none found")

    print("\nserial ports (USB-serial adapters, xHCI debug ports)")
    ports = _list_serial_ports()
    if ports:
        for device, description in ports:
            print(f"  {device}   {description}")
        print('\n  use with:  "source": {"kind": "serial", "port": "%s", "baudrate": 115200}'
              % ports[0][0])
    else:
        print("  none found")
    return 0


def _list_serial_ports() -> list[tuple[str, str]]:
    """Serial ports, via pyserial when present and by globbing otherwise."""
    try:
        from serial.tools import list_ports  # type: ignore
    except ImportError:
        pass
    else:
        return [(port.device, port.description or "") for port in list_ports.comports()]

    found: list[tuple[str, str]] = []
    for pattern in ("ttyUSB*", "ttyACM*", "ttyS*", "tty.usb*", "cu.usb*"):
        for node in sorted(Path("/dev").glob(pattern)):
            found.append((str(node), ""))
    return found


def cmd_calibrate(args) -> int:
    """Set the corner quad from the command line."""
    config, path, out = _load(args)
    if len(args.points) != 4:
        print("need exactly 4 --points x,y arguments", file=sys.stderr)
        return 2
    try:
        quad = [tuple(float(v) for v in p.split(",")) for p in args.points]
        if any(len(p) != 2 for p in quad):
            raise ValueError("each point must be x,y")
        ordered = geometry.order_quad(quad)
        size = tuple(args.size) if args.size else geometry.suggest_output_size(ordered)
        geometry.perspective_coeffs(
            [(0.0, 0.0), (float(size[0]), 0.0), (float(size[0]), float(size[1])), (0.0, float(size[1]))],
            ordered,
        )
    except ValueError as exc:
        print(f"bad calibration: {exc}", file=sys.stderr)
        return 2

    config.setdefault("calibration", {})
    config["calibration"]["quad"] = [[round(x, 2), round(y, 2)] for x, y in ordered]
    config["calibration"]["output_size"] = [int(size[0]), int(size[1])]
    config_module.save(config, path)
    print(f"quad        {config['calibration']['quad']}")
    print(f"output_size {config['calibration']['output_size']}")
    print(f"saved       {path}")
    return 0


def cmd_shot(args) -> int:
    """One capture cycle; print the text it read."""
    config, path, out = _load(args)
    # A single shot has no previous frame to compare against, so force the
    # OCR rather than waiting for a stability signal that cannot arrive.
    config.setdefault("capture", {})["stable_threshold"] = 100.0
    try:
        scope = build_scope(config, out)
    except (SourceError, TextSourceError) as exc:
        print(f"source error: {exc}", file=sys.stderr)
        return 2

    state = scope.step()
    scope.close()
    if state.get("last_error"):
        print(f"warning: {state['last_error']}", file=sys.stderr)
    text = (out / "screen.txt").read_text(encoding="utf-8") if (out / "screen.txt").exists() else ""
    sys.stdout.write(text if text.endswith("\n") or not text else text + "\n")
    return 0 if text.strip() else 1


def cmd_watch(args) -> int:
    """Run the capture loop and the HTTP API until interrupted."""
    config, path, out = _load(args)
    try:
        scope = build_scope(config, out)
    except (SourceError, TextSourceError) as exc:
        print(f"source error: {exc}", file=sys.stderr)
        return 2

    server_conf = config.get("server", {})
    host = args.host or server_conf.get("host", "0.0.0.0")
    port = int(args.port or server_conf.get("port", 8765))
    token = args.token or server_conf.get("token", "")

    httpd = serve(scope, host, port, token, path)
    shown = "localhost" if host in ("0.0.0.0", "::") else host
    print(f"hvscope {__version__}")
    print(f"source    {scope.source.describe()}")
    print(f"output    {out}")
    print(f"api       http://{shown}:{port}/   (dashboard)")
    print(f"          http://{shown}:{port}/text  /log  /state.json  /frame.png")
    if scope.mode == "camera" and not config.get("calibration", {}).get("quad"):
        print(f"calibrate http://{shown}:{port}/calibrate  <- not calibrated yet")
    if host == "0.0.0.0" and not token:
        print("warning:  bound to all interfaces with no token; anyone on the LAN can see the camera")
    print("Ctrl-C to stop.")

    stop = threading.Event()

    def handle(signum, frame):
        stop.set()

    signal.signal(signal.SIGINT, handle)
    signal.signal(signal.SIGTERM, handle)

    def on_cycle(state):
        if args.quiet:
            return
        lines = state.get("new_lines") or []
        suffix = f"  +{len(lines)} new" if lines else ""
        if state.get("mode") == "text":
            head = (f"\rread {state['frames']:5d}  "
                    f"transcript {state['transcript'].get('lines', 0):5d} lines{suffix}   ")
        else:
            flag = "stable" if state.get("stable") else "moving"
            head = (f"\rframe {state['frames']:5d}  {flag}  diff {state['change_score']:6.2f}%"
                    f"  text {state['text_lines']:3d} lines{suffix}   ")
        print(head, end="", flush=True)
        if lines:
            # Break out of the in-place status line before listing what is new.
            print()
            for line in lines:
                print(f"  > {line}", flush=True)

    try:
        scope.run(stop, on_cycle)
    finally:
        httpd.shutdown()
        scope.close()
        print("\nstopped.")
    return 0


def cmd_state(args) -> int:
    config, path, out = _load(args)
    target = out / "state.json"
    if not target.exists():
        print(f"no state at {target}; is hvscope watch running?", file=sys.stderr)
        return 1
    print(json.dumps(json.loads(target.read_text(encoding="utf-8")), indent=2, sort_keys=True))
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="hvscope",
        description="Read a machine's screen through a camera and serve it as text.",
    )
    parser.add_argument("--version", action="version", version=f"hvscope {__version__}")
    parser.add_argument("-c", "--config", help="path to hvscope.json")
    sub = parser.add_subparsers(dest="command", required=True)

    p = sub.add_parser("init", help="write a default hvscope.json")
    p.add_argument("--force", action="store_true", help="overwrite an existing config")
    p.add_argument("--user", action="store_true",
                   help="write to ~/.config/hvscope/ instead of the current directory")
    p.set_defaults(func=cmd_init)

    p = sub.add_parser("probe", help="grab one frame, or listen for text, and report")
    p.add_argument("--seconds", type=float, default=5.0,
                   help="how long to listen on a text source (default 5)")
    p.set_defaults(func=cmd_probe)

    p = sub.add_parser("devices", help="list USB capture devices and serial ports")
    p.set_defaults(func=cmd_devices)

    p = sub.add_parser("calibrate", help="set the screen-corner quad without the web UI")
    p.add_argument("--points", nargs=4, metavar="X,Y", required=True,
                   help="the four screen corners in source-image pixels")
    p.add_argument("--size", nargs=2, type=int, metavar=("W", "H"),
                   help="output rectangle (default: derived from the quad)")
    p.set_defaults(func=cmd_calibrate)

    p = sub.add_parser("shot", help="capture once and print the text")
    p.set_defaults(func=cmd_shot)

    p = sub.add_parser("watch", help="run the capture loop and the HTTP API")
    p.add_argument("--host", help="override server.host")
    p.add_argument("--port", type=int, help="override server.port")
    p.add_argument("--token", help="require this token on every request")
    p.add_argument("-q", "--quiet", action="store_true", help="no per-frame progress line")
    p.set_defaults(func=cmd_watch)

    p = sub.add_parser("state", help="print the last state.json")
    p.set_defaults(func=cmd_state)
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        return args.func(args)
    except KeyboardInterrupt:
        return 130
    except (ValueError, TextSourceError, SourceError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
