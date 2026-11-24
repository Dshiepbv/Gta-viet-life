"""Cầu nối AI tiếng Việt cho GTA 5.

Script trong game (GtaVietLife.cs) gọi tới đây qua http://127.0.0.1:5577
  POST /start  : bắt đầu thu micro (khi bấm giữ phím nói)
  POST /stop   : dừng thu, nhận diện, hỏi AI, đọc câu trả lời. Trả về:
                 dòng 1 = hành động (none/follow/unfollow/flee/fight)
                 dòng 2 = thời lượng giọng nói (ms)
                 dòng 3 = tên NPC không dấu (hiện thông báo trong game)
  GET  /ping   : kiểm tra cầu nối đang chạy
"""
import json
import os
import threading
import time
import unicodedata
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs

HERE = os.path.dirname(os.path.abspath(__file__))


def load_config() -> dict:
    path = os.path.join(HERE, "config.json")
    if not os.path.exists(path):
        path = os.path.join(HERE, "config.example.json")
        print("[cfg] Chưa có config.json, đang dùng config.example.json")
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def ascii_name(s: str) -> str:
    s = s.replace("đ", "d").replace("Đ", "D")
    return "".join(c for c in unicodedata.normalize("NFD", s) if unicodedata.category(c) != "Mn")


class Bridge:
    def __init__(self, cfg: dict, overlay):
        from llm import Brain
        from memory import Memory
        from stt import STT, Recorder
        from tts import Voice
        self.cfg = cfg
        self.overlay = overlay
        self.recorder = Recorder(cfg.get("mic_device"))
        print("[init] Đang nạp mô hình nghe (lần đầu sẽ tải về, hơi lâu)...")
        self.stt = STT(cfg)
        self.brain = Brain(cfg)
        self.voice = Voice(cfg)
        self.memory = Memory(os.path.join(HERE, cfg.get("memory_db", "memory.db")))
        self.busy = threading.Lock()

    def start(self):
        self.voice.stop()
        self.recorder.start()
        self.overlay.show("player", "🎙 ...", 30000)

    def stop(self, ctx: dict) -> str:
        audio = self.recorder.stop()
        with self.busy:
            t0 = time.time()
            text = self.stt.transcribe(audio)
            t1 = time.time()
            if not text:
                self.overlay.show("player", "(không nghe rõ, Sếp nói lại nhé)", 2500)
                return "none\n0\n"
            self.overlay.show("player", text)
            key = ctx.get("key") or "tmp_0"
            npc = self.memory.get_or_create(key, ctx.get("gender", "male"), int(ctx.get("pedtype", "4") or 4))
            result = self.brain.chat(npc, ctx, self.memory.history(key), text)
            t2 = time.time()
            self.memory.add(key, "player", text)
            self.memory.add(key, "npc", result["reply"])
            if result["affinity_delta"]:
                self.memory.change_affinity(key, result["affinity_delta"])
            self.overlay.show("npc", f"{npc['name']}: {result['reply']}", 8000)
            try:
                ms = self.voice.speak(result["reply"], npc)
            except Exception as e:  # mất mạng → không có giọng, vẫn có phụ đề
                print(f"[tts] lỗi: {e}")
                ms = 400 + 75 * len(result["reply"])
            t3 = time.time()
            print(f"[{npc['name']}] nghe {t1 - t0:.1f}s · nghĩ {t2 - t1:.1f}s · nói {t3 - t2:.1f}s | "
                  f"Bạn: {text} → {result['reply']} ({result['action']})")
            return f"{result['action']}\n{ms}\n{ascii_name(npc['name'])}"


def make_handler(bridge: Bridge):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *args):
            pass

        def _send(self, body: str, code: int = 200):
            data = body.encode("utf-8")
            self.send_response(code)
            self.send_header("Content-Type", "text/plain; charset=utf-8")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def do_GET(self):
            self._send("ok" if self.path == "/ping" else "not found", 200 if self.path == "/ping" else 404)

        def do_POST(self):
            n = int(self.headers.get("Content-Length", 0) or 0)
            raw = self.rfile.read(n).decode("utf-8") if n else ""
            ctx = {k: v[0] for k, v in parse_qs(raw).items()}
            try:
                if self.path == "/start":
                    bridge.start()
                    self._send("ok")
                elif self.path == "/stop":
                    self._send(bridge.stop(ctx))
                else:
                    self._send("not found", 404)
            except Exception as e:
                print(f"[http] lỗi {self.path}: {e}")
                self._send(f"none\n0\n", 200)

    return Handler


def main():
    cfg = load_config()
    if cfg.get("overlay", True):
        from overlay import Overlay
        overlay = Overlay(cfg)
    else:
        from overlay import NoOverlay
        overlay = NoOverlay()
    bridge = Bridge(cfg, overlay)
    port = int(cfg.get("port", 5577))
    server = ThreadingHTTPServer(("127.0.0.1", port), make_handler(bridge))
    threading.Thread(target=server.serve_forever, daemon=True).start()
    print(f"[ok] Cầu nối đang chạy ở cổng {port}. Vào game, đến gần NPC, giữ phím N để nói.")
    overlay.show("npc", "Cầu nối AI tiếng Việt đã sẵn sàng", 4000)
    overlay.run()


if __name__ == "__main__":
    main()
