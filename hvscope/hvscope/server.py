"""HTTP API over the capture loop.

Two audiences. An agent reads ``/text``, ``/log`` and ``/state.json``; a human
opens ``/calibrate`` once to click the four screen corners, and ``/`` to check
the framing is right.
"""

from __future__ import annotations

import hmac
import json
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

from . import config as config_module
from . import geometry

CALIBRATION_PAGE = """<!doctype html>
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>hvscope calibration</title>
<style>
 :root{color-scheme:dark}
 body{margin:0;background:#111;color:#eee;font:14px/1.5 system-ui,sans-serif}
 header{padding:12px 16px;background:#1c1c1c;border-bottom:1px solid #333}
 h1{margin:0 0 4px;font-size:15px}
 p{margin:0;color:#aaa;font-size:13px}
 #wrap{position:relative;display:inline-block;margin:16px;max-width:calc(100% - 32px)}
 img{display:block;max-width:100%;height:auto;border:1px solid #444}
 .dot{position:absolute;width:18px;height:18px;margin:-9px 0 0 -9px;border-radius:50%;
      background:#2ea3ff;border:2px solid #fff;pointer-events:none;
      display:flex;align-items:center;justify-content:center;font-size:10px;font-weight:700;color:#fff}
 #bar{padding:0 16px 24px}
 button{font:inherit;padding:8px 14px;margin-right:8px;border-radius:6px;border:1px solid #444;
        background:#2a2a2a;color:#eee;cursor:pointer}
 button:disabled{opacity:.45;cursor:default}
 button.primary{background:#2ea3ff;border-color:#2ea3ff;color:#04121f;font-weight:600}
 code{background:#222;padding:1px 5px;border-radius:4px}
 #msg{margin-top:12px;min-height:1.5em}
 .ok{color:#5ad18a} .err{color:#ff7a7a}
</style>
<header>
 <h1>Calibration &mdash; click the four corners of the ASUS screen</h1>
 <p>Order does not matter. Click <code>top-left, top-right, bottom-right, bottom-left</code> or any
    rotation of it; the quad is normalised server-side.</p>
</header>
<div id="wrap"><img id="shot" src="raw.png" alt="camera frame"></div>
<div id="bar">
 <button id="undo">Undo last point</button>
 <button id="clear">Clear</button>
 <button id="save" class="primary" disabled>Save calibration</button>
 <button id="reload">Reload frame</button>
 <div id="msg"></div>
</div>
<script>
const wrap=document.getElementById('wrap'),img=document.getElementById('shot'),msg=document.getElementById('msg');
let pts=[];
function redraw(){
  wrap.querySelectorAll('.dot').forEach(d=>d.remove());
  pts.forEach((p,i)=>{
    const d=document.createElement('div');d.className='dot';d.textContent=i+1;
    d.style.left=(p.x*img.clientWidth)+'px';d.style.top=(p.y*img.clientHeight)+'px';
    wrap.appendChild(d);
  });
  document.getElementById('save').disabled=pts.length!==4;
}
img.addEventListener('click',e=>{
  if(pts.length>=4){msg.textContent='Four points already set - undo or clear first.';msg.className='err';return;}
  const r=img.getBoundingClientRect();
  pts.push({x:(e.clientX-r.left)/r.width,y:(e.clientY-r.top)/r.height});
  msg.textContent=pts.length+' / 4 points';msg.className='';redraw();
});
document.getElementById('undo').onclick=()=>{pts.pop();redraw();msg.textContent=pts.length+' / 4 points';msg.className='';};
document.getElementById('clear').onclick=()=>{pts=[];redraw();msg.textContent='';msg.className='';};
document.getElementById('reload').onclick=()=>{img.src='raw.png?t='+Date.now();};
window.addEventListener('resize',redraw);
document.getElementById('save').onclick=async()=>{
  // Send fractions; the server scales them by the true frame size, so a
  // browser-side resize cannot corrupt the calibration.
  const body={points:pts.map(p=>[p.x,p.y]),normalised:true};
  const r=await fetch('api/calibrate'+location.search,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
  const j=await r.json().catch(()=>({error:'bad response'}));
  if(r.ok){msg.textContent='Saved. Output '+j.output_size[0]+'x'+j.output_size[1]+' - check / for the flattened view.';msg.className='ok';}
  else{msg.textContent=j.error||('HTTP '+r.status);msg.className='err';}
};
</script>
"""

DASHBOARD_PAGE = """<!doctype html>
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>hvscope</title>
<style>
 :root{color-scheme:dark}
 body{margin:0;background:#111;color:#eee;font:14px/1.6 system-ui,sans-serif}
 header{padding:12px 16px;background:#1c1c1c;border-bottom:1px solid #333;display:flex;
        gap:16px;align-items:baseline;flex-wrap:wrap}
 h1{margin:0;font-size:15px} a{color:#2ea3ff}
 main{display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:16px;padding:16px}
 section{background:#1a1a1a;border:1px solid #333;border-radius:8px;padding:12px;min-width:0}
 h2{margin:0 0 8px;font-size:13px;text-transform:uppercase;letter-spacing:.06em;color:#9a9a9a}
 img{width:100%;height:auto;border:1px solid #333;border-radius:4px}
 pre{margin:0;white-space:pre-wrap;word-break:break-word;font:12px/1.45 ui-monospace,monospace;
     max-height:60vh;overflow:auto;background:#111;padding:8px;border-radius:4px}
 .kv{display:grid;grid-template-columns:auto 1fr;gap:2px 12px;font:12px/1.6 ui-monospace,monospace}
 .kv b{color:#9a9a9a;font-weight:400}
</style>
<header>
 <h1>hvscope</h1>
 <a href="calibrate" id="cal-link">calibrate</a><a href="text">/text</a><a href="log">/log</a>
 <a href="state.json">/state.json</a><span id="tick" style="color:#666"></span>
</header>
<main>
 <section id="s-flat"><h2>Flattened screen</h2><img id="flat" src="screen-color.png"></section>
 <section id="s-bin"><h2>What the OCR reads</h2><img id="bin" src="frame.png"></section>
 <section><h2>State</h2><div class="kv" id="state"></div></section>
 <section><h2 id="text-title">Current text</h2><pre id="text"></pre></section>
 <section style="grid-column:1/-1"><h2>Transcript</h2><pre id="log"></pre></section>
</main>
<script>
const q=location.search;
async function refresh(){
  const stamp='?t='+Date.now()+(q?'&'+q.slice(1):'');
  try{
    const s=await (await fetch('state.json'+q)).json();
    // A text stream has no frames, so hide the panels that would 404.
    const camera = s.mode !== 'text';
    document.getElementById('s-flat').hidden=!camera;
    document.getElementById('s-bin').hidden=!camera;
    document.getElementById('text-title').textContent=camera?'Current text':'Recent output';
    if(camera){
      document.getElementById('flat').src='screen-color.png'+stamp;
      document.getElementById('bin').src='frame.png'+stamp;
    }
    const rows = camera ? {
      mode:s.mode,frames:s.frames,stable:s.stable,'change %':s.change_score,
      calibrated:s.calibrated,'ocr ok':s.ocr_available,confidence:s.confidence,
      'text lines':s.text_lines,'transcript lines':(s.transcript||{}).lines,
      'last ocr':s.last_ocr_at||'-','screen changed':s.screen_changed_at||'-',
      errors:s.errors,'last error':s.last_error||'-'
    } : {
      mode:s.mode,source:s.source,reads:s.frames,
      'transcript lines':(s.transcript||{}).lines,'buffered lines':s.text_lines,
      pending:s.pending||'-','last line at':s.screen_changed_at||'-',
      errors:s.errors,'last error':s.last_error||'-'
    };
    document.getElementById('state').innerHTML=Object.entries(rows)
      .map(([k,v])=>'<b>'+k+'</b><span>'+String(v)+'</span>').join('');
    document.getElementById('text').textContent=await (await fetch('text'+q)).text();
    document.getElementById('log').textContent=await (await fetch('log'+q)).text();
    document.getElementById('cal-link').hidden=!camera;
    document.getElementById('tick').textContent='updated '+new Date().toLocaleTimeString();
  }catch(e){document.getElementById('tick').textContent='offline';}
}
refresh();setInterval(refresh,2000);
</script>
"""


class _Handler(BaseHTTPRequestHandler):
    server_version = "hvscope"
    scope = None
    token = ""
    config_path: Path | None = None

    def log_message(self, fmt, *args):  # quieter than the default stderr spam
        pass

    # -- helpers --------------------------------------------------------

    def _authorised(self, query: dict) -> bool:
        if not self.token:
            return True
        supplied = (query.get("token", [""])[0]) or self.headers.get("X-Hvscope-Token", "")
        return hmac.compare_digest(str(supplied), str(self.token))

    def _send(self, code: int, body: bytes, content_type: str) -> None:
        self.send_response(code)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)

    def _text(self, body: str, code: int = 200) -> None:
        self._send(code, body.encode("utf-8"), "text/plain; charset=utf-8")

    def _json(self, payload: dict, code: int = 200) -> None:
        self._send(code, json.dumps(payload, indent=2, sort_keys=True).encode("utf-8"),
                   "application/json; charset=utf-8")

    def _html(self, body: str) -> None:
        self._send(200, body.encode("utf-8"), "text/html; charset=utf-8")

    def _file(self, name: str, content_type: str) -> None:
        if getattr(self.scope, "mode", "camera") == "text":
            self._text(
                f"{name} is not produced in text mode: this source delivers "
                "characters, not frames\n",
                404,
            )
            return
        path = self.scope.out / name
        if not path.exists():
            self._text(f"{name} not produced yet\n", 404)
            return
        self._send(200, path.read_bytes(), content_type)

    # -- routes ---------------------------------------------------------

    def do_GET(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler API
        parsed = urlparse(self.path)
        query = parse_qs(parsed.query)
        route = parsed.path.rstrip("/") or "/"

        if not self._authorised(query):
            self._text("unauthorised: pass ?token=... or X-Hvscope-Token\n", 401)
            return

        if route == "/":
            self._html(DASHBOARD_PAGE)
        elif route == "/calibrate":
            self._html(CALIBRATION_PAGE)
        elif route == "/text":
            with self.scope.lock:
                body = self.scope.text
            self._text(body + ("\n" if body and not body.endswith("\n") else ""))
        elif route == "/log":
            count = int(query.get("n", ["120"])[0])
            self._text(self.scope.logbook.tail(count) + "\n")
        elif route == "/state.json":
            with self.scope.lock:
                self._json(self.scope.state.as_dict())
        elif route == "/frame.png":
            self._file("screen.png", "image/png")
        elif route == "/screen-color.png":
            self._file("screen-color.png", "image/png")
        elif route == "/raw.png":
            self._file("raw.png", "image/png")
        elif route == "/healthz":
            self._json({"ok": True, "frames": self.scope.state.frames})
        else:
            self._text("not found\n", 404)

    do_HEAD = do_GET

    def do_POST(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler API
        parsed = urlparse(self.path)
        query = parse_qs(parsed.query)
        route = parsed.path.rstrip("/") or "/"

        if not self._authorised(query):
            self._text("unauthorised\n", 401)
            return

        length = int(self.headers.get("Content-Length") or 0)
        raw = self.rfile.read(length) if length else b"{}"
        try:
            payload = json.loads(raw.decode("utf-8") or "{}")
        except (json.JSONDecodeError, UnicodeDecodeError) as exc:
            self._json({"error": f"bad JSON: {exc}"}, 400)
            return

        if route == "/api/calibrate":
            self._calibrate(payload)
        elif route == "/api/reset-log":
            self.scope.logbook.reset()
            self._json({"ok": True})
        else:
            self._json({"error": "not found"}, 404)

    def _calibrate(self, payload: dict) -> None:
        points = payload.get("points") or payload.get("quad")
        if not isinstance(points, list) or len(points) != 4:
            self._json({"error": "need exactly 4 points"}, 400)
            return

        raw_path = self.scope.out / "raw.png"
        if not raw_path.exists():
            self._json({"error": "no camera frame captured yet"}, 409)
            return

        try:
            from PIL import Image

            with Image.open(raw_path) as frame:
                width, height = frame.size
            if payload.get("normalised"):
                quad = [[float(x) * width, float(y) * height] for x, y in points]
            else:
                quad = [[float(x), float(y)] for x, y in points]
            ordered = geometry.order_quad(quad)
            size = payload.get("output_size") or geometry.suggest_output_size(ordered)
            # Fail here rather than in the capture loop if the quad is degenerate.
            geometry.perspective_coeffs(
                [(0.0, 0.0), (float(size[0]), 0.0), (float(size[0]), float(size[1])), (0.0, float(size[1]))],
                ordered,
            )
        except (ValueError, TypeError, OSError) as exc:
            self._json({"error": str(exc)}, 400)
            return

        self.scope.config.setdefault("calibration", {})
        self.scope.config["calibration"]["quad"] = [[round(x, 2), round(y, 2)] for x, y in ordered]
        self.scope.config["calibration"]["output_size"] = [int(size[0]), int(size[1])]
        if self.config_path:
            config_module.save(self.scope.config, self.config_path)

        self._json({
            "ok": True,
            "quad": self.scope.config["calibration"]["quad"],
            "output_size": [int(size[0]), int(size[1])],
            "saved_to": str(self.config_path) if self.config_path else None,
        })


def serve(scope, host: str, port: int, token: str = "", config_path: Path | None = None):
    """Start the API in a background thread; returns the server object."""
    handler = type("BoundHandler", (_Handler,), {
        "scope": scope,
        "token": token or "",
        "config_path": config_path,
    })
    httpd = ThreadingHTTPServer((host, int(port)), handler)
    httpd.daemon_threads = True
    thread = threading.Thread(target=httpd.serve_forever, name="hvscope-http", daemon=True)
    thread.start()
    return httpd
