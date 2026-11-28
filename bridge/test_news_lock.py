import contextlib
import io
import os
import sys
import tempfile
import threading
import unittest

# Importing the bridge opens bridge.log in its data folder and redirects the console.
_previous = os.environ.get("GVL_DATA_DIR")
os.environ["GVL_DATA_DIR"] = tempfile.mkdtemp(prefix="gvl-bridge-test-")
_console = sys.stdout, sys.stderr
from main import Bridge  # noqa: E402

sys.stdout, sys.stderr = _console
if _previous is None:
    del os.environ["GVL_DATA_DIR"]
else:
    os.environ["GVL_DATA_DIR"] = _previous


class Silent:
    def recent(self, *_):
        return []

    def show(self, *_):
        pass

    def speak(self, *_):
        pass


class Brain:
    def __init__(self, result):
        self.result = result

    def news(self, *_):
        if isinstance(self.result, Exception):
            raise self.result
        return self.result


def quiet():
    # The bridge prints Vietnamese, which a cp1252 console (CI) cannot encode.
    return contextlib.redirect_stdout(io.StringIO())


class NewsLockTests(unittest.TestCase):
    def bridge(self, result):
        bridge = Bridge.__new__(Bridge)
        bridge.busy = threading.Lock()
        bridge.life = bridge.overlay = bridge.voice = Silent()
        bridge.brain = Brain(result)
        return bridge

    def test_empty_news_releases_the_lock(self):
        bridge = self.bridge([])
        with quiet():
            bridge.news({"hour": 7})
        self.assertFalse(bridge.busy.locked())

    def test_failing_model_releases_the_lock(self):
        bridge = self.bridge(RuntimeError("offline"))
        with quiet(), self.assertRaises(RuntimeError):
            bridge.news({"hour": 7})
        self.assertFalse(bridge.busy.locked())


if __name__ == "__main__":
    unittest.main()
