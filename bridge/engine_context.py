"""Safe formatter for authoritative, bounded Life Engine NPC context."""
import json

# The Engine bounds the number of facts and memories, but 128 facts of 500 characters is a
# legitimate context far beyond the prompt budget; the prompt is trimmed, not discarded.
MAX_RAW = 200_000
MAX_PROMPT = 6000


def canonical_affinity(raw: str):
    """Affinity the Life Engine holds for this NPC, or None when the NPC is not an engine actor."""
    if not raw or not isinstance(raw, str) or len(raw) > MAX_RAW:
        return None
    try:
        data = json.loads(raw)
    except (TypeError, ValueError):
        return None
    relation = data.get("relationship") if isinstance(data, dict) else None
    value = relation.get("affinity") if isinstance(relation, dict) else None
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    return max(-100.0, min(100.0, float(value)))


def engine_context_prompt(raw: str) -> str:
    """Format canonical Host facts for the existing cognition prompt; never invent facts."""
    if not raw or not isinstance(raw, str) or len(raw) > MAX_RAW:
        return ""
    try:
        data = json.loads(raw)
    except (TypeError, ValueError):
        return ""
    if not isinstance(data, dict):
        return ""
    identity = data.get("identity") if isinstance(data.get("identity"), dict) else {}
    state = data.get("current_state") if isinstance(data.get("current_state"), dict) else {}
    needs = state.get("needs") if isinstance(state.get("needs"), dict) else {}
    relation = data.get("relationship") if isinstance(data.get("relationship"), dict) else {}
    lines = ["DỮ LIỆU CANONICAL TỪ LIFE ENGINE (ưu tiên dữ liệu này; không tự suy diễn ngoài phạm vi đã biết):"]
    for label, key in (("Tên", "name"), ("Tuổi", "age_years"), ("Nghề nghiệp", "occupation"),
                       ("Hộ gia đình", "household_id"), ("Tiểu sử", "biography"),
                       ("Mục tiêu sống", "life_goal")):
        if identity.get(key) not in (None, ""):
            lines.append(f"- {label}: {str(identity[key])[:300]}")
    if state.get("activity"):
        lines.append(f"- Hoạt động hiện tại: {str(state['activity'])[:80]}")
    if state.get("location_id"):
        lines.append(f"- Mã địa điểm: {str(state['location_id'])[:128]}")
    if needs:
        lines.append("- Nhu cầu (0–100): " + ", ".join(f"{k}={v}" for k, v in list(needs.items())[:8]))
    if relation:
        lines.append(f"- Thiện cảm với người chơi: {relation.get('affinity', 0)}")
    facts = data.get("known_facts", [])
    if isinstance(facts, list) and facts:
        lines.append("- Điều NPC thực sự biết:")
        for fact in facts[:8]:
            if isinstance(fact, dict) and isinstance(fact.get("fact"), str):
                lines.append(f"  + {fact['fact'][:300]} [nguồn: {str(fact.get('source', 'không rõ'))[:80]}]")
    memories = data.get("recent_memories", [])
    if isinstance(memories, list) and memories:
        lines.append("- Ký ức gần đây với người chơi:")
        for memory in memories[-6:]:
            if isinstance(memory, dict) and isinstance(memory.get("summary"), str):
                lines.append(f"  + {memory['summary'][:300]}")
    examples = data.get("dialogue_examples", [])
    if isinstance(examples, list) and examples:
        lines.append("- Mẫu câu đáp hợp ngữ cảnh (tham khảo giọng điệu, không chép nguyên):")
        for example in examples[:2]:
            if isinstance(example, dict) and isinstance(example.get("response"), str):
                lines.append(f"  + {example['response'][:200]}")
    boundary = data.get("knowledge_boundary")
    tail = "\nGIỚI HẠN TRI THỨC: " + boundary[:700] if isinstance(boundary, str) else ""
    # The knowledge boundary is a safety rule: trim facts and memories to keep room for it.
    return "\n".join(lines)[:MAX_PROMPT - len(tail)] + tail
