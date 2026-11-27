import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
GODOT = ROOT / "adapters" / "godot"


class GodotAdapterContractTests(unittest.TestCase):
    def test_project_entrypoints_exist(self):
        self.assertTrue((GODOT / "project.godot").is_file())
        self.assertTrue((GODOT / "scenes" / "main.tscn").is_file())
        self.assertTrue((GODOT / "scripts" / "godot_adapter.gd").is_file())
        self.assertTrue((GODOT / "scripts" / "resident.gd").is_file())

    def test_adapter_uses_the_canonical_host_protocol(self):
        source = (GODOT / "scripts" / "godot_adapter.gd").read_text(encoding="utf-8")
        for required in (
            'const PROTOCOL := "gvl-engine/1"',
            'const ADAPTER_ID := "godot4.reference"',
            '"/engine/v1/handshake"',
            '"/engine/v1/simulation/npcs"',
            '"/engine/v1/heartbeat"',
            '"/engine/v1/adapter/commands"',
            '"/engine/v1/adapter/commands/ack"',
            '"/engine/v1/simulation/interact"',
            "actor.set_activity",
            '"GVL_ENGINE_TOKEN"',
            '"destination_reached"',
            '"/engine/v1/world/buildings"',
            '"/engine/v1/world/objects"',
            '"/engine/v1/world/interact"',
            '"/engine/v1/world/commands"',
            '"/engine/v1/world/commands/ack"',
            '"space.transition"',
            '"interaction.use_object"',
            '"/engine/v1/world/buildings"',
        ):
            self.assertIn(required, source + (GODOT / "README.md").read_text(encoding="utf-8"))

    def test_adapter_does_not_duplicate_game_neutral_domain_rules(self):
        source = (GODOT / "scripts" / "godot_adapter.gd").read_text(encoding="utf-8").lower()
        self.assertNotIn("import gta", source)
        self.assertNotIn("scripthook", source)
        self.assertNotIn("hunger += ", source)
        self.assertNotIn("affinity += ", source)
        self.assertIn('"/engine/v1/simulation/interact"', source)
        self.assertIn('"/engine/v1/adapter/commands/ack"', source)


if __name__ == "__main__":
    unittest.main()
