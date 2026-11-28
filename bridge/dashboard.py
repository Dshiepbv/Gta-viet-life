"""Dashboard kinh doanh 24/24 cho điện thoại (Android/iPhone) — chỉ ĐỌC, không điều khiển game.

Chạy trên PC (kể cả khi GTA đang tắt): python bridge\\dashboard.py
Điện thoại cùng Wi-Fi mở  http://<IP-may-tinh>:5588  (IP in ra khi chạy).
Game ghi GtaVietLifeDash.json mỗi 2 phút; khi game tắt, trang tự ước tính tiếp
theo cùng công thức bù-khi-mở-lại (40% doanh số), còn số thật chốt khi mở game.
"""
import hmac
import json
import os
import socket
import sys
import threading
import time
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

HERE = os.path.dirname(os.path.abspath(__file__))
PORT = 5588


def game_dir():
    d = os.environ.get("GTA_DIR", "")
    if not d:
        try:
            with open(os.path.join(HERE, "config.json"), encoding="utf-8") as f:
                d = json.load(f).get("game_dir", "")
        except Exception:
            pass
    return d or r"E:\GTA-Viet_Life\GTAV"


DASH = os.path.join(game_dir(), "GtaVietLifeDash.json")
CMD = os.path.join(game_dir(), "GtaVietLifeCmd.txt")
OPS = {"restock", "hire", "fire", "upgrade", "collect"}
LOOPBACK = ("127.0.0.1", "::1")
MAX_PIN_FAILURES, PIN_LOCK_SECONDS = 5, 60
_failures = {}
_failures_lock = threading.Lock()


def pin():
    p = os.environ.get("DASH_PIN", "")
    if not p:
        try:
            with open(os.path.join(HERE, "config.json"), encoding="utf-8") as f:
                p = str(json.load(f).get("dash_pin", ""))
        except Exception:
            pass
    return p


def bind_host():
    """Without a PIN the dashboard stays on this computer; the phone needs dash_pin in config.json."""
    return "0.0.0.0" if pin() else "127.0.0.1"


def refusal(client, given):
    """None when the request may read or order; otherwise the message to show."""
    expected = pin()
    if not expected:
        return None if client in LOOPBACK else "Dat dash_pin trong bridge/config.json de dung tu may khac"
    now = time.time()
    with _failures_lock:
        recent = [t for t in _failures.get(client, []) if now - t < PIN_LOCK_SECONDS]
        _failures[client] = recent
        if len(recent) >= MAX_PIN_FAILURES:
            return "Sai ma PIN qua nhieu lan, thu lai sau 1 phut"
        if hmac.compare_digest(str(given).encode("utf-8"), expected.encode("utf-8")):
            return None
        if given:
            recent.append(now)
    return "Sai ma PIN"


def cfg(k):
    try:
        with open(os.path.join(HERE, "config.json"), encoding="utf-8") as f:
            return str(json.load(f).get(k, "") or "")
    except Exception:
        return ""


def cloud_sync():
    """Day snapshot len Google Apps Script va keo lenh tu dien thoai ve GtaVietLifeCmd.txt."""
    url, key = cfg("cloud_url"), cfg("cloud_key")
    if not url or not key:
        return
    print("Dong bo dam may: BAT (moi 30 giay)")
    last = None
    while True:
        try:
            m = os.path.getmtime(DASH) if os.path.exists(DASH) else None
            if m is not None and m != last:
                with open(DASH, encoding="utf-8") as f:
                    snap = json.load(f)
                body = json.dumps({"a": "push", "key": key, "snap": snap}).encode()
                req = urllib.request.Request(url, body, {"Content-Type": "text/plain"})
                r = json.loads(urllib.request.urlopen(req, timeout=20).read().decode())
                if r.get("ok"):
                    last = m
                else:
                    print("Day len that bai:", r.get("msg"))
            r = json.loads(urllib.request.urlopen(f"{url}?a=pull&key={key}", timeout=20).read().decode())
            cmds = r.get("cmds", []) if r.get("ok") else []
            if cmds:
                with open(CMD, "a", encoding="utf-8") as f:
                    f.write("".join(c.strip() + "\n" for c in cmds))
                print(f"Nhan {len(cmds)} lenh tu dien thoai")
        except Exception as e:
            print("Dong bo dam may loi:", e)
        time.sleep(30)


def pending():
    try:
        with open(CMD, encoding="utf-8") as f:
            return [l.strip() for l in f if l.strip()]
    except Exception:
        return []


class H(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def _send(self, body, ctype, code=200):
        data = body if isinstance(body, bytes) else body.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        path = self.path.split("?")[0]
        if path in ("/", "/index.html"):
            with open(os.path.join(HERE, "dashboard.html"), "rb") as f:
                self._send(f.read(), "text/html; charset=utf-8")
        elif path == "/data":
            self._data("")
        else:
            self._send("not found", "text/plain", 404)

    def _data(self, given):
        problem = refusal(self.client_address[0], given)
        if problem:
            return self._send(json.dumps({"error": problem, "needPin": True}), "application/json; charset=utf-8")
        try:
            with open(DASH, "rb") as f:
                data = json.loads(f.read().decode("utf-8"))
            data["pending"] = [l.split("|") for l in pending()]
            data["needPin"] = bool(pin())
            self._send(json.dumps(data), "application/json; charset=utf-8")
        except Exception:
            self._send('{"error":"chua co du lieu - vao game mot lan"}', "application/json; charset=utf-8", 200)

    def do_POST(self):
        if self.path not in ("/cmd", "/data"):
            return self._send("not found", "text/plain", 404)
        try:
            n = int(self.headers.get("Content-Length", 0) or 0)
            if not 0 <= n <= 4096:
                return self._send('{"ok":false,"msg":"Yeu cau qua lon"}', "application/json", 413)
            c = json.loads(self.rfile.read(n).decode("utf-8"))
            if self.path == "/data":
                return self._data(c.get("pin", ""))
            problem = refusal(self.client_address[0], c.get("pin", ""))
            if problem:
                return self._send(json.dumps({"ok": False, "msg": problem}), "application/json", 200)
            op, idx, arg = str(c.get("op")), int(c.get("idx")), int(c.get("arg", 0))
            if op not in OPS or idx < 0 or idx > 20:
                return self._send('{"ok":false,"msg":"Lenh khong hop le"}', "application/json", 200)
            if len(pending()) >= 30:
                return self._send('{"ok":false,"msg":"Hang doi day (30 lenh)"}', "application/json", 200)
            line = f"{int(time.time()*1000)}|{op}|{idx}|{arg}\n"
            for _ in range(5):
                try:
                    with open(CMD, "a", encoding="utf-8") as f:
                        f.write(line)
                    break
                except OSError:
                    time.sleep(0.2)
            self._send('{"ok":true}', "application/json", 200)
        except Exception as e:
            self._send(json.dumps({"ok": False, "msg": str(e)}), "application/json", 200)


def lan_ip():
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("10.255.255.255", 1))
        ip = s.getsockname()[0]
        s.close()
        return ip
    except Exception:
        return "IP-may-tinh"


if __name__ == "__main__":
    print(f"Doc du lieu tu: {DASH}")
    if pin():
        print(f"Mo tren dien thoai (cung Wi-Fi o nha): http://{lan_ip()}:{PORT}")
        try:
            ts = [a[4][0] for a in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET) if a[4][0].startswith("100.")]
            for ip in ts:
                print(f"Mo tu BAT KY DAU (qua Tailscale): http://{ip}:{PORT}")
        except Exception:
            pass
    else:
        print(f"Chua dat dash_pin: chi mo duoc tren may nay (http://127.0.0.1:{PORT}).")
        print('Muon xem tren dien thoai: them "dash_pin": "<ma>" vao bridge/config.json roi chay lai.')
    threading.Thread(target=cloud_sync, daemon=True).start()
    try:
        ThreadingHTTPServer((bind_host(), PORT), H).serve_forever()
    except KeyboardInterrupt:
        sys.exit(0)
