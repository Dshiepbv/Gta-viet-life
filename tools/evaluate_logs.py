"""Chấm gói log chẩn đoán theo tiêu chí, có đối chứng.

Mỗi tiêu chí chỉ được ĐẠT khi có bằng chứng dương trong log của đúng phiên chơi. Không có bằng chứng
thì là CHƯA ĐO, không bao giờ được coi là đạt. tests/test_log_evaluation.py buộc mỗi tiêu chí phải
ĐẠT trên log mẫu đúng, KHÔNG ĐẠT trên log mẫu sai và CHƯA ĐO trên gói rỗng.

    python tools/evaluate_logs.py <goi.zip hoặc thư mục> [--baseline <gói cũ>] [--output bao-cao.md]
"""
from __future__ import annotations

import argparse
import re
import sys
import zipfile
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Dict, List, Optional, Tuple

PASS, FAIL, UNMEASURED = "ĐẠT", "KHÔNG ĐẠT", "CHƯA ĐO"
Verdict = Tuple[str, str]
MAX_FILE_BYTES = 20_000_000
SCRIPTS = ("GtaVietLife", "GtaVietLifeCore", "GtaVietLifeDoiSong", "GtaVietLifeEconomy",
           "GtaVietLifeInteriors", "GtaVietLifePersonal", "GtaVietLifeWorld")
SINCE = {"GtaVietLifeShopFloor": (0, 3, 5)}
CLOCK = re.compile(r"^\[?(\d\d):(\d\d):(\d\d)")


def load(source: Path) -> Dict[str, List[str]]:
    """Read log files only, in memory; nothing from the bundle is extracted or executed."""
    files: Dict[str, List[str]] = {}
    if source.is_dir():
        entries = ((p.name, p.read_bytes()) for p in source.glob("*.log") if p.stat().st_size <= MAX_FILE_BYTES)
        for name, data in entries:
            files[name] = data.decode("utf-8", "replace").splitlines()
        return files
    with zipfile.ZipFile(source) as bundle:
        for info in bundle.infolist():
            name = Path(info.filename).name
            if name.endswith(".log") and info.file_size <= MAX_FILE_BYTES:
                files[name] = bundle.read(info).decode("utf-8", "replace").splitlines()
    return files


def _seconds(line: str) -> Optional[int]:
    match = CLOCK.match(line)
    if not match:
        return None
    hour, minute, second = (int(part) for part in match.groups())
    return hour * 3600 + minute * 60 + second


class Session:
    """The newest game session: module logs keep lines of earlier days with a clock time only."""

    def __init__(self, files: Dict[str, List[str]]):
        self.files = files
        hook = files.get("ScriptHookVDotNet.log", [])
        times = [t for t in map(_seconds, hook) if t is not None]
        self.start = times[0] if times else None

    def lines(self, name: str) -> List[str]:
        lines = self.files.get(name, [])
        if self.start is None:
            return []
        kept: List[str] = []
        newest = 24 * 3600
        for line in reversed(lines):
            time = _seconds(line)
            if time is not None:
                # Walking backwards the clock must not rise; a rise or a time before the start is an older day.
                if time > newest or time < self.start - 120:
                    break
                newest = time
            kept.append(line)
        return kept[::-1]

    def after_last(self, name: str, marker: str) -> List[str]:
        lines = self.files.get(name, [])
        for index in range(len(lines) - 1, -1, -1):
            if marker in lines[index]:
                return lines[index:]
        return lines

    def events(self, kind: str) -> List[str]:
        pattern = re.compile(r"^\d\d:\d\d:\d\d d-?\d+ \d+ %s( |$)" % re.escape(kind))
        return [line for line in self.lines("GtaVietLifeEvents.log") if pattern.match(line)]

    def requests(self, path: str) -> List[int]:
        pattern = re.compile(r'"(?:GET|POST) %s HTTP/1\.\d" (\d{3})' % re.escape(path))
        return [int(m.group(1)) for line in self.after_last("engine-host.log", "listening on")
                for m in [pattern.search(line)] if m]

    def version(self) -> Optional[Tuple[int, ...]]:
        for line in reversed(self.files.get("launcher.log", [])):
            match = re.search(r"Bộ cài (\d+)\.(\d+)\.(\d+)", line)
            if match:
                return tuple(int(part) for part in match.groups())
        return None


def grep(lines: List[str], pattern: str) -> List[str]:
    expression = re.compile(pattern)
    return [line for line in lines if expression.search(line)]


def decide(good: List, bad: List, missing: str, good_text: str, bad_text: str) -> Verdict:
    if bad:
        return FAIL, "%s: %d. Ví dụ: %s" % (bad_text, len(bad), str(bad[0])[:160])
    if good:
        return PASS, "%s: %d" % (good_text, len(good))
    return UNMEASURED, missing


def scripts_loaded(s: Session) -> Verdict:
    hook = s.files.get("ScriptHookVDotNet.log", [])
    if not hook:
        return UNMEASURED, "Không có ScriptHookVDotNet.log"
    version = s.version() or (0, 0, 0)
    expected = list(SCRIPTS) + [name for name, since in SINCE.items() if version >= since]
    started = {m.group(1) for line in hook for m in [re.search(r"Started script (\w+)\.", line)] if m}
    missing = [name for name in expected if name not in started]
    if missing:
        return FAIL, "Không nạp được: " + ", ".join(missing)
    return PASS, "Nạp đủ %d script" % len(expected)


def scripts_alive(s: Session) -> Verdict:
    hook = s.files.get("ScriptHookVDotNet.log", [])
    return decide(grep(hook, r"Started script"), grep(hook, r"unhandled exception|thrown while executing"),
                  "Không có script nào khởi động", "Script chạy, không có ngoại lệ", "Ngoại lệ làm sập script")


def module_errors(s: Session) -> Verdict:
    lines = [line for name in s.files if name.startswith("GtaVietLife") and "Events" not in name
             for line in s.lines(name)]
    return decide(lines, grep(lines, r"tick loi|viec cho loi|save loi|load loi|khong doc duoc|ve chu loi"),
                  "Không có dòng log module nào trong phiên", "Dòng log module không lỗi", "Dòng báo lỗi")


def package_intact(s: Session) -> Verdict:
    checks = grep(s.files.get("launcher.log", []), r"checksum khớp|Kiểm tra bộ cài:")
    if not checks:
        return UNMEASURED, "Launcher chưa ghi kết quả kiểm bộ cài"
    return (PASS, checks[-1][-120:]) if "checksum khớp" in checks[-1] else (FAIL, checks[-1][-160:])


def modules_installed(s: Session) -> Verdict:
    results = grep(s.files.get("launcher.log", []), r"Đã cài \d+ file|Module đã là bản mới nhất|\] ERROR ")
    if not results:
        return UNMEASURED, "Launcher chưa cài module lần nào"
    return (FAIL, results[-1][-160:]) if " ERROR " in results[-1] else (PASS, results[-1].split("] ", 1)[-1][:120])


def handshake(s: Session) -> Verdict:
    core = s.lines("GtaVietLifeCore.log")
    done = grep(core, r"handshake thanh cong")
    failed = [] if done else grep(core, r"handshake chua thanh cong")
    return decide(done, failed, "Core chưa thử bắt tay", "Bắt tay thành công", "Bắt tay thất bại")


def requests_accepted(s: Session) -> Verdict:
    codes = [(int(m.group(1)), line) for line in s.after_last("engine-host.log", "listening on")
             for m in [re.search(r'HTTP/1\.\d" (\d{3})', line)] if m]
    return decide([c for c, _ in codes if c < 300], [line for c, line in codes if c >= 300],
                  "Engine chưa nhận yêu cầu nào", "Yêu cầu trả 2xx", "Yêu cầu bị từ chối")


def player_synced(s: Session) -> Verdict:
    codes = s.requests("/engine/v1/simulation/actors/observe")
    return decide([c for c in codes if c == 200], [c for c in codes if c != 200],
                  "Chưa có quan sát người chơi", "Quan sát người chơi được nhận", "Quan sát bị từ chối")


def residents_registered(s: Session) -> Verdict:
    codes = s.requests("/engine/v1/simulation/npcs")
    return decide([c for c in codes if c == 201], [c for c in codes if c != 201],
                  "Chưa đặt cư dân nào (menu J)", "Cư dân đăng ký vào Engine", "Cư dân bị từ chối")


def days_advance(s: Session) -> Verdict:
    days = [m.group(1) for line in s.events("core.day") for m in [re.search(r"day=(\d+)", line)] if m]
    repeated = sorted({d for d in days if days.count(d) > 1})
    return decide(days, repeated, "Phiên chưa qua nửa đêm trong game", "Số lần sang ngày", "Ngày bị đếm lặp")


def dialogue_works(s: Session) -> Verdict:
    lines = s.after_last("bridge.log", "[init]") if "bridge.log" in s.files else []
    lines = s.files.get("bridge.log", []) if not grep(lines, r"Bạn:|lỗi /stop") else lines
    answered = grep(lines, r"Bạn: .+→")
    broken = grep(lines, r"lỗi /stop")
    if answered and (not broken or lines.index(answered[-1]) > lines.index(broken[-1])):
        broken = []
    return decide(answered, broken, "Chưa có lượt nói nào hoàn tất", "Lượt thoại có chữ và câu trả lời",
                  "Lượt thoại lỗi")


def dialogue_remembered(s: Session) -> Verdict:
    codes = s.requests("/engine/v1/simulation/memory")
    return decide([c for c in codes if c == 200], [c for c in codes if c != 200],
                  "Chưa nói chuyện với cư dân đã đăng ký", "Ký ức hội thoại được lưu", "Ký ức bị từ chối")


def interiors_enter(s: Session) -> Verdict:
    return decide(s.events("building.entered"), grep(s.lines("GtaVietLifeInteriors.log"), r"khong nap duoc"),
                  "Chưa vào nhà nào", "Lượt vào nhà", "Nội thất hoặc điểm thử không nạp được")


def interiors_exit(s: Session) -> Verdict:
    entered, exited = len(s.events("building.entered")), len(s.events("building.exited"))
    if not entered:
        return UNMEASURED, "Chưa vào nhà nào"
    if exited < entered - 1:   # the player may still be inside when the log ends
        return FAIL, "Vào %d lần nhưng chỉ ra %d lần" % (entered, exited)
    return PASS, "Vào %d, ra %d" % (entered, exited)


def interiors_anchors(s: Session) -> Verdict:
    rows = [(line, int(m.group(1))) for line in s.lines("GtaVietLifeInteriors.log")
            for m in [re.search(r"noi that \S+: (\d+)/\d+ diem", line)] if m]
    return decide([line for line, n in rows if n > 0], [line for line, n in rows if n == 0],
                  "Chưa vào nhà nào", "Nhà có chỗ tương tác", "Nhà không có chỗ tương tác nào")


def interiors_activity(s: Session) -> Verdict:
    return decide(s.events("building.activity"), [], "Chưa hoàn tất tương tác đồ vật nào",
                  "Tương tác hoàn tất", "")


def shop_found(s: Session) -> Verdict:
    lines = s.lines("GtaVietLifeShopFloor.log")
    return decide(grep(lines, r"quan tai "), grep(lines, r"tick loi|phim loi"),
                  "Chưa vào cửa hàng có quầy", "Nhận ra quầy", "Lỗi tại quầy")


def shop_staff(s: Session) -> Verdict:
    lines = s.lines("GtaVietLifeShopFloor.log")
    return decide(grep(lines, r"cho don dep: [1-9]"),
                  grep(lines, r"cho don dep: 0|khong nap duoc nhan vat"),
                  "Chưa vào cửa hàng có quầy", "Có chỗ cho người dọn", "Thiếu nhân viên")


def shop_order(s: Session) -> Verdict:
    lines = s.lines("GtaVietLifeShopFloor.log")
    bought, done = grep(lines, r" goi mon "), grep(lines, r" hoan tat ")
    if not bought:
        return UNMEASURED, "Chưa gọi món nào"
    if len(done) < len(bought) - 1:
        return FAIL, "Gọi %d món, chỉ hoàn tất %d" % (len(bought), len(done))
    return (PASS, "Hoàn tất %d/%d món" % (len(done), len(bought))) if done else (UNMEASURED, "Món đang dùng dở")


def bank_owned(s: Session) -> Verdict:
    takeover = s.events("bank.takeover")
    rejected = [c for c in s.requests("/engine/v1/world/ledger/player") if c != 200]
    return decide(takeover, rejected, "Không có lần nhận quyền trong phiên (có thể đã nhận từ phiên trước)",
                  "Sổ cái nhận quyền ngân hàng", "Đọc sổ cái thất bại")


def events_delivered(s: Session) -> Verdict:
    sent = [c for c in s.requests("/engine/v1/world/events") if c == 200]
    dead = s.lines("GtaVietLifeCore.deadletter.log")
    return decide(sent, dead, "Chưa gửi event nào", "Lô event được nhận, không có event bị loại",
                  "Event bị loại")


@dataclass(frozen=True)
class Criterion:
    code: str
    module: str
    expectation: str
    check: Callable[[Session], Verdict]


CRITERIA = (
    Criterion("T01", "M6", "Bộ cài đủ file và đúng checksum", package_intact),
    Criterion("T02", "M6", "Launcher cài module vào game", modules_installed),
    Criterion("T03", "M1", "Mọi script của bản cài được nạp", scripts_loaded),
    Criterion("T04", "M1", "Không script nào bị sập", scripts_alive),
    Criterion("T05", "M1", "Log module không có lỗi", module_errors),
    Criterion("T06", "M1", "Game bắt tay được với Engine", handshake),
    Criterion("T07", "M5", "Engine nhận mọi yêu cầu", requests_accepted),
    Criterion("T08", "M1", "Sang ngày đúng một lần", days_advance),
    Criterion("T09", "M0", "Trạng thái người chơi tới Engine", player_synced),
    Criterion("T10", "M0", "Cư dân được đăng ký", residents_registered),
    Criterion("T11", "M2", "Nói chuyện ra chữ và có trả lời", dialogue_works),
    Criterion("T12", "M2", "Ký ức hội thoại được lưu", dialogue_remembered),
    Criterion("T13", "M3", "Vào được nhà", interiors_enter),
    Criterion("T14", "M3", "Ra được khỏi nhà", interiors_exit),
    Criterion("T15", "M3", "Nhà có chỗ tương tác", interiors_anchors),
    Criterion("T16", "M3", "Hoàn tất tương tác đồ vật", interiors_activity),
    Criterion("T17", "Quán", "Nhận ra quầy trong cửa hàng", shop_found),
    Criterion("T18", "Quán", "Có người bán và người dọn", shop_staff),
    Criterion("T19", "Quán", "Gọi món và dùng xong", shop_order),
    Criterion("T20", "M4", "Sổ cái làm chủ ngân hàng", bank_owned),
    Criterion("T21", "M4", "Event tiền không bị mất", events_delivered),
)


def evaluate(files: Dict[str, List[str]]) -> Dict[str, Verdict]:
    session = Session(files)
    return {c.code: c.check(session) for c in CRITERIA}


def report(current: Dict[str, List[str]], baseline: Optional[Dict[str, List[str]]] = None) -> str:
    results = evaluate(current)
    before = evaluate(baseline) if baseline is not None else None
    version = Session(current).version()
    counts = {v: sum(1 for r, _ in results.values() if r == v) for v in (PASS, FAIL, UNMEASURED)}
    lines = ["# Kết quả chấm log", "",
             "Bản cài: %s" % (".".join(map(str, version)) if version else "không rõ"), "",
             "Đạt %d · Không đạt %d · Chưa đo %d (trên %d tiêu chí)" %
             (counts[PASS], counts[FAIL], counts[UNMEASURED], len(CRITERIA)), ""]
    head = ["Mã", "Module", "Kỳ vọng"] + (["Bản đối chứng"] if before else []) + ["Kết quả", "Bằng chứng"]
    lines += ["| " + " | ".join(head) + " |", "|" + "---|" * len(head)]
    for c in CRITERIA:
        verdict, evidence = results[c.code]
        row = [c.code, c.module, c.expectation]
        if before:
            row.append(before[c.code][0])
        lines.append("| " + " | ".join(row + [verdict, evidence.replace("|", "/")]) + " |")
    if before:
        worse = [c.code for c in CRITERIA if before[c.code][0] == PASS and results[c.code][0] == FAIL]
        fixed = [c.code for c in CRITERIA if before[c.code][0] == FAIL and results[c.code][0] == PASS]
        lines += ["", "Hỏng thêm so với bản đối chứng: %s" % (", ".join(worse) or "không"),
                  "Đã sửa được: %s" % (", ".join(fixed) or "chưa có tiêu chí nào chuyển sang đạt")]
    return "\n".join(lines) + "\n"


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("bundle", type=Path)
    parser.add_argument("--baseline", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args(argv)
    current = load(args.bundle)
    text = report(current, load(args.baseline) if args.baseline else None)
    if args.output:
        args.output.write_text(text, encoding="utf-8")
    else:
        sys.stdout.reconfigure(encoding="utf-8")
        print(text)
    return 1 if any(v == FAIL for v, _ in evaluate(current).values()) else 0


if __name__ == "__main__":
    sys.exit(main())
