import tempfile
import unittest
from pathlib import Path

from engine.life_simulation import (
    LifeSimulationEngine, NPCProfile, ScheduleBlock, SQLiteLifeStore,
)


def profile():
    return NPCProfile(
        npc_id="npc.minh-an", display_name="Minh An", home_location_id="home.001",
        occupation="barista", traits=("friendly", "disciplined"), life_goal="career",
        schedule=(
            ScheduleBlock(0, 360, "sleep", "home.001"),
            ScheduleBlock(360, 480, "commute", "road.001"),
            ScheduleBlock(480, 1020, "work", "cafe.001"),
            ScheduleBlock(1020, 1320, "leisure", "park.001"),
            ScheduleBlock(1320, 1440, "sleep", "home.001"),
        ),
    )


class LifeSimulationTests(unittest.TestCase):
    def test_npc_follows_schedule_and_needs_change(self):
        engine = LifeSimulationEngine()
        npc = engine.register_npc(profile())
        before = npc.hunger
        result = engine.advance(500)
        state = engine.npcs["npc.minh-an"]
        self.assertEqual(result["simulation_minute"], 500)
        self.assertEqual(state.current_activity, "work")
        self.assertEqual(state.location_id, "cafe.001")
        self.assertGreater(state.hunger, before)
        self.assertGreater(state.goal_progress, 0)
        self.assertGreater(len(result["events"]), 0)

    def test_talk_changes_relationship_and_persists_memory(self):
        engine = LifeSimulationEngine()
        engine.register_npc(profile())
        result = engine.interact("npc.minh-an", "player.main", "talk")
        self.assertEqual(result["affinity"], 2.0)
        self.assertTrue(result["memory_recorded"])
        self.assertEqual(engine.snapshot()["npcs"][0]["memory_count"], 1)

    def test_save_reload_preserves_clock_needs_and_memory(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "life.sqlite3"
            first = LifeSimulationEngine(SQLiteLifeStore(path))
            first.register_npc(profile())
            first.advance(600)
            first.interact("npc.minh-an", "player.main")
            before = first.snapshot()
            second = LifeSimulationEngine(SQLiteLifeStore(path))
            try:
                after = second.snapshot()
                self.assertEqual(after["simulation_minute"], before["simulation_minute"])
                self.assertEqual(after["npcs"][0]["activity"], before["npcs"][0]["activity"])
                self.assertEqual(after["npcs"][0]["memory_count"], 1)
                self.assertEqual(after["npcs"][0]["relationships"], {"player.main": 2.0})
                self.assertGreater(after["npcs"][0]["goal_progress"], 0)
            finally:
                second.store.close()
                first.store.close()

    def test_rejects_overlapping_schedules_and_bad_advance(self):
        with self.assertRaises(ValueError):
            NPCProfile("x", "X", "home", "worker", schedule=(
                ScheduleBlock(0, 600, "sleep", "home"),
                ScheduleBlock(500, 700, "work", "work"),
            ))
        engine = LifeSimulationEngine()
        with self.assertRaises(ValueError):
            engine.advance(-1)
        with self.assertRaises(ValueError):
            engine.advance(1.5)

    def test_need_driven_override(self):
        engine = LifeSimulationEngine()
        npc = engine.register_npc(profile())
        npc.hunger = 95
        engine.advance(1)
        self.assertEqual(npc.current_activity, "eat")


if __name__ == "__main__":
    unittest.main()
