import json
import unittest

from engine_context import engine_context_prompt


class EngineContextPromptTests(unittest.TestCase):
    def test_formats_only_canonical_bounded_context(self):
        raw = json.dumps({
            "identity": {"name": "Mai", "age_years": 29, "occupation": "nurse",
                         "biography": "Works at a clinic.", "life_goal": "health"},
            "current_state": {"activity": "work", "location_id": "clinic.1",
                              "needs": {"hunger": 70, "energy": 45}},
            "relationship": {"affinity": 18},
            "known_facts": [{"fact": "Clinic closes at 18:00.", "source": "observation.1"}],
            "recent_memories": [{"summary": "We met yesterday."}],
            "knowledge_boundary": "Do not claim unlisted events."
        }, ensure_ascii=False)
        prompt = engine_context_prompt(raw)
        self.assertIn("Mai", prompt)
        self.assertIn("Clinic closes at 18:00", prompt)
        self.assertIn("We met yesterday", prompt)
        self.assertIn("Do not claim unlisted events", prompt)
        self.assertIn("hunger=70", prompt)

    def test_invalid_or_oversized_context_is_ignored(self):
        self.assertEqual(engine_context_prompt("not-json"), "")
        self.assertEqual(engine_context_prompt("x" * 16001), "")
        self.assertEqual(engine_context_prompt(json.dumps(["not", "an", "object"])), "")


if __name__ == "__main__":
    unittest.main()
