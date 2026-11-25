"""Bộ não NPC: Gemini (gói miễn phí) là chính, Ollama chạy local là dự phòng."""
import random
import re
import json
import time
import urllib.error
import urllib.request

ACTIONS = ("none", "follow", "unfollow", "flee", "fight")

SYSTEM = """Bạn đóng vai một cư dân trong thế giới game mô phỏng đời sống. Người chơi đang đứng trước mặt bạn và nói chuyện bằng giọng nói.
Quy tắc:
- Luôn nói tiếng Việt tự nhiên, đời thường, như người thật ngoài phố. Ngắn gọn 1-2 câu, tối đa khoảng 25 từ.
- TRẢ LỜI ĐÚNG ĐIỀU người chơi vừa nói/hỏi trước, rồi mới thêm ý riêng nếu cần. Không lan man, không đổi chủ đề, không hỏi dồn nhiều câu một lúc.
- Không bịa địa danh, cửa hàng hay sự kiện cụ thể mà ngữ cảnh không có. Không biết thì nói không biết hoặc hỏi lại.
- Nếu người chơi trông đói, khát, mệt, mắc nợ, có tiếng tốt/xấu... bạn có thể để ý và nói tới khi hợp lúc.
- Giữ đúng tên, nghề nghiệp, tính cách, tâm trạng và độ thân thiết đã cho. Nhớ những gì hai bên đã nói trước đó.
- Phản ứng theo hoàn cảnh: giờ giấc, thời tiết, nơi đang đứng, người chơi đang bị truy nã hay không.
- Không bao giờ nhắc tới việc mình là AI, là game hay là nhân vật ảo.
- GIỌNG ĐIỆU: nói đúng "phong cách nói" được giao (vùng miền, tuổi, xưng hô, câu cửa miệng) và đúng nghề/tính cách; mỗi người một kiểu, không ai nói giống ai. Người trẻ dùng tiếng lóng nhẹ, người lớn tuổi chững chạc, cảnh sát cộc lốc công vụ, người bán hàng niềm nở mời chào, dân giang hồ nói cộc, người sang thì lịch sự.
- ĐÚNG NGỮ CẢNH: giọng thay đổi theo thiện cảm, giờ giấc (khuya thì uể oải/đề phòng), thời tiết (mưa thì than), người chơi cầm vũ khí/bị truy nã (sợ hoặc dè chừng), đang làm việc gì thì nói theo việc đó. Đừng chào hỏi máy móc.
- Không đọc ký hiệu, emoji, ngoặc đơn mô tả hành động; chỉ lời nói.
- Chọn một hành động cho bản thân:
  "follow": đồng ý đi theo người chơi (làm vệ sĩ, đi chơi cùng) khi được nhờ và bạn đồng ý.
  "unfollow": thôi đi theo khi người chơi bảo về hoặc bạn chán.
  "flee": bỏ chạy khi sợ hãi (bị đe doạ, thấy súng, người chơi bị truy nã cao).
  "fight": lao vào đánh khi bị xúc phạm nặng và bạn nóng tính.
  "none": các trường hợp còn lại.
- "affinity_delta" từ -2 đến 2: thiện cảm của bạn với người chơi thay đổi thế nào sau câu này.
- "tone": cảm xúc câu nói, chọn một: "binh_thuong", "vui", "cau", "so", "buon", "thi_tham", "hao_hung".
Chỉ trả về JSON đúng dạng: {"reply": "...", "action": "none", "affinity_delta": 0, "tone": "binh_thuong"}"""


def _post_json(url: str, body: dict, timeout: float = 20.0) -> dict:
    req = urllib.request.Request(url, data=json.dumps(body).encode("utf-8"),
                                 headers={"Content-Type": "application/json"}, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        try:
            detail = e.read().decode("utf-8", "replace")[:300].replace("\n", " ")
        except Exception:
            detail = ""
        print(f"[llm]   chi tiết lỗi {e.code}: {detail}")
        raise


TONES = ("binh_thuong", "vui", "cau", "so", "buon", "thi_tham", "hao_hung")

REGIONS = [
    ("miền Bắc (Hà Nội)", "xưng tôi/em/anh/chị, dùng 'nhỉ', 'đấy', 'thế', 'ạ', 'vâng', 'chứ lị'"),
    ("miền Nam (Sài Gòn)", "dùng 'nè', 'nghen', 'hen', 'dị', 'hông', 'má', 'tui', 'bữa nay', 'trời ơi'"),
    ("miền Trung", "dùng 'mô', 'tê', 'răng', 'rứa', 'chừ', 'ni', 'nớ', giọng thân tình chất phác"),
    ("miền Nam (Sài Gòn)", "nói thẳng, hay 'ê', 'mày tao' với người thân, 'chời', 'quá trời'"),
]
AGES = [("trẻ 20 tuổi", "tiếng lóng nhẹ như 'xịn', 'căng', 'ổn áp', 'chill', hơi nhanh nhảu"),
        ("khoảng 30 tuổi", "tự nhiên đời thường"),
        ("trung niên 45 tuổi", "chững chạc, hay khuyên bảo, gọi người chơi là 'cậu/chú em/cô em'"),
        ("lớn tuổi 60 tuổi", "chậm rãi, hay kể chuyện xưa, gọi người chơi là 'con/cháu'")]
CATCH = ["hay mở đầu bằng 'À này'", "hay thở dài 'Haizz'", "hay cười 'hì hì' cuối câu", "ít khi nói thừa",
         "hay chêm 'thật đấy'", "hay hỏi lại 'phải không?'", "hay than giá cả đắt đỏ", "hay nhắc 'hồi xưa'"]


def profile_for(npc: dict):
    import hashlib
    h = int(hashlib.md5(str(npc.get("key", npc.get("name", ""))).encode("utf-8")).hexdigest(), 16)
    return h, REGIONS[h % len(REGIONS)], AGES[(h >> 4) % len(AGES)], CATCH[(h >> 8) % len(CATCH)]


def style_for(npc: dict, ctx: dict) -> str:
    h, (region, words), (age, agew), catch = profile_for(npc)
    out = [f"Phong cách nói của bạn: giọng {region}; {words}. Tuổi: {age}, {agew}. Thói quen: {catch}."]
    hint = []
    try:
        hr = int(ctx.get("hour", "12"))
    except ValueError:
        hr = 12
    if hr >= 23 or hr < 5:
        hint.append("đang khuya, giọng uể oải hoặc đề phòng")
    elif hr < 9:
        hint.append("buổi sáng sớm")
    w = str(ctx.get("weather", "")).lower()
    if any(k in w for k in ("rain", "thunder", "mưa", "drizzle", "clearing")):
        hint.append("trời đang mưa, hơi cáu hoặc than vãn")
    if ctx.get("weapon") == "1":
        hint.append("người chơi đang cầm vũ khí: dè chừng hoặc sợ")
    try:
        if int(ctx.get("wanted", "0")) >= 2:
            hint.append("người chơi đang bị truy nã: cảnh giác, sợ liên lụy (trừ khi bạn là dân giang hồ)")
    except ValueError:
        pass
    if hint:
        out.append("Bối cảnh ảnh hưởng giọng: " + "; ".join(hint) + ".")
    return "\n".join(out)


FALLBACK = ["Ơ, nói gì cơ? Ồn quá chả nghe rõ.", "Từ từ, để tôi nghĩ đã...", "Hả? Nói lại cái coi.",
            "Ừm... chuyện đó để lúc khác nhé.", "Ờ ờ, tôi đang bận chút, lát nói tiếp."]


def _parse(text: str) -> dict:
    text = (text or "").strip()
    if text.startswith("```"):
        text = text.strip("`")
        text = text[text.find("{"):]
    try:
        start, end = text.find("{"), text.rfind("}")
        data = json.loads(text[start:end + 1])
    except (ValueError, TypeError):
        data = {"reply": text}
    reply = str(data.get("reply", "")).strip() or "Hả? Nói gì cơ?"
    action = str(data.get("action", "none")).strip().lower()
    if action not in ACTIONS:
        action = "none"
    try:
        delta = max(-2, min(2, int(data.get("affinity_delta", 0))))
    except (ValueError, TypeError):
        delta = 0
    tone = str(data.get("tone", "binh_thuong")).strip().lower()
    if tone not in TONES:
        tone = "binh_thuong"
    return {"reply": reply, "action": action, "affinity_delta": delta, "tone": tone, "heard": str(data.get("heard", "")).strip()}


def build_context(npc: dict, ctx: dict) -> str:
    aff = npc["affinity"]
    mood = "rất quý" if aff >= 60 else "khá quý" if aff >= 20 else "ghét" if aff <= -40 else "khó chịu" if aff <= -15 else "bình thường"
    lines = [
        f"Bạn tên {npc['name']}, giới tính {npc['gender']}, nghề: {npc['job']}, tính cách: {npc['trait']}.",
        f"Cảm nhận của bạn về người chơi: {mood} (điểm thân thiết {aff}, thang -100 đến 100).",
        f"Đang ở: {ctx.get('street', '?')}, khu {ctx.get('zone', '?')}. Giờ: {ctx.get('hour', '?')}:{ctx.get('minute', '00')}. Thời tiết: {ctx.get('weather', '?')}.",
        f"Ngoại hình theo game: {ctx.get('model', '?')}.",
        f"Người chơi: {'đang ngồi trên xe' if ctx.get('in_vehicle') == '1' else 'đi bộ'}, mức truy nã {ctx.get('wanted', '0')}/5, có {ctx.get('money', '?')} đô.",
    ]
    lines.append(style_for(npc, ctx))
    if ctx.get("phone") == "1":
        lines.append("Hai người đang nói chuyện qua ĐIỆN THOẠI, không đứng cạnh nhau: nói như nghe máy, không có hành động đi theo/bỏ chạy/đánh nhau (action luôn là none).")
    if ctx.get("sms") == "1":
        lines.append("Đây là TIN NHẮN SMS: trả lời ngắn kiểu nhắn tin, có thể viết tắt nhẹ, không đánh dấu hành động (action luôn là none).")
    if ctx.get("following") == "1":
        lines.append("Bạn hiện đang đi theo người chơi.")
    if ctx.get("weapon") == "1":
        lines.append("Người chơi đang cầm vũ khí trên tay.")
    if ctx.get("extra"):
        lines.append(ctx["extra"])
    return "\n".join(lines)


def _get_json(url: str, timeout: float = 15.0) -> dict:
    with urllib.request.urlopen(url, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


class Brain:
    PREFERRED = ["gemini-3.8-flash", "gemini-3.7-flash", "gemini-3.6-flash", "gemini-3.5-flash",
                 "gemini-flash-latest", "gemini-3.5-flash-lite", "gemini-3.1-flash-lite", "gemini-2.5-flash"]

    def __init__(self, cfg: dict):
        self.cfg = cfg
        first = cfg.get("gemini_model", "gemini-2.5-flash")
        self.candidates = [first] + [m for m in self.PREFERRED if m != first]
        self.model = first
        self.dead = set()

    def _demote(self, m):
        if m in self.candidates:
            self.candidates.remove(m)
            self.candidates.append(m)
        if self.model == m:
            self.model = next((c for c in self.candidates if c not in self.dead), m)

    def _hedged(self, call):
        """Gọi Gemini; nếu model đầu chưa trả lời sau 2.5 giây (hoặc lỗi) thì gọi thêm model kế tiếp song song, lấy kết quả về trước."""
        import concurrent.futures as cf
        models = ([self.model] + [m for m in self.candidates if m != self.model and m not in self.dead])[:5]
        ex = cf.ThreadPoolExecutor(max_workers=len(models))
        futs, nxt, launched, last = {}, 0, 0.0, None
        try:
            while True:
                if nxt < len(models) and (not futs or time.time() - launched >= 2.0):
                    m = models[nxt]
                    futs[ex.submit(call, m)] = m
                    nxt += 1
                    launched = time.time()
                done, _ = cf.wait(list(futs), timeout=0.2, return_when=cf.FIRST_COMPLETED)
                for f in done:
                    m = futs.pop(f)
                    try:
                        result = f.result()
                        self.model = m
                        return result
                    except urllib.error.HTTPError as e:
                        last = e
                        print(f"[llm] gemini model {m} lỗi {e.code}")
                        self._demote(m)
                        if e.code in (404, 400):
                            self.dead.add(m)
                    except Exception as e:
                        last = e
                        print(f"[llm] gemini model {m} lỗi: {e}")
                        self._demote(m)
                if not futs and nxt >= len(models):
                    raise RuntimeError(f"mọi model Gemini đều lỗi: {last}")
        finally:
            ex.shutdown(wait=False)

    def chat(self, npc: dict, ctx: dict, history: list, player_text: str) -> dict:
        system = SYSTEM + "\n\n" + build_context(npc, ctx)
        prov = self.cfg.get("provider", "gemini")
        order = {"groq": ["groq", "ollama", "gemini"], "gemini": ["gemini", "groq", "ollama"]}.get(prov, ["ollama", "groq", "gemini"])
        last_err = None
        for name in order:
            if name == "groq" and self.cfg.get("groq_api_key"):
                try:
                    return self._groq_chat(system, history, player_text)
                except Exception as e:
                    last_err = e
            elif name == "gemini" and self.cfg.get("gemini_api_key"):
                try:
                    return self._hedged(lambda m: self._gemini(system, history, player_text, model=m))
                except Exception as e:
                    last_err = e
            elif name == "ollama" and self.cfg.get("ollama_enabled", True):
                try:
                    return self._ollama(system, history, player_text)
                except (urllib.error.URLError, TimeoutError, KeyError, IndexError, ValueError) as e:
                    last_err = e
                    print(f"[llm] ollama lỗi: {e}")
        print(f"[llm] không có bộ não nào trả lời được: {last_err}")
        return {"reply": random.choice(FALLBACK), "action": "none", "affinity_delta": 0}

    AUDIO_NOTE = ("\n\nNgười chơi nói bằng giọng, bạn nhận được file âm thanh tiếng Việt. Hãy nghe, chép lại nguyên văn "
                  "lời họ vào trường \"heard\" (để trống \"\" nếu không có tiếng người nói rõ hoặc chỉ là tiếng ồn), "
                  "rồi trả lời như bình thường. Chỉ trả về JSON dạng: "
                  "{\"heard\": \"...\", \"reply\": \"...\", \"action\": \"none\", \"affinity_delta\": 0}")

    def chat_audio(self, npc: dict, ctx: dict, history: list, wav_bytes: bytes) -> dict:
        """Gửi thẳng âm thanh cho Gemini: nghe và trả lời trong 1 lần gọi (nhanh hơn Whisper trên CPU)."""
        system = SYSTEM + "\n\n" + build_context(npc, ctx) + self.AUDIO_NOTE
        return self._hedged(lambda m: self._gemini(system, history, None, audio=wav_bytes, model=m))

    def _gemini(self, system: str, history: list, player_text, audio: bytes = None, model: str = None) -> dict:
        contents = [{"role": "user" if r == "player" else "model", "parts": [{"text": t}]} for r, t in history]
        if audio is not None:
            import base64
            contents.append({"role": "user", "parts": [
                {"inlineData": {"mimeType": "audio/wav", "data": base64.b64encode(audio).decode("ascii")}},
                {"text": "(Lời người chơi vừa nói, ở file âm thanh trên.)"}]})
        else:
            contents.append({"role": "user", "parts": [{"text": player_text}]})
        gen = {"responseMimeType": "application/json", "temperature": 0.9, "maxOutputTokens": 300}
        budget = self.cfg.get("gemini_thinking_budget", 0)
        if budget is not None:
            gen["thinkingConfig"] = {"thinkingBudget": budget}
        body = {"systemInstruction": {"parts": [{"text": system}]}, "contents": contents, "generationConfig": gen}
        url = (f"https://generativelanguage.googleapis.com/v1beta/models/{model or self.model}:generateContent"
               f"?key={self.cfg['gemini_api_key']}")
        data = _post_json(url, body, timeout=9.0)
        text = "".join(p.get("text", "") for p in data["candidates"][0]["content"]["parts"])
        return _parse(text)

    def _groq_chat(self, system, history, player_text) -> dict:
        msgs = [{"role": "system", "content": system}]
        msgs += [{"role": "user" if r == "player" else "assistant", "content": t} for r, t in history]
        msgs.append({"role": "user", "content": player_text})
        return _parse(self._groq_text(msgs))

    def _groq_text(self, msgs) -> str:
        if not getattr(self, "groq_models", None):
            self._groq_pick_model()
        last = None
        # thu lan luot toi da 3 model: model nao loi (vd gpt-oss bao "Failed to validate JSON") thi doi model khac
        for model in list(self.groq_models)[:3]:
            body = {"model": model, "messages": msgs, "temperature": 0.9, "max_tokens": 700}
            if "gpt-oss" in model:
                body["reasoning_effort"] = "low"
            if "qwen" in model:
                body["reasoning_format"] = "hidden"   # khong tra ve phan suy nghi <think>
            req = urllib.request.Request("https://api.groq.com/openai/v1/chat/completions",
                                         data=json.dumps(body).encode("utf-8"), method="POST",
                                         headers={"Content-Type": "application/json", "User-Agent": "gta-viet-life",
                                                  "Authorization": "Bearer " + self.cfg["groq_api_key"]})
            try:
                with urllib.request.urlopen(req, timeout=12) as r:
                    data = json.loads(r.read().decode("utf-8"))
                text = data["choices"][0]["message"].get("content") or ""
                text = re.sub(r"<think>.*?</think>", "", text, flags=re.S)
                if not text.strip():
                    raise ValueError("model tra ve rong")
                if self.groq_models[0] != model:   # nho model chay duoc cho lan sau
                    self.groq_models.remove(model)
                    self.groq_models.insert(0, model)
                    print(f"[llm] groq doi sang model {model}")
                return text
            except urllib.error.HTTPError as e:
                last = e
                print(f"[llm]   groq {model} lỗi {e.code}: {e.read().decode('utf-8', 'replace')[:160]}")
                if e.code == 429:
                    raise
            except (ValueError, KeyError, IndexError, TimeoutError, urllib.error.URLError) as e:
                last = e
                print(f"[llm]   groq {model} lỗi: {e}")
        raise last or RuntimeError("groq khong tra loi")

    def _groq_pick_model(self):
        req = urllib.request.Request("https://api.groq.com/openai/v1/models", headers={
            "User-Agent": "gta-viet-life", "Authorization": "Bearer " + self.cfg["groq_api_key"]})
        with urllib.request.urlopen(req, timeout=10) as r:
            ids = [m["id"] for m in json.loads(r.read().decode("utf-8")).get("data", [])]
        print(f"[llm] groq có các model: {ids}")
        chat = [i for i in ids if not any(x in i.lower() for x in ("whisper", "guard", "tts", "playai", "orpheus", "embed", "allam"))]
        def rank(i):
            l = i.lower()
            return (0 if "llama" in l and "70b" in l else 1 if "qwen" in l else 2 if "llama" in l and "scout" in l
                    else 3 if "gpt-oss-120b" in l else 4 if "llama" in l else 5, i)
        if not chat:
            raise RuntimeError("Groq không có model chat nào")
        self.groq_models = sorted(chat, key=rank)
        pref = self.cfg.get("groq_model")
        if pref in self.groq_models:
            self.groq_models.remove(pref); self.groq_models.insert(0, pref)
        print(f"[llm] groq thu theo thu tu: {self.groq_models[:3]}")

    def transcribe_groq(self, wav: bytes) -> str:
        """Nghe bằng Whisper large-v3-turbo của Groq (miễn phí, ~0.5 giây)."""
        b = "----gtavl" + str(int(time.time() * 1000))
        def field(n, v):
            return f'--{b}\r\nContent-Disposition: form-data; name="{n}"\r\n\r\n{v}\r\n'.encode()
        body = (field("model", "whisper-large-v3-turbo") + field("language", "vi") + field("temperature", "0")
                + field("response_format", "json")
                + f'--{b}\r\nContent-Disposition: form-data; name="file"; filename="a.wav"\r\nContent-Type: audio/wav\r\n\r\n'.encode()
                + wav + f"\r\n--{b}--\r\n".encode())
        req = urllib.request.Request("https://api.groq.com/openai/v1/audio/transcriptions", data=body, method="POST",
                                     headers={"Content-Type": "multipart/form-data; boundary=" + b,
                                              "User-Agent": "gta-viet-life",
                                              "Authorization": "Bearer " + self.cfg["groq_api_key"]})
        try:
            with urllib.request.urlopen(req, timeout=10) as r:
                return json.loads(r.read().decode("utf-8")).get("text", "").strip()
        except urllib.error.HTTPError as e:
            print(f"[stt] groq lỗi {e.code}: {e.read().decode('utf-8', 'replace')[:200]}")
            raise

    def _ollama_text(self, msgs, num_predict=150) -> str:
        body = {"model": self.cfg.get("ollama_model", "qwen2.5:3b"), "messages": msgs,
                "stream": False, "format": "json", "keep_alive": "60m",
                "options": {"temperature": 0.95, "num_predict": num_predict, "num_ctx": 2048,
                            "num_gpu": int(self.cfg.get("ollama_gpu_layers", 0))}}
        url = self.cfg.get("ollama_url", "http://127.0.0.1:11434") + "/api/chat"
        return _post_json(url, body, timeout=45.0)["message"]["content"]

    BANTER = ("Bạn viết một đoạn hội thoại ngắn, tự nhiên giữa hai người Việt đang đứng cạnh nhau ngoài phố "
              "(nói như đời thường ở Việt Nam, khẩu ngữ, có thể dùng từ địa phương theo quê). "
              "2 đến 4 lượt, xen kẽ A và B, mỗi lượt MỘT câu ngắn dưới 18 từ. "
              "Nội dung hợp với nghề, tính cách, tuổi, giờ giấc, thời tiết, chỗ đang đứng hoặc tin đồn gần đây "
              "(chuyện cơm áo, giá cả, việc làm, kẹt xe, chuyện hàng xóm, trêu nhau...). "
              "Không nhắc tới game hay người chơi, không chào khán giả. "
              "Chỉ trả về JSON: {\"lines\": [{\"who\": \"A\", \"text\": \"...\", \"tone\": \"binh_thuong\"}]} "
              "với tone là một trong: " + ", ".join(TONES) + ".")

    def banter(self, a: dict, b: dict, ctx: dict) -> list:
        """Hai NPC noi chuyen voi nhau. Tra ve [(who 'A'/'B', text, tone)]; loi thi tra ve []."""
        def desc(n):
            h, (region, words), (age, _agew), catch = profile_for(n)
            return f"{n['name']} ({n['gender']}, {age}, giọng {region}: {words}, làm {n['job']}, tính {n['trait']})"
        rel = random.choice(["hai người lạ vừa đứng cạnh nhau", "hai người quen sơ sơ cùng khu", "hai người bạn lâu năm",
                             "hàng xóm", "đồng nghiệp tình cờ gặp"])
        user = (f"A là {desc(a)}. B là {desc(b)}. Quan hệ: {rel}. "
                f"Lúc này {ctx.get('hour', '?')} giờ, thời tiết {ctx.get('weather', '?')}, ở {ctx.get('street', 'ngoài phố')}. "
                + (f"Tin đồn gần đây: {ctx.get('extra')}" if ctx.get("extra") else ""))
        msgs = [{"role": "system", "content": self.BANTER}, {"role": "user", "content": user}]
        text = None
        if self.cfg.get("groq_api_key"):
            try:
                text = self._groq_text(msgs)
            except Exception as e:
                print(f"[banter] groq lỗi: {e}")
        if text is None and self.cfg.get("ollama_enabled", True):
            try:
                text = self._ollama_text(msgs, num_predict=300)
            except Exception as e:
                print(f"[banter] ollama lỗi: {e}")
        if not text:
            return []
        try:
            t = text.strip()
            data = json.loads(t[t.find("{"):t.rfind("}") + 1])
            out = []
            for ln in data.get("lines", [])[:4]:
                who = "B" if str(ln.get("who", "A")).strip().upper().startswith("B") else "A"
                tx = str(ln.get("text", "")).strip()
                tone = str(ln.get("tone", "binh_thuong")).strip().lower()
                if tx:
                    out.append((who, tx, tone if tone in TONES else "binh_thuong"))
            return out
        except (ValueError, TypeError, AttributeError) as e:
            print(f"[banter] không đọc được JSON: {e}")
            return []

    NEWS = ("Bạn là phát thanh viên đài phát thanh thành phố, đọc bản tin ngắn bằng tiếng Việt tự nhiên, "
            "giọng thời sự nhưng gần gũi. Viết 3 đến 4 câu, mỗi câu một tin, dưới 30 từ. Câu đầu chào khán giả kèm giờ "
            "và thứ trong tuần, sau đó điểm các sự kiện gần đây (chỉ dùng sự kiện được cung cấp, không bịa thêm vụ việc), "
            "câu cuối là thời tiết và lời khuyên đi đường. Không nhắc tới game. "
            "Chỉ trả về JSON: {\"lines\": [\"...\", \"...\"]}")

    def news(self, ctx: dict, events: list) -> list:
        """Ban tin radio tu cac su kien gan day. Loi thi tra ve [] (cau noi tu doc tin don tho)."""
        user = (f"Bây giờ là {ctx.get('hour', '?')} giờ, {ctx.get('weekday', '')}, thời tiết {ctx.get('weather', '?')}. "
                "Sự kiện gần đây: " + (" | ".join(events) if events else "không có gì đặc biệt, thành phố bình yên."))
        msgs = [{"role": "system", "content": self.NEWS}, {"role": "user", "content": user}]
        text = None
        if self.cfg.get("groq_api_key"):
            try:
                text = self._groq_text(msgs)
            except Exception as e:
                print(f"[news] groq lỗi: {e}")
        if text is None and self.cfg.get("ollama_enabled", True):
            try:
                text = self._ollama_text(msgs, num_predict=350)
            except Exception as e:
                print(f"[news] ollama lỗi: {e}")
        if not text:
            return []
        try:
            t = text.strip()
            data = json.loads(t[t.find("{"):t.rfind("}") + 1])
            return [str(x).strip() for x in data.get("lines", []) if str(x).strip()][:5]
        except (ValueError, TypeError, AttributeError) as e:
            print(f"[news] không đọc được JSON: {e}")
            return []

    def _ollama(self, system: str, history: list, player_text: str) -> dict:
        msgs = [{"role": "system", "content": system}]
        msgs += [{"role": "user" if r == "player" else "assistant", "content": t} for r, t in history]
        msgs.append({"role": "user", "content": player_text})
        body = {"model": self.cfg.get("ollama_model", "qwen2.5:3b"), "messages": msgs,
                "stream": False, "format": "json", "keep_alive": "60m",
                "options": {"temperature": 0.9, "num_predict": 150, "num_ctx": 2048,
                            "num_gpu": int(self.cfg.get("ollama_gpu_layers", 0))}}   # 0 = chạy CPU, nhường card đồ hoạ cho game
        url = self.cfg.get("ollama_url", "http://127.0.0.1:11434") + "/api/chat"
        data = _post_json(url, body, timeout=45.0)
        return _parse(data["message"]["content"])

    def warmup_ollama(self):
        """Nạp sẵn model Ollama vào RAM để lần dự phòng đầu tiên không bị chờ lâu."""
        if not self.cfg.get("ollama_enabled", True):
            return
        try:
            body = {"model": self.cfg.get("ollama_model", "qwen2.5:7b"), "prompt": "", "keep_alive": "60m",
                    "options": {"num_gpu": int(self.cfg.get("ollama_gpu_layers", 0))}}
            _post_json(self.cfg.get("ollama_url", "http://127.0.0.1:11434") + "/api/generate", body, timeout=120.0)
            print("[llm] Ollama đã nạp sẵn model trên máy (dự phòng không giới hạn).")
        except Exception:
            print("[llm] Chưa thấy Ollama chạy trên máy (chưa cài thì bỏ qua, vẫn dùng Groq).")
