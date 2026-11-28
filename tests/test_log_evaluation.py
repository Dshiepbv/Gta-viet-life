"""Control cases for tools/evaluate_logs.py: every criterion must be able to pass, fail and abstain."""
import importlib.util
import io
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("evaluate_logs", ROOT / "tools" / "evaluate_logs.py")
logs = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = logs
spec.loader.exec_module(logs)

ALL = list(logs.SCRIPTS) + list(logs.SINCE)


def request(path, code):
    return '2026-11-28 19:40:00,000 INFO 127.0.0.1 "POST %s HTTP/1.1" %d -' % (path, code)


def hook(names, extra=()):
    return ["[19:38:31] [DEBUG] Loading"] + ["[19:38:34] [INFO] Started script %s." % n for n in names] + list(extra)


def event(kind, data="", time="19:40:00"):
    return "%s d4 1 %s %s" % (time, kind, data)


PATHS = ("/engine/v1/simulation/actors/observe", "/engine/v1/simulation/npcs", "/engine/v1/simulation/memory",
         "/engine/v1/world/ledger/player", "/engine/v1/world/events")
CODES = dict(zip(PATHS, (200, 201, 200, 200, 200)))


def good():
    return {
        "launcher.log": ["[x] Bộ cài 0.3.5 đầy đủ, checksum khớp.", "[x] Đã cài 8 file"],
        "ScriptHookVDotNet.log": hook(ALL),
        "engine-host.log": ["INFO Engine Host listening on 127.0.0.1:5580"] + [request(p, CODES[p]) for p in PATHS],
        "GtaVietLifeCore.log": ["19:38:35 engine host handshake thanh cong"],
        "GtaVietLifeEvents.log": [event("core.day", "day=5"), event("building.entered"), event("building.activity"),
                                  event("building.exited"), event("bank.takeover")],
        "GtaVietLifeInteriors.log": ["19:40:00 noi that 1_2_3: 3/5 diem sinh hoat"],
        "GtaVietLifeShopFloor.log": ["19:40:00 quan tai 1,2; cho don dep: 3", "19:40:05 goi mon Pho",
                                     "19:40:20 hoan tat Pho"],
        "bridge.log": ["[init] x", "[Lan] nghe 1s | Bạn: chào → chào bạn (none)"],
    }


def bad():
    files = good()
    files["launcher.log"] = ["[x] Bộ cài 0.3.5", "[x] Kiểm tra bộ cài: thiếu file", "[x] ERROR hỏng"]
    files["ScriptHookVDotNet.log"] = hook(ALL[:-1], ["[19:42:32] [ERROR] Caught unhandled exception:"])
    files["engine-host.log"] = ["INFO Engine Host listening on 127.0.0.1:5580"] + [request(p, 400) for p in PATHS]
    files["GtaVietLifeCore.log"] = ["19:38:35 engine host handshake chua thanh cong: x"]
    files["GtaVietLifeCore.deadletter.log"] = ["19:41:00 event bi loai"]
    files["GtaVietLifeEvents.log"] = [event("core.day", "day=5"), event("core.day", "day=5"),
                                      event("building.entered"), event("building.entered")]
    files["GtaVietLifeInteriors.log"] = ["19:40:00 noi that 1_2_3: 0/5 diem sinh hoat",
                                         "19:41:00 diem thu 'x' khong nap duoc"]
    files["GtaVietLifeShopFloor.log"] = ["19:40:00 quan tai 1,2; cho don dep: 0", "19:40:01 tick loi: x",
                                         "19:40:05 goi mon Pho", "19:40:30 goi mon Pho"]
    files["bridge.log"] = ["[init] x", "[http] lỗi /stop: Library cublas64_12.dll is not found"]
    return files


# An activity that never completes leaves no line at all, so absence cannot be told from "not tried".
CANNOT_FAIL = {"T16"}


class ControlTests(unittest.TestCase):
    def test_every_criterion_passes_on_a_healthy_session(self):
        results = logs.evaluate(good())
        self.assertEqual({k: v for k, v in results.items() if v[0] != logs.PASS}, {})

    def test_every_criterion_fails_on_a_broken_session(self):
        results = logs.evaluate(bad())
        wrong = {k: v for k, v in results.items() if k not in CANNOT_FAIL and v[0] != logs.FAIL}
        self.assertEqual(wrong, {})

    def test_nothing_passes_without_evidence(self):
        self.assertEqual({v[0] for v in logs.evaluate({}).values()}, {logs.UNMEASURED})

    def test_lines_from_earlier_days_are_ignored(self):
        files = good()
        files["GtaVietLifeCore.log"] = ["23:10:00 engine host handshake thanh cong",
                                        "08:00:00 cu", "19:38:36 phien nay khong bat tay"]
        files["GtaVietLifeEvents.log"] = [event("building.entered", time="21:00:00"),
                                          event("core.day", "day=1", time="19:39:00")]
        results = logs.evaluate(files)
        self.assertEqual((results["T06"][0], results["T13"][0]), (logs.UNMEASURED, logs.UNMEASURED))

    def test_old_builds_are_not_asked_for_newer_scripts(self):
        files = good()
        files["launcher.log"] = ["[x] Bộ cài 0.3.3 đầy đủ, checksum khớp."]
        files["ScriptHookVDotNet.log"] = hook(logs.SCRIPTS)
        self.assertEqual(logs.evaluate(files)["T03"][0], logs.PASS)

    def test_comparison_names_what_got_worse_and_better(self):
        text = logs.report(bad(), good())
        self.assertIn("Hỏng thêm so với bản đối chứng: T01", text)
        self.assertIn("Đã sửa được: T01", logs.report(good(), bad()))

    def test_zip_bundle_is_read_without_extracting(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "bundle.zip"
            with zipfile.ZipFile(path, "w") as bundle:
                for name, lines in good().items():
                    bundle.writestr(name, "\n".join(lines))
                bundle.writestr("../outside.txt", "ignored")
            self.assertEqual(logs.load(path).keys(), good().keys())
            self.assertEqual(list(Path(folder).iterdir()), [path])


class GameLogMarkerTests(unittest.TestCase):
    def test_scripts_write_the_lines_the_criteria_look_for(self):
        shop = (ROOT / "game" / "GtaVietLifeShopFloor.cs").read_text(encoding="utf-8-sig")
        interiors = (ROOT / "game" / "GtaVietLifeInteriors.cs").read_text(encoding="utf-8-sig")
        for marker in ('Log("quan tai ', 'cho don dep: ', 'Log("goi mon ', 'Log("hoan tat ', "khong nap duoc nhan vat"):
            self.assertIn(marker, shop)
        for marker in ('Log("noi that ', "diem sinh hoat", "khong nap duoc"):
            self.assertIn(marker, interiors)


if __name__ == "__main__":
    unittest.main()
