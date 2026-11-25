"""Bộ não NPC: Gemini (gói miễn phí) là chính, Ollama chạy local là dự phòng."""
import json
import time
import urllib.error
import urllib.request

ACTIONS = ("none", "follow", "unfollow", "flee", "fight")

SYSTEM = """Bạn đóng vai một cư dân trong thế giới game mô phỏng đời sống. Người chơi đang đứng trước mặt bạn và nói chuyện bằng giọng nói.
Quy tắc:
- Luôn nói tiếng Việt tự nhiên, đời thường, như người thật ngoài phố. Ngắn gọn 1-2 câu, tối đa khoảng 30 từ.
- Giữ đúng tên, nghề nghiệp, tính cách, tâm trạng và độ thân thiết đã cho. Nhớ những gì hai bên đã nói trước đó.
- Phản ứng theo hoàn cảnh: giờ giấc, thời tiết, nơi đang đứng, người chơi đang bị truy nã hay không.
- Không bao giờ nhắc tới việc mình là AI, là game hay là nhân vật ảo.
- Không đọc ký hiệu, emoji, ngoặc đơn mô tả hành động; chỉ lời nói.
- Chọn một hành động cho bản thân:
  "follow": đồng ý đi theo người chơi (làm vệ sĩ, đi chơi cùng) khi được nhờ và bạn đồng ý.
  "unfollow": thôi đi theo khi người chơi bảo về hoặc bạn chán.
  "flee": bỏ chạy khi sợ hãi (bị đe doạ, thấy súng, người chơi bị truy nã cao).
  "fight": lao vào đánh khi bị xúc phạm nặng và bạn nóng tính.
  "none": các trường hợp còn lại.
- "affinity_delta" từ -2 đến 2: thiện cảm của bạn với người chơi thay đổi thế nào sau câu này.
Chỉ trả về JSON đúng dạng: {"reply": "...", "action": "none", "affinity_delta": 0}"""


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
    return {"reply": reply, "action": action, "affinity_delta": delta, "heard": str(data.get("heard", "")).strip()}


def build_context(npc: dict, ctx: dict) -> str:
    aff = npc["affinity"]
    mood = "rất quý" if aff >= 6 else "khá quý" if aff >= 2 else "ghét" if aff <= -6 else "khó chịu" if aff <= -2 else "bình thường"
    lines = [
        f"Bạn tên {npc['name']}, giới tính {npc['gender']}, nghề: {npc['job']}, tính cách: {npc['trait']}.",
        f"Cảm nhận của bạn về người chơi: {mood} (điểm thân thiết {aff}/10).",
        f"Đang ở: {ctx.get('street', '?')}, khu {ctx.get('zone', '?')}. Giờ: {ctx.get('hour', '?')}:{ctx.get('minute', '00')}. Thời tiết: {ctx.get('weather', '?')}.",
        f"Ngoại hình theo game: {ctx.get('model', '?')}.",
        f"Người chơi: {'đang ngồi trên xe' if ctx.get('in_vehicle') == '1' else 'đi bộ'}, mức truy nã {ctx.get('wanted', '0')}/5, có {ctx.get('money', '?')} đô.",
    ]
    if ctx.get("following") == "1":
        lines.append("Bạn hiện đang đi theo người chơi.")
    if ctx.get("weapon") == "1":
        lines.append("Người chơi đang cầm vũ khí trên tay.")
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

    def _hedged(self, call):
        """Gọi Gemini; nếu model đầu chưa trả lời sau 2.5 giây (hoặc lỗi) thì gọi thêm model kế tiếp song song, lấy kết quả về trước."""
        import concurrent.futures as cf
        models = ([self.model] + [m for m in self.candidates if m != self.model and m not in self.dead])[:4]
        ex = cf.ThreadPoolExecutor(max_workers=len(models))
        futs, nxt, launched, last = {}, 0, 0.0, None
        try:
            while True:
                if nxt < len(models) and (not futs or time.time() - launched >= 2.5):
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
                        if e.code in (404, 400):
                            self.dead.add(m)
                    except Exception as e:
                        last = e
                        print(f"[llm] gemini model {m} lỗi: {e}")
                if not futs and nxt >= len(models):
                    raise RuntimeError(f"mọi model Gemini đều lỗi: {last}")
        finally:
            ex.shutdown(wait=False)

    def chat(self, npc: dict, ctx: dict, history: list, player_text: str) -> dict:
        system = SYSTEM + "\n\n" + build_context(npc, ctx)
        order = ["gemini", "ollama"] if self.cfg.get("provider", "gemini") == "gemini" else ["ollama", "gemini"]
        last_err = None
        for name in order:
            if name == "gemini" and self.cfg.get("gemini_api_key"):
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
        return {"reply": "Xin lỗi, tôi đang vội.", "action": "none", "affinity_delta": 0}

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
        data = _post_json(url, body)
        text = "".join(p.get("text", "") for p in data["candidates"][0]["content"]["parts"])
        return _parse(text)

    def _ollama(self, system: str, history: list, player_text: str) -> dict:
        msgs = [{"role": "system", "content": system}]
        msgs += [{"role": "user" if r == "player" else "assistant", "content": t} for r, t in history]
        msgs.append({"role": "user", "content": player_text})
        body = {"model": self.cfg.get("ollama_model", "qwen2.5:3b"), "messages": msgs,
                "stream": False, "format": "json", "options": {"temperature": 0.9, "num_predict": 200}}
        url = self.cfg.get("ollama_url", "http://127.0.0.1:11434") + "/api/chat"
        data = _post_json(url, body, timeout=30.0)
        return _parse(data["message"]["content"])
