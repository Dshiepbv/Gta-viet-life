import json
import threading
import unittest
from http.server import ThreadingHTTPServer
from urllib.request import Request, urlopen

import dashboard


class AccessTests(unittest.TestCase):
    def setUp(self):
        self.original = dashboard.pin
        dashboard._failures.clear()

    def tearDown(self):
        dashboard.pin = self.original

    def use_pin(self, value):
        dashboard.pin = lambda: value

    def test_without_a_pin_nothing_leaves_this_computer(self):
        self.use_pin("")
        self.assertEqual(dashboard.bind_host(), "127.0.0.1")
        self.assertIsNone(dashboard.refusal("127.0.0.1", ""))
        self.assertIsNotNone(dashboard.refusal("192.168.1.20", ""))

    def test_with_a_pin_reading_and_ordering_need_it(self):
        self.use_pin("4321")
        self.assertEqual(dashboard.bind_host(), "0.0.0.0")
        self.assertIsNotNone(dashboard.refusal("192.168.1.20", ""))
        self.assertIsNotNone(dashboard.refusal("192.168.1.20", "0000"))
        self.assertIsNone(dashboard.refusal("192.168.1.20", "4321"))

    def test_guessing_is_locked_out(self):
        self.use_pin("4321")
        for guess in range(dashboard.MAX_PIN_FAILURES):
            dashboard.refusal("10.0.0.9", str(guess))
        self.assertIsNotNone(dashboard.refusal("10.0.0.9", "4321"))
        self.assertIsNone(dashboard.refusal("10.0.0.10", "4321"))

    def test_data_over_http_is_hidden_until_the_pin_is_sent(self):
        self.use_pin("4321")
        server = ThreadingHTTPServer(("127.0.0.1", 0), dashboard.H)
        threading.Thread(target=server.serve_forever, daemon=True).start()
        base = "http://127.0.0.1:%d" % server.server_address[1]
        try:
            read = lambda request: json.loads(urlopen(request, timeout=5).read())
            plain = read(base + "/data")
            self.assertEqual(set(plain), {"error", "needPin"})
            allowed = read(Request(base + "/data", data=b'{"pin":"4321"}'))
            self.assertNotEqual(allowed.get("error"), "Sai ma PIN")
        finally:
            server.shutdown()
            server.server_close()


if __name__ == "__main__":
    unittest.main()
