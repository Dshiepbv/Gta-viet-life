"""Without Groq/Gemini/Ollama the NPC answers from the Vietnam Data Pack instead of stalling."""
import contextlib
import io
import unittest

import llm


class OfflineReplyTests(unittest.TestCase):
    def setUp(self):
        self.pack = llm._pack()
        self.assertIsNotNone(self.pack, "Data Pack must load for the bridge")
        self.responses = {r["response"] for r in self.pack.dialogue}

    def test_food_order_gets_a_shop_answer(self):
        result = llm.offline_reply({"name": "Hải", "affinity": 0}, "Bán cho tôi một ổ bánh mì")
        self.assertEqual(result["intent"], "food_order")
        self.assertIn(result["reply"], self.responses)
        self.assertNotIn(result["reply"], llm.FALLBACK)

    def test_greeting_and_unknown_text_still_answer(self):
        for text in ("Chào anh", "Ừm"):
            result = llm.offline_reply({"name": "Lan", "affinity": 10}, text)
            self.assertIn(result["reply"], self.responses, text)

    def test_hostile_npc_does_not_answer_warmly(self):
        result = llm.offline_reply({"name": "Tú", "affinity": -80}, "Chào anh")
        conflict = {r["response"] for r in self.pack.dialogue if r["intent"] == "conflict"}
        self.assertIn(result["reply"], conflict)
        self.assertEqual(result["affinity_delta"], 0)

    def test_brain_without_any_provider_uses_the_pack(self):
        brain = llm.Brain({"provider": "groq", "ollama_enabled": False})
        with contextlib.redirect_stdout(io.StringIO()):   # CI console is cp1252; the bridge logs in Vietnamese
            result = brain.chat({"name": "Hải", "affinity": 0, "gender": "nam", "job": "bán hàng", "trait": "vui"}, {},
                                [], "Món này bao nhiêu tiền?")
        self.assertIn(result["reply"], self.responses)

    def test_control_empty_text_has_no_answer(self):
        self.assertIsNone(llm.offline_reply({"name": "Hải"}, "   "))


if __name__ == "__main__":
    unittest.main()
