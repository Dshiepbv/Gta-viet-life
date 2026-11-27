import ast
import time
import unittest
from pathlib import Path

from engine.life_simulation import LifeSimulationEngine, NPCProfile, ScheduleBlock


ROOT = Path(__file__).resolve().parents[1]


class EngineBoundaryTests(unittest.TestCase):
    def test_authoritative_engine_has_no_game_sdk_imports(self):
        forbidden = {"gta", "scripthook", "shvdn", "godot", "unity", "unreal", "rage"}
        for source in ROOT.glob("*.py"):
            tree = ast.parse(source.read_text(encoding="utf-8"), filename=str(source))
            for node in ast.walk(tree):
                if isinstance(node, ast.Import):
                    names = [alias.name.split(".")[0].lower() for alias in node.names]
                elif isinstance(node, ast.ImportFrom):
                    names = [(node.module or "").split(".")[0].lower()]
                else:
                    continue
                self.assertFalse(forbidden.intersection(names), f"{source.name} imports a game SDK: {names}")

    def test_population_load_is_bounded_and_persistent(self):
        engine = LifeSimulationEngine()
        for i in range(250):
            profile = NPCProfile(
                npc_id=f"npc.load.{i:04d}", display_name=f"Resident {i}",
                home_location_id=f"home.{i:04d}", occupation="worker",
                life_goal=("career", "health", "social")[i % 3],
                schedule=(
                    ScheduleBlock(0, 360, "work", f"work.{i:04d}"),
                    ScheduleBlock(360, 1440, "free_time", f"home.{i:04d}"),
                ),
            )
            engine.register_npc(profile)
        started = time.perf_counter()
        result = engine.advance(360)
        elapsed = time.perf_counter() - started
        self.assertEqual(result["npc_count"], 250)
        self.assertEqual(engine.snapshot()["npc_count"], 250)
        self.assertEqual(engine.simulation_minute, 360)
        print(f"M0 load baseline: 250 NPC x 360 sim minutes in {elapsed:.3f}s")

    def test_identical_inputs_produce_identical_authoritative_state(self):
        profile = NPCProfile(
            npc_id="npc.determinism", display_name="An", home_location_id="home",
            occupation="teacher", traits=("patient",), life_goal="health",
            schedule=(ScheduleBlock(0, 1440, "work", "school"),),
        )
        snapshots = []
        for _ in range(2):
            engine = LifeSimulationEngine()
            engine.register_npc(profile)
            engine.advance(240)
            snapshot = engine.snapshot()
            snapshots.append(snapshot["npcs"])
            engine.store.close()
        self.assertEqual(snapshots[0], snapshots[1])


if __name__ == "__main__":
    unittest.main()
