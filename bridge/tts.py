"""Nói: Edge-TTS (miễn phí, cần mạng). Mỗi NPC có cao độ và tốc độ giọng riêng."""
import asyncio
import os
import re
import tempfile
import threading
import time

import edge_tts
import pygame

VOICE_MALE = "vi-VN-NamMinhNeural"
VOICE_FEMALE = "vi-VN-HoaiMyNeural"


class Voice:
    def __init__(self, cfg: dict):
        pygame.mixer.init()
        self.volume = float(cfg.get("tts_volume", 1.0))
        self.dir = tempfile.mkdtemp(prefix="gtavl_")
        self.n = 0
        self.lock = threading.Lock()
        self.use_params = True
        self.epoch = 0

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
        for use_params in ((True, False) if self.use_params else (False,)):
            try:
                kw = dict(rate=npc.get("rate", "+0%"), pitch=npc.get("pitch", "+0Hz")) if use_params else {}
                asyncio.run(edge_tts.Communicate(text, voice, **kw).save(path))
                return True
            except Exception as e:
                print(f"[tts] lỗi ({'có' if use_params else 'không'} chỉnh giọng): {e}")
                if use_params:
                    self.use_params = False
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
        chunks = self._split(text)
        self.n += 1
        base = self.n
        results = [None] * len(chunks)
        events = [threading.Event() for _ in chunks]

        def work(i, t):
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
