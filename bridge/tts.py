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

    def stop(self):
        try:
            pygame.mixer.music.stop()
        except pygame.error:
            pass

    def speak(self, text: str, npc: dict) -> int:
        """Tạo giọng và phát ngay. Trả về thời lượng (ms)."""
        voice = VOICE_FEMALE if npc.get("gender") == "nữ" else VOICE_MALE
        with self.lock:
            self.n += 1
            path = os.path.join(self.dir, f"say_{self.n % 8}.mp3")
            self.stop()
            try:
                pygame.mixer.music.unload()
            except (pygame.error, AttributeError):
                pass
            comm = edge_tts.Communicate(text, voice, rate=npc.get("rate", "+0%"), pitch=npc.get("pitch", "+0Hz"))
            asyncio.run(comm.save(path))
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
