"""Quan hệ xã hội (trụ 4): danh bạ, SMS, hẹn hò/kết hôn/quà, trí nhớ dài hạn (tóm tắt), bảng tin mạng xã hội.

Game (GtaVietLifePersonal.cs) gọi: GET /contacts, POST /sms, POST /relation, GET /feed, GET /npc?key=.
GtaVietLifeCore.cs đẩy lô sự kiện bus vào POST /gvl_event (xem docs/CORE.md).
Trả về chữ KHÔNG DẤU cho game (font GTA), phụ đề có dấu hiện qua overlay.
"""
import json
import random
import threading
import time
import unicodedata

from life import relation

STATUS_ASCII = {"hen_ho": "hen ho", "vo_chong": "vo chong", "chia_tay": ""}

# sự kiện bus -> bài đăng mạng xã hội (ẩn danh, như tin đồn trong thành phố)
FEED_TEXT = {
    "legal.jail": "Tin nóng: một người vừa bị tống giam ở trại Bolingbroke.",
    "legal.crash": "Lại thêm một vụ va chạm xe trên phố, chủ xe bức xúc đòi bồi thường.",
    "legal.court": "Toà vừa xử xong một vụ bồi thường dân sự vì đâm xe.",
    "legal.stop": "CSGT đang lập chốt kiểm tra giấy tờ, anh em đi đường nhớ mang bằng lái.",
    "social.marry": "Có đám cưới trong khu! Chúc mừng đôi uyên ương.",
    "social.breakup": "Nghe nói có cặp vừa đường ai nấy đi...",
    "social.pet": "Ai vừa nhận nuôi một bé cún dễ thương thế kia?",
    "life.event": "Vừa có chuyện ồn ào ngoài phố, mọi người cẩn thận nhé.",
    "life.powercut": "Mất điện cả khu, nóng không chịu nổi.",
    "econ.foreclose": "Ngân hàng vừa siết nợ một căn nhà trong thành phố.",
    "econ.impound": "Lại có xe bị cẩu về bãi Davis vì đỗ sai chỗ.",
}


class Social:
    def __init__(self, bridge):
        self.b = bridge
        self.feed = []   # (ts, text)
        self.lock = threading.Lock()
        self.summarizing = set()

    # ------------------------------------------------------------ danh bạ
    def contacts(self) -> str:
        out = []
        for n in self.b.memory.contacts():
            st = STATUS_ASCII.get(n.get("status") or "", "")
            out.append("|".join([n["key"], self._a(n["name"]), self._a(relation(n["affinity"])), str(n["affinity"]), st]))
        return "\n".join(out)

    def npc(self, key: str) -> str:
        n = self.b.memory.get(key)
        return json.dumps(n, ensure_ascii=False) if n else "{}"

    # ------------------------------------------------------------ SMS
    def sms(self, ctx: dict) -> str:
        key, text = ctx.get("key", ""), (ctx.get("text") or "").strip()
        n = self.b.memory.get(key)
        if not n or not text:
            return "(so khong ton tai)"
        c = {"sms": "1", "hour": self.b.life.state.get("hour", "12"), "street": "(nhắn tin)", "zone": "?"}
        c["extra"] = self.b.life.context(n)
        try:
            res = self.b.brain.chat(n, c, self.b.memory.history(key), text)
        except Exception as e:
            print(f"[sms] lỗi: {e}")
            return "(khong tra loi)"
        self.b.memory.add(key, "player", "[SMS] " + text)
        self.b.memory.add(key, "npc", "[SMS] " + res["reply"])
        if res.get("affinity_delta"):
            self._affinity(n, res["affinity_delta"])
        self.b.overlay.show("npc", f"📱 {n['name']}: {res['reply']}", 7000)
        print(f"[sms] Sếp → {n['name']}: {text} | {res['reply']}")
        self.after_talk(key)
        return self._a(res["reply"])

    # ------------------------------------------------------------ hẹn hò, kết hôn, quà, chia tay
    def relation(self, ctx: dict) -> str:
        key, action = ctx.get("key", ""), ctx.get("action", "")
        n = self.b.memory.get(key)
        if not n:
            return "no\n(khong tim thay nguoi nay)"
        aff, status = n["affinity"], n.get("status") or ""
        ok, say, new_status, delta = False, "", None, 0
        if action == "date":
            ok = aff >= 60 and status != "vo_chong" and random.random() < 0.85
            say = "Người chơi ngỏ lời muốn hẹn hò, làm người yêu của bạn."
            delta = 10 if ok else -5
            new_status = "hen_ho" if ok else None
        elif action == "marry":
            ok = status == "hen_ho" and aff >= 85 and random.random() < 0.8
            say = "Người chơi cầu hôn bạn, muốn cưới bạn làm vợ/chồng."
            delta = 15 if ok else -8
            new_status = "vo_chong" if ok else None
        elif action == "breakup":
            ok = True
            say = "Người chơi nói lời chia tay với bạn."
            delta = -30
            new_status = "chia_tay"
        elif action == "gift":
            try:
                value = int(ctx.get("value", "0"))
            except ValueError:
                value = 0
            ok = True
            say = f"Người chơi vừa gửi tặng bạn một món quà trị giá khoảng {value} đô."
            delta = max(3, min(20, 3 + value // 8))
        else:
            return "no\n?"
        decision = ("Bạn ĐỒNG Ý / vui vẻ đón nhận." if ok else "Bạn TỪ CHỐI một cách tế nhị (hoặc thẳng thừng nếu đang ghét).")
        c = {"phone": "1", "hour": self.b.life.state.get("hour", "12"), "street": "(điện thoại)", "zone": "?",
             "extra": self.b.life.context(n) + "\n" + decision}
        try:
            res = self.b.brain.chat(n, c, self.b.memory.history(key), "(" + say + ")")
            reply = res["reply"]
        except Exception as e:
            print(f"[relation] lỗi: {e}")
            reply = "Ừ." if ok else "Thôi, để sau đi."
        self.b.memory.add(key, "player", "(" + say + ")")
        self.b.memory.add(key, "npc", reply)
        self.b.memory.set_fields(key, **({"status": new_status} if new_status else {}))
        self._affinity(n, delta, scale=1)
        self.b.overlay.show("npc", f"📱 {n['name']}: {reply}", 7000)
        if new_status == "vo_chong":
            self.b.life.add_rumor(f"{n['name']} vừa nhận lời cầu hôn, sắp có đám cưới!")
        print(f"[relation] {action} {n['name']} ({aff}) -> {'ok' if ok else 'no'}: {reply}")
        return ("ok" if ok else "no") + "\n" + self._a(reply)

    # ------------------------------------------------------------ thiện cảm, tin đồn khi bị ghét
    def _affinity(self, n: dict, delta: int, scale: int = 5) -> int:
        new = self.b.memory.change_affinity(n["key"], delta, scale)
        self.level_change(n, n["affinity"], new)
        return new

    def level_change(self, n: dict, old: int, new: int):
        if relation(old) == relation(new):
            return
        print(f"[quan hệ] {n['name']}: {relation(old)} → {relation(new)} ({new})")
        if new <= -40 < old:
            self.b.life.add_rumor(f"{n['name']} ({n['job']}) đang kể xấu người chơi với mọi người xung quanh.")
            self.post(f"{n['name']} vừa đăng status bóng gió về một người quen \"không ra gì\".")
        elif new >= 60 > old:
            self.post(f"{n['name']} vừa khoe có thêm một người bạn thân mới.")

    # ------------------------------------------------------------ trí nhớ dài hạn: tóm tắt sau mỗi 10 câu
    def after_talk(self, key: str):
        if not key.startswith("npc_"):
            return
        n = self.b.memory.get(key)
        if not n or n["msgs"] == 0 or n["msgs"] % 10 != 0 or key in self.summarizing:
            return
        self.summarizing.add(key)
        threading.Thread(target=self._summarize, args=(n,), daemon=True).start()

    def _summarize(self, n: dict):
        try:
            rows = self.b.memory.recent_text(n["key"], 24)
            convo = "\n".join(("Người chơi: " if r == "player" else f"{n['name']}: ") + t for r, t in rows)
            prompt = ("Tóm tắt NGẮN (tối đa 60 từ, tiếng Việt) những điều quan trọng " + n["name"] +
                      " nên nhớ lâu dài về người chơi: tên, việc họ làm, lời hứa, chuyện vui buồn, mâu thuẫn, món nợ, sở thích. "
                      "Giữ lại ý cũ còn đúng. Trí nhớ cũ: " + (n.get("summary") or "(chưa có)") +
                      "\nĐoạn hội thoại gần đây:\n" + convo +
                      '\nChỉ trả về JSON: {"summary": "..."}')
            msgs = [{"role": "user", "content": prompt}]
            text = None
            if self.b.cfg.get("groq_api_key"):
                try:
                    text = self.b.brain._groq_text(msgs)
                except Exception as e:
                    print(f"[nhớ] Groq lỗi: {e}")
            if text is None:
                text = self.b.brain._ollama_text(msgs, num_predict=200)
            s = text[text.find("{"):text.rfind("}") + 1]
            summary = str(json.loads(s).get("summary", "")).strip()
            if summary:
                self.b.memory.set_fields(n["key"], summary=summary[:600])
                print(f"[nhớ] {n['name']}: {summary}")
        except Exception as e:
            print(f"[nhớ] không tóm tắt được {n['name']}: {e}")
        finally:
            self.summarizing.discard(n["key"])

    # ------------------------------------------------------------ bus sự kiện game + mạng xã hội
    def gvl_event(self, ctx: dict) -> str:
        for line in (ctx.get("lines") or "").split("\n"):
            p = line.split("\t")
            if len(p) != 3:
                continue
            typ = p[1]
            text = FEED_TEXT.get(typ)
            if text and random.random() < 0.7:
                self.post(text)
                if typ in ("legal.jail", "legal.crash", "social.marry", "life.event"):
                    self.b.life.add_rumor(text)
        return "ok"

    def post(self, text: str):
        with self.lock:
            self.feed.append((time.time(), text))
            self.feed = self.feed[-30:]

    def feed_text(self) -> str:
        with self.lock:
            items = list(self.feed)
        rumors = [t for _, t in getattr(self.b.life, "rumors", [])]
        lines = []
        for ts, t in reversed(items[-8:]):
            mins = int((time.time() - ts) / 60)
            lines.append(f"[{mins} phut truoc] {self._a(t)}")
        for t in reversed(rumors[-4:]):
            lines.append("[tin don] " + self._a(t))
        return "\n".join(lines[:12])

    @staticmethod
    def _a(s: str) -> str:
        s = (s or "").replace("đ", "d").replace("Đ", "D")
        s = "".join(c for c in unicodedata.normalize("NFD", s) if unicodedata.category(c) != "Mn")
        return s.replace("|", "/").replace("\n", " ")
