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


def wav_bytes(audio: np.ndarray) -> bytes:
    import io
    import wave
    buf = io.BytesIO()
    with wave.open(buf, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((np.clip(audio, -1, 1) * 32767).astype(np.int16).tobytes())
    return buf.getvalue()


def _save_wav(audio: np.ndarray):
    """Lưu lần thu gần nhất vào bridge/last.wav để kiểm tra chất lượng micro."""
    try:
        import wave
        path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "last.wav")
        with wave.open(path, "wb") as w:
            w.setnchannels(1)
            w.setsampwidth(2)
            w.setframerate(RATE)
            w.writeframes((np.clip(audio, -1, 1) * 32767).astype(np.int16).tobytes())
    except Exception as e:
        print(f"[mic] không lưu được last.wav: {e}")


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
        self._factory = WhisperModel
        self._cfg = cfg
        self.on_cpu = False
        if cfg.get("whisper_device", "cuda") == "cpu":
            self._use_cpu()
        else:
            try:
                self.model = WhisperModel(model, device="cuda", compute_type=cfg.get("whisper_compute", "int8_float16"))
                print(f"[stt] {model} chạy trên GPU")
            except Exception as e:  # thiếu CUDA/cuDNN → chạy CPU
                print(f"[stt] Không chạy được GPU ({e}). Chuyển sang CPU, sẽ chậm hơn.")
                self._use_cpu()
        # Không đặt prompt mặc định: khi tiếng thu kém, Whisper hay chép lại đúng prompt thành "câu nói"
        self.prompt = cfg.get("whisper_prompt") or None

    def _use_cpu(self):
        name = self._cfg.get("whisper_cpu_model", "small")
        self.model = self._factory(name, device="cpu", compute_type="int8",
                                   cpu_threads=int(self._cfg.get("whisper_cpu_threads", 8)))
        self.on_cpu = True
        print(f"[stt] {name} chạy trên CPU")

    def _recognise(self, audio: np.ndarray) -> str:
        segments, _ = self.model.transcribe(audio, language="vi", beam_size=1, temperature=0.0,
                                            vad_filter=True,
                                            vad_parameters=dict(threshold=0.3, min_silence_duration_ms=400),
                                            no_speech_threshold=0.5, log_prob_threshold=-1.0,
                                            initial_prompt=self.prompt, condition_on_previous_text=False)
        # segments là generator: lỗi thư viện GPU chỉ lộ ra khi đọc kết quả.
        return " ".join(s.text.strip() for s in segments).strip()

    def transcribe(self, audio: np.ndarray) -> str:
        dur = audio.size / RATE
        peak = float(np.max(np.abs(audio))) if audio.size else 0.0
        print(f"[mic] thu {dur:.1f}s, âm lượng đỉnh {peak:.4f}")
        if audio.size < RATE * 0.4:
            print("[mic] bấm quá ngắn (dưới 0.4 giây), giữ N lâu hơn")
            return ""
        if peak < 0.02:
            print("[mic] TIẾNG QUÁ NHỎ (giọng nói bình thường phải đạt 0.1 trở lên). Sai micro hoặc micro Windows để âm lượng thấp. "
                  "Chạy list_mics.bat, đặt \"mic_device\" trong config.json, và chỉnh Input volume trong Cài đặt âm thanh của Windows")
            return ""
        _save_wav(audio)
        audio = audio / peak * 0.9
        try:
            text = self._recognise(audio)
        except (RuntimeError, OSError) as e:
            if self.on_cpu:
                raise
            # Ví dụ thiếu cublas64_12.dll: mô hình nạp được trên GPU nhưng không chạy được.
            print(f"[stt] GPU lỗi khi nhận dạng ({e}). Chuyển sang CPU; lần đầu phải tải mô hình, hãy chờ.")
            self._use_cpu()
            text = self._recognise(audio)
        if not text:
            print("[mic] nghe được tiếng nhưng không nhận ra chữ nào")
        return text
