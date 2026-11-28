import json
import unittest

from engine_context import canonical_affinity, engine_context_prompt


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

    def test_large_context_is_trimmed_and_keeps_the_knowledge_boundary(self):
        raw = json.dumps({
            "identity": {"name": "Mai", "biography": "b" * 2000},
            "known_facts": [{"fact": "f" * 500, "source": "s"} for _ in range(128)],
            "recent_memories": [{"summary": "m" * 500} for _ in range(10)],
            "knowledge_boundary": "Do not claim unlisted events." + "." * 650,
        })
        self.assertGreater(len(raw), 16000)
        prompt = engine_context_prompt(raw)
        self.assertIn("Mai", prompt)
        self.assertIn("Do not claim unlisted events", prompt)
        self.assertLessEqual(len(prompt), 6000)

    def test_engine_owns_the_affinity_of_its_actors(self):
        self.assertEqual(canonical_affinity(json.dumps({"relationship": {"affinity": 35}})), 35.0)
        self.assertEqual(canonical_affinity(json.dumps({"relationship": {"affinity": 900}})), 100.0)
        for raw in ("", "not-json", json.dumps({"relationship": {"affinity": True}}), json.dumps([1])):
            self.assertIsNone(canonical_affinity(raw))
        main = (__import__("pathlib").Path(__file__).with_name("main.py")).read_text(encoding="utf-8")
        self.assertIn('if result["affinity_delta"] and engine_affinity is None:', main)

    def test_invalid_or_oversized_context_is_ignored(self):
        self.assertEqual(engine_context_prompt("not-json"), "")
        self.assertEqual(engine_context_prompt("x" * 200001), "")
        self.assertEqual(engine_context_prompt(json.dumps(["not", "an", "object"])), "")


if __name__ == "__main__":
    unittest.main()
