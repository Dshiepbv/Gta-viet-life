"""Trí nhớ NPC: tính cách + lịch sử trò chuyện, lưu SQLite.

Key bắt đầu bằng "tmp_" là người đi đường ngẫu nhiên: xoá khi khởi động lại.
Key bắt đầu bằng "npc_" là NPC Sếp đã đánh dấu (phím K): nhớ lâu dài.
"""
import random
import sqlite3
import threading
import time

NAMES_MALE = ["Tuấn", "Hùng", "Dũng", "Minh", "Long", "Quang", "Phong", "Bảo", "Thắng", "Khoa",
              "Hải", "Sơn", "Trung", "Đạt", "Nam", "Huy", "Lộc", "Tài", "Vinh", "Cường"]
NAMES_FEMALE = ["Lan", "Hoa", "Mai", "Linh", "Trang", "Thảo", "Ngọc", "Hương", "Vy", "Nhung",
                "Hạnh", "Yến", "Phương", "Thu", "Hằng", "Quyên", "My", "Trâm", "Diệp", "Oanh"]
CIVIL_JOBS = ["bán bánh mì đầu hẻm", "tài xế xe ôm công nghệ", "nhân viên văn phòng", "sinh viên năm hai",
              "thợ sửa xe máy", "chủ quán cà phê nhỏ", "bảo vệ toà nhà", "công nhân xây dựng",
              "người về hưu hay đi dạo", "dân chơi đường phố", "nhân viên bán hàng", "shipper giao đồ ăn",
              "kế toán", "người tập gym", "nhạc công đường phố", "môi giới bất động sản"]
TRAITS = ["vui vẻ, hay đùa", "cộc cằn, ít nói", "tốt bụng, nhiệt tình", "đa nghi, cảnh giác",
          "lắm chuyện, thích buôn dưa", "nóng tính, dễ cáu", "nhút nhát", "tự tin, hơi khoe khoang",
          "lịch sự, chừng mực", "mệt mỏi vì công việc"]
PITCHES = ["-12Hz", "-6Hz", "+0Hz", "+0Hz", "+6Hz", "+12Hz"]
RATES = ["-5%", "+0%", "+0%", "+8%", "+15%"]

# Loại ped trong GTA (GET_PED_TYPE)
PED_TYPE_JOBS = {6: "cảnh sát tuần tra", 27: "cảnh sát đặc nhiệm", 29: "lính", 20: "nhân viên cứu thương",
                 21: "lính cứu hoả", 7: "thành viên băng nhóm", 8: "thành viên băng nhóm",
                 9: "thành viên băng nhóm", 10: "thành viên băng nhóm", 11: "thành viên băng nhóm",
                 12: "thành viên băng nhóm", 13: "thành viên băng nhóm", 14: "thành viên băng nhóm",
                 19: "gái làng chơi", 22: "người buôn ma tuý", 26: "người lang thang"}


class Memory:
    def __init__(self, path: str):
        self.lock = threading.Lock()
        self.db = sqlite3.connect(path, check_same_thread=False)
        self.db.executescript("""
            CREATE TABLE IF NOT EXISTS npc(
                key TEXT PRIMARY KEY, name TEXT, gender TEXT, job TEXT, trait TEXT,
                pitch TEXT, rate TEXT, affinity INTEGER DEFAULT 0, created REAL);
            CREATE TABLE IF NOT EXISTS msg(
                id INTEGER PRIMARY KEY AUTOINCREMENT, key TEXT, role TEXT, text TEXT, ts REAL);
            CREATE INDEX IF NOT EXISTS msg_key ON msg(key);
        """)
        self._migrate()
        self.db.execute("DELETE FROM msg WHERE key LIKE 'tmp_%'")
        self.db.execute("DELETE FROM npc WHERE key LIKE 'tmp_%'")
        self.db.commit()

    def _migrate(self):
        """v1: thiện cảm -100..100 (cũ -10..10, nhân 10), thêm cột quan hệ/tóm tắt/điện thoại."""
        cols = {r[1] for r in self.db.execute("PRAGMA table_info(npc)").fetchall()}
        for name, decl in (("status", "TEXT DEFAULT ''"), ("summary", "TEXT DEFAULT ''"),
                           ("msgs", "INTEGER DEFAULT 0"), ("last_talk", "REAL DEFAULT 0"),
                           ("phone", "INTEGER DEFAULT 0")):
            if name not in cols:
                self.db.execute(f"ALTER TABLE npc ADD COLUMN {name} {decl}")
        ver = self.db.execute("PRAGMA user_version").fetchone()[0]
        if ver < 1:
            self.db.execute("UPDATE npc SET affinity = affinity * 10")
            self.db.execute("UPDATE npc SET msgs = (SELECT COUNT(*) FROM msg WHERE msg.key = npc.key)")
            self.db.execute("PRAGMA user_version = 1")
        self.db.commit()

    COLS = ["key", "name", "gender", "job", "trait", "pitch", "rate", "affinity", "status", "summary", "msgs", "phone"]

    def get(self, key: str):
        with self.lock:
            row = self.db.execute("SELECT " + ",".join(self.COLS) + " FROM npc WHERE key=?", (key,)).fetchone()
        return dict(zip(self.COLS, row)) if row else None

    def get_or_create(self, key: str, gender: str, ped_type: int, name: str = "") -> dict:
        with self.lock:
            row = self.db.execute(
                "SELECT " + ",".join(self.COLS) + " FROM npc WHERE key=?", (key,)).fetchone()
            if row is None:
                female = gender.lower().startswith("f")
                npc = dict(
                    key=key,
                    name=name.strip() or random.choice(NAMES_FEMALE if female else NAMES_MALE),
                    gender="nữ" if female else "nam",
                    job=PED_TYPE_JOBS.get(ped_type) or random.choice(CIVIL_JOBS),
                    trait=random.choice(TRAITS),
                    pitch=random.choice(PITCHES),
                    rate=random.choice(RATES),
                    affinity=0, status="", summary="", msgs=0, phone=0,
                )
                self.db.execute(
                    "INSERT INTO npc(key,name,gender,job,trait,pitch,rate,affinity,created) VALUES(?,?,?,?,?,?,?,?,?)",
                    (npc["key"], npc["name"], npc["gender"], npc["job"], npc["trait"],
                     npc["pitch"], npc["rate"], 0, time.time()))
                self.db.commit()
                return npc
            return dict(zip(self.COLS, row))

    def history(self, key: str, limit: int = 12) -> list:
        with self.lock:
            rows = self.db.execute(
                "SELECT role,text FROM msg WHERE key=? ORDER BY id DESC LIMIT ?", (key, limit)).fetchall()
        rows.reverse()
        # Gemini cần lượt đầu tiên là của người chơi
        while rows and rows[0][0] != "player":
            rows.pop(0)
        return rows

    def add(self, key: str, role: str, text: str):
        with self.lock:
            self.db.execute("INSERT INTO msg(key,role,text,ts) VALUES(?,?,?,?)", (key, role, text, time.time()))
            if role == "player":
                self.db.execute("UPDATE npc SET msgs=msgs+1, last_talk=? WHERE key=?", (time.time(), key))
            self.db.commit()

    def change_affinity(self, key: str, delta: int, scale: int = 5) -> int:
        """delta của LLM là -2..2, nhân scale ra điểm thiện cảm -100..100. Trả về giá trị mới."""
        with self.lock:
            self.db.execute(
                "UPDATE npc SET affinity=MAX(-100, MIN(100, affinity+?)) WHERE key=?", (int(delta) * scale, key))
            if key.startswith("npc_"):
                self.db.execute("UPDATE npc SET phone=1 WHERE key=? AND affinity>=10", (key,))
            self.db.commit()
            row = self.db.execute("SELECT affinity FROM npc WHERE key=?", (key,)).fetchone()
        return row[0] if row else 0

    def set_fields(self, key: str, **kw):
        if not kw:
            return
        with self.lock:
            self.db.execute("UPDATE npc SET " + ",".join(f"{k}=?" for k in kw) + " WHERE key=?",
                            list(kw.values()) + [key])
            self.db.commit()

    def contacts(self) -> list:
        """NPC đã ghi nhớ (K) hoặc có số điện thoại, thân nhất trước."""
        with self.lock:
            rows = self.db.execute(
                "SELECT " + ",".join(self.COLS) + " FROM npc WHERE key LIKE 'npc_%' OR phone=1 "
                "ORDER BY affinity DESC LIMIT 30").fetchall()
        return [dict(zip(self.COLS, r)) for r in rows]

    def recent_text(self, key: str, limit: int = 20) -> list:
        with self.lock:
            rows = self.db.execute(
                "SELECT role,text FROM msg WHERE key=? ORDER BY id DESC LIMIT ?", (key, limit)).fetchall()
        rows.reverse()
        return rows
