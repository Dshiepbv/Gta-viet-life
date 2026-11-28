import contextlib
import io
import sys
import types
import unittest

import numpy as np

# The tests replace the speech model and never record, so no audio device or model is needed.
sys.modules.setdefault("sounddevice", types.ModuleType("sounddevice"))
import stt  # noqa: E402


class Model:
    def __init__(self, name, device, **_):
        self.device = device

    def transcribe(self, audio, **_):
        def segments():
            if self.device == "cuda":
                raise RuntimeError("Library cublas64_12.dll is not found or cannot be loaded")
            yield types.SimpleNamespace(text=" xin chao ")
        return segments(), None


class FallbackTests(unittest.TestCase):
    def setUp(self):
        self.original = sys.modules.get("faster_whisper")
        module = types.ModuleType("faster_whisper")
        module.WhisperModel = Model
        sys.modules["faster_whisper"] = module
        self.save = stt._save_wav
        stt._save_wav = lambda audio: None

    def tearDown(self):
        stt._save_wav = self.save
        if self.original is None:
            del sys.modules["faster_whisper"]
        else:
            sys.modules["faster_whisper"] = self.original

    def test_gpu_failing_during_recognition_falls_back_to_the_cpu(self):
        # The bridge prints Vietnamese, which a cp1252 console (CI) cannot encode.
        with contextlib.redirect_stdout(io.StringIO()):
            engine = stt.STT({})
            self.assertFalse(engine.on_cpu)
            audio = np.full(16000, 0.3, dtype=np.float32)
            self.assertEqual(engine.transcribe(audio), "xin chao")
            self.assertTrue(engine.on_cpu)
            self.assertEqual(engine.transcribe(audio), "xin chao")


if __name__ == "__main__":
    unittest.main()
