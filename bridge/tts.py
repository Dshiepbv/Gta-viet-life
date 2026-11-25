"""Nói: Edge-TTS (miễn phí, cần mạng). Mỗi NPC có cao độ và tốc độ giọng riêng."""
import asyncio
import os
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
            pygame.mixer.music.stop()
        except pygame.error:
            pass

    def speak(self, text: str, npc: dict) -> int:
        """Tạo giọng và phát ngay. Trả về thời lượng (ms)."""
        voice = VOICE_FEMALE if npc.get("gender") == "nữ" else VOICE_MALE
        my_epoch = self.epoch
        with self.lock:
            self.n += 1
            path = os.path.join(self.dir, f"say_{self.n % 8}.mp3")
            self._halt()
            try:
                pygame.mixer.music.unload()
            except (pygame.error, AttributeError):
                pass
            ok = False
            for use_params in ((True, False) if self.use_params else (False,)):
                try:
                    kw = dict(rate=npc.get("rate", "+0%"), pitch=npc.get("pitch", "+0Hz")) if use_params else {}
                    asyncio.run(edge_tts.Communicate(text, voice, **kw).save(path))
                    ok = True
                    break
                except Exception as e:
                    print(f"[tts] lỗi ({'có' if use_params else 'không'} chỉnh giọng): {e}")
                    if use_params:
                        self.use_params = False  # lần sau bỏ qua chỉnh cao độ/tốc độ cho nhanh
            if not ok:
                raise RuntimeError("không tạo được giọng nói")
            if my_epoch != self.epoch:
                return 0  # Sếp đã bắt đầu nói lượt mới, bỏ giọng cũ
            try:
                length_ms = int(pygame.mixer.Sound(path).get_length() * 1000)
            except pygame.error:
                length_ms = 400 + 75 * len(text)
            pygame.mixer.music.load(path)
            pygame.mixer.music.set_volume(self.volume)
            pygame.mixer.music.play()
            return length_ms


if __name__ == "__main__":
    v = Voice({})
    ms = v.speak("Chào anh, hôm nay trời đẹp ghê ha.", {"gender": "nữ"})
    time.sleep(ms / 1000 + 0.5)
