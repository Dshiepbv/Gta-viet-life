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
        self.db.execute("DELETE FROM msg WHERE key LIKE 'tmp_%'")
        self.db.execute("DELETE FROM npc WHERE key LIKE 'tmp_%'")
        self.db.commit()

    def get_or_create(self, key: str, gender: str, ped_type: int, name: str = "") -> dict:
        with self.lock:
            row = self.db.execute(
                "SELECT key,name,gender,job,trait,pitch,rate,affinity FROM npc WHERE key=?", (key,)).fetchone()
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
                    affinity=0,
                )
                self.db.execute(
                    "INSERT INTO npc(key,name,gender,job,trait,pitch,rate,affinity,created) VALUES(?,?,?,?,?,?,?,?,?)",
                    (npc["key"], npc["name"], npc["gender"], npc["job"], npc["trait"],
                     npc["pitch"], npc["rate"], 0, time.time()))
                self.db.commit()
                return npc
            cols = ["key", "name", "gender", "job", "trait", "pitch", "rate", "affinity"]
            return dict(zip(cols, row))

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
            self.db.commit()

    def change_affinity(self, key: str, delta: int):
        with self.lock:
            self.db.execute(
                "UPDATE npc SET affinity=MAX(-10, MIN(10, affinity+?)) WHERE key=?", (int(delta), key))
            self.db.commit()
