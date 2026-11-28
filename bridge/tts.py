"""Nói: Edge-TTS (miễn phí, cần mạng). Mỗi NPC có cao độ và tốc độ giọng riêng."""
import asyncio
import base64
import json
import os
import re
import tempfile
import threading
import time
import urllib.error
import urllib.request
import wave

import edge_tts
import pygame

VOICE_MALE = "vi-VN-NamMinhNeural"
VOICE_FEMALE = "vi-VN-HoaiMyNeural"

GEM_FEMALE = ["Kore", "Aoede", "Leda", "Zephyr", "Autonoe", "Callirrhoe", "Despina", "Erinome",
              "Laomedeia", "Achernar", "Sulafat", "Vindemiatrix"]
GEM_MALE = ["Puck", "Charon", "Fenrir", "Orus", "Enceladus", "Iapetus", "Umbriel", "Algieba",
            "Algenib", "Rasalgethi", "Alnilam", "Schedar", "Gacrux", "Achird", "Sadachbia", "Zubenelgenubi"]
GEM_OLD_MALE = ["Charon", "Gacrux", "Rasalgethi", "Umbriel", "Algenib", "Schedar"]
GEM_OLD_FEMALE = ["Vindemiatrix", "Sulafat", "Achernar", "Despina"]
TONE_TEXT = {"vui": "vui vẻ, tươi tắn", "cau": "cáu kỉnh, gắt gỏng", "so": "hoảng sợ, run rẩy, nói nhanh",
             "buon": "buồn bã, chậm rãi", "thi_tham": "thì thầm, nhỏ giọng", "hao_hung": "hào hứng, sôi nổi",
             "binh_thuong": "tự nhiên, đời thường"}
GEM_TTS_MODELS = ["gemini-2.5-flash-preview-tts", "gemini-2.5-pro-preview-tts"]

# tone -> (đổi tốc độ %, đổi cao độ Hz)
TONE_ADJ = {"vui": (8, 6), "cau": (10, -4), "so": (14, 12), "buon": (-12, -8),
            "thi_tham": (-8, -10), "hao_hung": (14, 8)}


def _shift(val: str, d: int, unit: str) -> str:
    try:
        n = int(val.replace(unit, "").replace("+", ""))
    except ValueError:
        n = 0
    n = max(-40, min(40, n + d))
    return f"{n:+d}{unit}"


class Voice:
    def __init__(self, cfg: dict):
        try:
            pygame.mixer.init()
        except pygame.error as e:
            # Không có thiết bị âm thanh: cầu nối vẫn chạy, chỉ im lặng cho tới khi mở được loa.
            print(f"[tts] chưa mở được thiết bị âm thanh: {e}")
        self.volume = float(cfg.get("tts_volume", 1.0))
        self.dir = tempfile.mkdtemp(prefix="gtavl_")
        self.n = 0
        self.lock = threading.Lock()
        self.use_params = True
        self.epoch = 0
        self.edge_fails = 0
        self.edge_skip_until = 0.0
        self.cfg = cfg
        self.gem_key = cfg.get("gemini_api_key", "")
        self.gem_on = bool(cfg.get("gemini_tts", True)) and bool(self.gem_key)
        self.gem_models = [cfg["gemini_tts_model"]] if cfg.get("gemini_tts_model") else list(GEM_TTS_MODELS)
        self.gem_skip_until = 0.0

    def gem_usable(self) -> bool:
        return self.gem_on and time.time() >= self.gem_skip_until

    def _gem_voice(self, npc: dict) -> str:
        from llm import profile_for
        h, _, (age, _w), _c = profile_for(npc)
        female = npc.get("gender") == "nữ"
        old = "lớn tuổi" in age or "trung niên" in age
        pool = (GEM_OLD_FEMALE if female else GEM_OLD_MALE) if old and (h >> 12) % 3 else (GEM_FEMALE if female else GEM_MALE)
        return pool[(h >> 16) % len(pool)]

    def _gemini_tts(self, text: str, npc: dict, path: str) -> bool:
        from llm import profile_for
        h, (region, _w), (age, _aw), _c = profile_for(npc)
        tone = TONE_TEXT.get(npc.get("_tone", ""), TONE_TEXT["binh_thuong"])
        prompt = (f"Đọc câu sau bằng tiếng Việt, giọng {region}, giọng của người {age}, {tone}, "
                  f"nói như người thật ngoài đời, không đọc phần chỉ dẫn này: {text}")
        body = {"contents": [{"parts": [{"text": prompt}]}],
                "generationConfig": {"responseModalities": ["AUDIO"],
                                     "speechConfig": {"voiceConfig": {"prebuiltVoiceConfig": {"voiceName": self._gem_voice(npc)}}}}}
        last = None
        for m in self.gem_models:
            url = f"https://generativelanguage.googleapis.com/v1beta/models/{m}:generateContent?key={self.gem_key}"
            req = urllib.request.Request(url, data=json.dumps(body).encode("utf-8"),
                                         headers={"Content-Type": "application/json"}, method="POST")
            try:
                with urllib.request.urlopen(req, timeout=12) as r:
                    data = json.loads(r.read().decode("utf-8"))
                part = data["candidates"][0]["content"]["parts"][0]["inlineData"]
                pcm = base64.b64decode(part["data"])
                with wave.open(path, "wb") as w:
                    w.setnchannels(1); w.setsampwidth(2); w.setframerate(24000); w.writeframes(pcm)
                return True
            except urllib.error.HTTPError as e:
                last = e
                if e.code == 429:
                    self.gem_skip_until = time.time() + 300
                    print("[tts] Gemini TTS hết lượt, 5 phút tới dùng giọng Edge")
                    return False
            except Exception as e:
                last = e
        print(f"[tts] Gemini TTS lỗi: {last}")
        self.gem_skip_until = time.time() + 60
        return False

    def stop(self):
        self.epoch += 1  # huỷ giọng đang tạo dở của lượt trước
        self._halt()

    def _halt(self):
        try:
            pygame.mixer.stop()
            pygame.mixer.music.stop()
        except pygame.error:
            pass

    def _make(self, text: str, voice: str, npc: dict, path: str) -> bool:
        skip_edge = time.time() < self.edge_skip_until  # Edge đang lỗi liên tục: dùng thẳng giọng Google cho nhanh
        for use_params in (() if skip_edge else ((True, False) if self.use_params else (False,))):
            try:
                kw = {}
                if use_params:
                    r, p = npc.get("rate", "+0%"), npc.get("pitch", "+0Hz")
                    dr, dp = TONE_ADJ.get(npc.get("_tone", ""), (0, 0))
                    kw = dict(rate=_shift(r, dr, "%"), pitch=_shift(p, dp, "Hz"))
                asyncio.run(edge_tts.Communicate(text, voice, **kw).save(path))
                self.edge_fails = 0
                return True
            except Exception as e:
                print(f"[tts] lỗi ({'có' if use_params else 'không'} chỉnh giọng): {e}")
                if use_params:
                    self.use_params = False
        if not skip_edge:
            self.edge_fails += 1
            if self.edge_fails >= 2:
                self.edge_skip_until = time.time() + 180
                print("[tts] Edge lỗi liên tục, 3 phút tới dùng giọng Google")
        try:  # dự phòng: giọng Google
            from gtts import gTTS
            gTTS(text, lang="vi").save(path)
            print("[tts] dùng giọng dự phòng Google")
            return True
        except Exception as e:
            print(f"[tts] giọng dự phòng cũng lỗi: {e}")
            return False

    @staticmethod
    def _split(text: str):
        parts = [p.strip() for p in re.split(r"(?<=[.!?…,;])\s+", text) if p.strip()]
        out = []
        for p in parts:  # gộp mảnh quá ngắn để khỏi vấp
            if out and len(out[-1]) < 14:
                out[-1] += " " + p
            else:
                out.append(p)
        return out or [text]

    def speak(self, text: str, npc: dict) -> int:
        """Tạo giọng theo từng câu (song song) và phát câu đầu ngay khi xong."""
        voice = VOICE_FEMALE if npc.get("gender") == "nữ" else VOICE_MALE
        self.epoch += 1
        my_epoch = self.epoch
        self._halt()
        use_gem = self.gem_usable()
        chunks = [text] if use_gem else self._split(text)   # Gemini đọc cả câu một lần cho liền giọng
        self.n += 1
        base = self.n
        results = [None] * len(chunks)
        events = [threading.Event() for _ in chunks]

        def work(i, t):
            if use_gem:
                wp = os.path.join(self.dir, f"say_{base}_{i}.wav")
                if self._gemini_tts(t, npc, wp):
                    results[i] = wp
                    events[i].set()
                    return
            path = os.path.join(self.dir, f"say_{base}_{i}.mp3")
            results[i] = path if self._make(t, voice, npc, path) else None
            events[i].set()

        for i, t in enumerate(chunks):
            threading.Thread(target=work, args=(i, t), daemon=True).start()

        def player():
            for i in range(len(chunks)):
                events[i].wait()
                if my_epoch != self.epoch:
                    return
                if not results[i]:
                    continue
                try:
                    if not pygame.mixer.get_init():
                        pygame.mixer.init()
                    snd = pygame.mixer.Sound(results[i])
                    snd.set_volume(self.volume)
                    ch = snd.play()
                    while ch is not None and ch.get_busy():
                        if my_epoch != self.epoch:
                            ch.stop()
                            return
                        time.sleep(0.03)
                except pygame.error as e:
                    print(f"[tts] không phát được: {e}")

        threading.Thread(target=player, daemon=True).start()
        return 400 + 75 * len(text)


if __name__ == "__main__":
    v = Voice({})
    ms = v.speak("Chào anh, hôm nay trời đẹp ghê ha. Anh cần tìm đường nào không?", {"gender": "nữ"})
    time.sleep(ms / 1000 + 3)
