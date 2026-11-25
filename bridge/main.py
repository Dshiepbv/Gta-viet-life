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


class _Tee:
    """Vừa in ra cửa sổ, vừa ghi vào bridge/bridge.log để dễ kiểm tra lỗi."""

    def __init__(self, stream, path):
        self.stream, self.f = stream, open(path, "a", encoding="utf-8", buffering=1)

    def write(self, data):
        try:
            self.stream.write(data)
        except Exception:
            pass
        self.f.write(data)

    def flush(self):
        try:
            self.stream.flush()
        except Exception:
            pass
        self.f.flush()


import sys
sys.stdout = _Tee(sys.stdout, os.path.join(HERE, "bridge.log"))
sys.stderr = sys.stdout


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
        from stt import Recorder
        from tts import Voice
        self.cfg = cfg
        self.overlay = overlay
        self.recorder = Recorder(cfg.get("mic_device"))
        self.stt = None
        self.stt_mode = cfg.get("stt_mode", "gemini") if (cfg.get("gemini_api_key") or cfg.get("groq_api_key")) else "local"
        if self.stt_mode == "groq" and not cfg.get("groq_api_key"):
            self.stt_mode = "gemini" if cfg.get("gemini_api_key") else "local"
        if self.stt_mode == "local":
            self._get_stt()
        else:
            print("[init] Nghe bằng Gemini (không cần nạp Whisper). Whisper chỉ nạp khi Gemini lỗi.")
        self.brain = Brain(cfg)
        threading.Thread(target=self.brain.warmup_ollama, daemon=True).start()
        self.voice = Voice(cfg)
        self.memory = Memory(os.path.join(HERE, cfg.get("memory_db", "memory.db")))
        from life import Life
        self.life = Life()
        self.busy = threading.Lock()

    def _get_stt(self):
        if self.stt is None:
            from stt import STT
            print("[init] Đang nạp mô hình nghe (lần đầu sẽ tải về, hơi lâu)...")
            self.stt = STT(self.cfg)
        return self.stt

    banter_epoch = 0

    def banter(self, ctx: dict) -> str:
        """Hai NPC gan nhau noi chuyen voi nhau. Tra ve: so cau, roi moi dong 'A|ms' hoac 'B|ms'."""
        if not self.busy.acquire(blocking=False):
            return "0"
        try:
            a = self.memory.get_or_create(ctx.get("keyA", "tmp_a"), ctx.get("genderA", "male"), int(ctx.get("pedtypeA", "4") or 4))
            b = self.memory.get_or_create(ctx.get("keyB", "tmp_b"), ctx.get("genderB", "male"), int(ctx.get("pedtypeB", "4") or 4))
            if a["name"] == b["name"] and a["key"] != b["key"]:
                b = dict(b, name=b["name"] + " (bạn)")
            try:
                ctx["extra"] = self.life.context(a)
            except Exception:
                ctx["extra"] = ""
            lines = self.brain.banter(a, b, ctx)
        finally:
            self.busy.release()
        if not lines:
            return "0"
        plan = [(who, text, tone, 700 + 85 * len(text)) for who, text, tone in lines]
        self.banter_epoch += 1
        my = self.banter_epoch

        def run():
            for who, text, tone, ms in plan:
                if my != self.banter_epoch:
                    return
                n = a if who == "A" else b
                self.overlay.show("npc", f"{n['name']}: {text}", ms + 1500)
                try:
                    self.voice.speak(text, dict(n, _tone=tone))
                except Exception as e:
                    print(f"[banter] tts lỗi: {e}")
                time.sleep(ms / 1000.0 + 0.5)

        threading.Thread(target=run, daemon=True).start()
        print("[banter] " + " / ".join(f"{(a if w == 'A' else b)['name']}: {t}" for w, t, _, _ in plan))
        return str(len(plan)) + "\n" + "\n".join(f"{w}|{ms}" for w, _, _, ms in plan)

    def start(self):
        self.banter_epoch += 1   # Sep bat dau noi thi dung cuoc tro chuyen cua NPC
        self.voice.stop()
        self.recorder.start()
        self.overlay.show("player", "🎙 ...", 30000)

    def stop(self, ctx: dict) -> str:
        audio = self.recorder.stop()
        with self.busy:
            from stt import wav_bytes
            t0 = time.time()
            dur = audio.size / 16000
            peak = float(abs(audio).max()) if audio.size else 0.0
            print(f"[mic] thu {dur:.1f}s, âm lượng đỉnh {peak:.4f}")
            if dur < 0.4 or peak < 0.02:
                print("[mic] quá ngắn hoặc quá nhỏ, bỏ qua")
                self.overlay.show("player", "(không nghe rõ, Sếp nói lại nhé)", 2500)
                return "none\n0\n"
            key = ctx.get("key") or "tmp_0"
            npc = self.memory.get_or_create(key, ctx.get("gender", "male"), int(ctx.get("pedtype", "4") or 4),
                                            ctx.get("name", ""))
            history = self.memory.history(key)
            self.life.update_state({"hour": ctx.get("hour", "")})
            ctx["extra"] = self.life.context(npc, ctx.get("res_activity", ""))
            result, text, by_audio = None, "", False
            if self.stt_mode == "groq":
                try:
                    text = self.brain.transcribe_groq(wav_bytes(audio))
                    if not text:
                        print("[mic] Groq không nghe ra lời nói")
                        self.overlay.show("player", "(không nghe rõ, Sếp nói lại nhé)", 2500)
                        return "none\n0\n"
                    result = self.brain.chat(npc, ctx, history, text)
                except Exception as e:
                    print(f"[stt] Groq lỗi, chuyển sang Gemini: {e}")
                    result = None
            if self.stt_mode in ("gemini", "groq") and result is None and self.cfg.get("gemini_api_key"):
                try:
                    result = self.brain.chat_audio(npc, ctx, history, wav_bytes(audio))
                    text = result.get("heard", "")
                    by_audio = True
                    if not text:
                        print("[mic] Gemini không nghe ra lời nói")
                        self.overlay.show("player", "(không nghe rõ, Sếp nói lại nhé)", 2500)
                        return "none\n0\n"
                except Exception as e:
                    print(f"[stt] Gemini nghe lỗi, chuyển sang Whisper: {e}")
                    result = None
            t1 = time.time()
            if result is None:
                text = self._get_stt().transcribe(audio)
                t1 = time.time()
                if not text:
                    self.overlay.show("player", "(không nghe rõ, Sếp nói lại nhé)", 2500)
                    return "none\n0\n"
                self.overlay.show("player", text)
                result = self.brain.chat(npc, ctx, history, text)
            else:
                self.overlay.show("player", text)
            t2 = time.time()
            self.memory.add(key, "player", text)
            self.memory.add(key, "npc", result["reply"])
            if result["affinity_delta"]:
                self.memory.change_affinity(key, result["affinity_delta"])
            self.overlay.show("npc", f"{npc['name']}: {result['reply']}", 8000)
            ms = 400 + 75 * len(result["reply"])  # ước lượng thời lượng để NPC mấp máy miệng

            def speak():
                try:
                    self.voice.speak(result["reply"], dict(npc, _tone=result.get("tone", "")))
                except Exception as e:  # mất mạng → không có giọng, vẫn có phụ đề
                    print(f"[tts] lỗi: {e}")
                print(f"[{npc['name']}] giọng sẵn sàng sau {time.time() - t2:.1f}s")

            threading.Thread(target=speak, daemon=True).start()
            t3 = time.time()
            timing = (f"Gemini nghe+nghĩ {t2 - t0:.1f}s" if by_audio
                      else f"nghe {t1 - t0:.1f}s · nghĩ {t2 - t1:.1f}s")
            print(f"[{npc['name']}] {timing} | "
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
                if self.path == "/state":
                    bridge.life.update_state(ctx)
                    self._send("ok")
                elif self.path == "/event":
                    bridge.life.add_rumor(ctx.get("text", ""))
                    self._send("ok")
                elif self.path == "/start":
                    bridge.start()
                    self._send("ok")
                elif self.path == "/stop":
                    self._send(bridge.stop(ctx))
                elif self.path == "/banter":
                    self._send(bridge.banter(ctx))
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
