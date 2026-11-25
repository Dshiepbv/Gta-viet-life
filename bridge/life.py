"""Đời sống: lịch sinh hoạt NPC, trạng thái của Sếp (nhu cầu, việc làm, nợ...), tin đồn và quan hệ.

Script thế giới trong game (GtaVietLifeWorld.cs) gửi trạng thái về /state và sự kiện về /event.
Mọi thứ ở đây được thêm vào "ngữ cảnh" của NPC để họ nói chuyện đúng hoàn cảnh.
"""
import threading
import time

ACTIVE_JOBS = {
    "bán bánh mì đầu hẻm": "đứng xe bánh mì", "tài xế xe ôm công nghệ": "chạy xe chờ khách",
    "nhân viên văn phòng": "làm việc ở công ty", "sinh viên năm hai": "đi học",
    "thợ sửa xe máy": "sửa xe ở tiệm", "chủ quán cà phê nhỏ": "trông quán cà phê",
    "bảo vệ toà nhà": "trực ở toà nhà", "công nhân xây dựng": "làm ở công trường",
    "người về hưu hay đi dạo": "đi dạo", "dân chơi đường phố": "la cà ngoài phố",
    "nhân viên bán hàng": "đứng quầy bán hàng", "shipper giao đồ ăn": "chạy giao đồ ăn",
    "kế toán": "làm sổ sách ở văn phòng", "người tập gym": "tập gym",
    "nhạc công đường phố": "chơi nhạc ở góc phố", "môi giới bất động sản": "dẫn khách đi xem nhà",
}


def activity(job: str, hour: int) -> str:
    """NPC đang làm gì lúc này, theo nghề và giờ trong game."""
    work = ACTIVE_JOBS.get(job, "làm việc")
    if hour >= 23 or hour < 5:
        return "khuya rồi, đáng lẽ bạn đang ngủ nhưng còn thức vì có việc hoặc đi chơi muộn, hơi buồn ngủ"
    if hour < 7:
        return "mới dậy sớm, đang đi ra ngoài, hơi uể oải"
    if hour < 11:
        return f"đang trong ca sáng, bận {work}"
    if hour < 13:
        return "đang giờ nghỉ trưa, đói bụng, định đi ăn cơm"
    if hour < 17:
        return f"đang trong ca chiều, bận {work}"
    if hour < 20:
        return "vừa tan làm, đang trên đường về nhà hoặc đi ăn tối"
    return "đang đi chơi hoặc kiếm gì ăn tối, rảnh rỗi hơn ban ngày"


def relation(affinity: int) -> str:
    if affinity >= 8:
        return "bạn thân"
    if affinity >= 4:
        return "bạn bè"
    if affinity >= 1:
        return "người quen"
    if affinity <= -6:
        return "kẻ thù"
    if affinity <= -2:
        return "người bạn không ưa"
    return "người lạ"


def _level(v, words):
    try:
        v = float(v)
    except (TypeError, ValueError):
        return None
    for limit, w in words:
        if v < limit:
            return w
    return words[-1][1]


class Life:
    def __init__(self):
        self.lock = threading.Lock()
        self.state = {}
        self.rumors = []  # (thời gian, nội dung)

    def update_state(self, d: dict):
        with self.lock:
            self.state.update({k: v for k, v in d.items() if v != ""})

    def add_rumor(self, text: str):
        text = text.strip()
        if not text:
            return
        with self.lock:
            self.rumors.append((time.time(), text))
            self.rumors = self.rumors[-8:]
        print(f"[tin đồn] {text}")

    def context(self, npc: dict) -> str:
        """Các dòng bổ sung đưa vào prompt NPC."""
        with self.lock:
            st = dict(self.state)
            rumors = [t for _, t in self.rumors[-3:]]
        lines = []
        try:
            hour = int(st.get("hour", "12"))
        except ValueError:
            hour = 12
        lines.append(f"Việc bạn đang làm lúc này: {activity(npc['job'], hour)}.")
        lines.append(f"Với người chơi, bạn coi họ là: {relation(npc['affinity'])}.")
        p = []
        if st.get("player_name"):
            who = f"tên {st['player_name']}"
            if st.get("hometown"):
                who += f", quê {st['hometown']}"
            lines.append(f"Người chơi {who}. Gọi đúng tên khi đã quen.")
        if st.get("outfit"):
            p.append(f"đang mặc {st['outfit']}")
        if st.get("skills"):
            p.append(f"nổi tiếng {st['skills']}")
        if st.get("job"):
            p.append(f"đang làm {st['job']}")
        if st.get("business"):
            p.append(f"chủ {st['business']}")
        rep = _level(st.get("rep"), [(-20, "tiếng xấu, nhiều người ngán"), (10, "bình thường"),
                                      (40, "được nhiều người biết và nể"), (999, "rất có tiếng tốt")])
        if rep:
            p.append(rep)
        debt = st.get("debt")
        if debt and debt not in ("0", "0.0"):
            p.append(f"đang nợ {debt} đô")
        needs = []
        for key, name, words in (("hunger", "đói", [(15, "đói lả"), (35, "hơi đói")]),
                                 ("thirst", "khát", [(15, "khát khô cổ"), (35, "hơi khát")]),
                                 ("energy", "mệt", [(15, "kiệt sức, mắt trũng"), (35, "hơi mệt")])):
            w = _level(st.get(key), words + [(999, None)])
            if w:
                needs.append(w)
        if needs:
            p.append("trông " + ", ".join(needs))
        if p:
            lines.append("Điều bạn biết/nhìn thấy ở người chơi: " + "; ".join(p) + ".")
        if rumors:
            lines.append("Tin đồn gần đây trong khu: " + " | ".join(rumors) +
                         ". Chỉ nhắc khi hợp ý câu chuyện, đừng tự nhồi vào.")
        return "\n".join(lines)
