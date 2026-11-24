"""Nghe: thu micro và chuyển giọng nói tiếng Việt thành chữ (faster-whisper)."""
import glob
import os
import site
import sys
import threading

import numpy as np
import sounddevice as sd

RATE = 16000


def _add_cuda_dlls():
    """Trên Windows, cho Python tìm thấy cuBLAS/cuDNN cài bằng pip (nvidia-*-cu12)."""
    if sys.platform != "win32":
        return
    for base in site.getsitepackages() + [site.getusersitepackages()]:
        for d in glob.glob(os.path.join(base, "nvidia", "*", "bin")):
            try:
                os.add_dll_directory(d)
                os.environ["PATH"] = d + os.pathsep + os.environ.get("PATH", "")
            except OSError:
                pass


class Recorder:
    def __init__(self, device=None):
        self.device = device
        self.frames = []
        self.stream = None
        self.lock = threading.Lock()

    def _cb(self, indata, frames, t, status):
        self.frames.append(indata.copy())

    def start(self):
        with self.lock:
            if self.stream is not None:
                return
            self.frames = []
            self.stream = sd.InputStream(samplerate=RATE, channels=1, dtype="float32",
                                         device=self.device, callback=self._cb)
            self.stream.start()

    def stop(self) -> np.ndarray:
        with self.lock:
            if self.stream is None:
                return np.zeros(0, dtype=np.float32)
            self.stream.stop()
            self.stream.close()
            self.stream = None
            if not self.frames:
                return np.zeros(0, dtype=np.float32)
            return np.concatenate(self.frames).flatten()


class STT:
    def __init__(self, cfg: dict):
        _add_cuda_dlls()
        from faster_whisper import WhisperModel
        model = cfg.get("whisper_model", "large-v3-turbo")
        try:
            self.model = WhisperModel(model, device="cuda", compute_type=cfg.get("whisper_compute", "int8_float16"))
            print(f"[stt] {model} chạy trên GPU")
        except Exception as e:  # thiếu CUDA/cuDNN → chạy CPU
            print(f"[stt] Không chạy được GPU ({e}). Chuyển sang CPU, sẽ chậm hơn.")
            self.model = WhisperModel(cfg.get("whisper_cpu_model", "small"), device="cpu", compute_type="int8")
        self.prompt = cfg.get("whisper_prompt", "Xin chào anh, cho em hỏi đường với. Dạ vâng, cảm ơn chị.")

    def transcribe(self, audio: np.ndarray) -> str:
        if audio.size < RATE * 0.4:  # bấm quá nhanh, dưới 0.4 giây
            return ""
        peak = float(np.max(np.abs(audio)))
        if peak < 0.01:  # im lặng
            return ""
        audio = audio / peak * 0.9
        segments, _ = self.model.transcribe(audio, language="vi", beam_size=1, vad_filter=True,
                                            initial_prompt=self.prompt, condition_on_previous_text=False)
        return " ".join(s.text.strip() for s in segments).strip()
