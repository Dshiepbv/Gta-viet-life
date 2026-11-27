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

    def test_offscreen_socializing_builds_npc_to_npc_relationships_and_memories(self):
        engine = LifeSimulationEngine()
        for npc_id, name in (("npc.social.a", "An"), ("npc.social.b", "Binh")):
            engine.register_npc(NPCProfile(
                npc_id=npc_id, display_name=name, home_location_id="home.social",
                occupation="neighbor", household_id="household.shared",
                schedule=(ScheduleBlock(0, 1440, "socialize", "park.shared"),),
            ))
        engine.advance(15)
        a = engine.npcs["npc.social.a"]
        b = engine.npcs["npc.social.b"]
        self.assertEqual(a.affinity["npc.social.b"], 2.0)
        self.assertEqual(b.affinity["npc.social.a"], 2.0)
        self.assertEqual(a.memories[-1]["participant_id"], "npc.social.b")
        self.assertEqual(a.memories[-1]["source"], "autonomous_social_simulation")

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

    def test_dialogue_context_is_provenance_aware_and_persisted(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "context.sqlite3"
            rich = NPCProfile(
                npc_id="npc.context", display_name="Mai", home_location_id="home.context",
                occupation="nurse", traits=("careful",), life_goal="health",
                household_id="household.7", age_years=29, biography="Works at the local clinic.",
                skills={"medical": 72.0},
                knowledge=({"fact": "The clinic closes at 18:00.", "source": "npc_observation_001",
                            "confidence": 0.95},),
                schedule=(ScheduleBlock(0, 1440, "work", "clinic.context"),),
            )
            first = LifeSimulationEngine(SQLiteLifeStore(path))
            first.register_npc(rich)
            result = first.interact("npc.context", "player.1", message="I met you at the clinic yesterday.")
            context = result["response_context"]
            self.assertEqual(context["identity"]["age_years"], 29)
            self.assertEqual(context["identity"]["household_id"], "household.7")
            self.assertEqual(context["identity"]["skills"]["medical"], 72.0)
            self.assertEqual(context["known_facts"][0]["source"], "npc_observation_001")
            self.assertIn("Do not claim unlisted events", context["knowledge_boundary"])
            self.assertEqual(context["recent_memories"][-1]["summary"], "I met you at the clinic yesterday.")
            second = LifeSimulationEngine(SQLiteLifeStore(path))
            try:
                restored = second.dialogue_context("npc.context", "player.1")
                self.assertEqual(restored["identity"]["biography"], "Works at the local clinic.")
                self.assertEqual(restored["known_facts"], list(rich.knowledge))
                self.assertEqual(restored["recent_memories"][-1]["participant_id"], "player.1")
            finally:
                second.store.close()
                first.store.close()

    def test_rejects_unprovenanced_or_invalid_npc_knowledge(self):
        with self.assertRaises(ValueError):
            NPCProfile("npc.bad", "Bad", "home", "worker", knowledge=({"fact": "secret", "confidence": 2},))
        with self.assertRaises(ValueError):
            NPCProfile("npc.bad-age", "Bad", "home", "worker", age_years=-1)

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

    def test_schema_one_migrates_to_idempotency_schema_two(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "legacy.sqlite3"
            import sqlite3
            connection = sqlite3.connect(path)
            connection.execute("CREATE TABLE schema_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)")
            connection.execute("CREATE TABLE npc_state (npc_id TEXT PRIMARY KEY, profile_json TEXT NOT NULL, state_json TEXT NOT NULL, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP)")
            connection.execute("CREATE TABLE life_events (event_id TEXT PRIMARY KEY, sim_minute INTEGER NOT NULL, npc_id TEXT NOT NULL, event_type TEXT NOT NULL, payload_json TEXT NOT NULL)")
            connection.execute("INSERT INTO schema_meta(key,value) VALUES('schema_version','1')")
            connection.execute("INSERT INTO schema_meta(key,value) VALUES('simulation_minute','12')")
            connection.commit()
            connection.close()
            store = SQLiteLifeStore(path)
            try:
                self.assertEqual(store.get_wallclock_epoch(), None)
                self.assertIsNone(store.get_request_result("not-seen"))
            finally:
                store.close()

    def test_activity_transition_creates_durable_adapter_command(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "commands.sqlite3"
            first = LifeSimulationEngine(SQLiteLifeStore(path))
            first.register_npc(profile())
            first.advance(1)
            commands = first.pending_commands()
            self.assertEqual(len(commands), 1)
            command = commands[0]
            self.assertEqual(command["command_type"], "actor.set_activity")
            self.assertEqual(command["payload"]["activity"], "sleep")
            second = LifeSimulationEngine(SQLiteLifeStore(path))
            try:
                self.assertEqual(second.pending_commands()[0]["command_id"], command["command_id"])
                self.assertTrue(second.complete_command(command["command_id"], {"success": True, "execution": "accepted"}))
                self.assertEqual(second.pending_commands(), [])
                self.assertFalse(second.complete_command(command["command_id"], {"success": True}))
            finally:
                second.store.close()
                first.store.close()

    def test_pending_activity_commands_coalesce_to_latest_npc_state(self):
        with tempfile.TemporaryDirectory() as folder:
            store = SQLiteLifeStore(Path(folder) / "coalesce.sqlite3")
            engine = LifeSimulationEngine(store)
            p = NPCProfile(
                npc_id="npc.coalesce", display_name="Lan", home_location_id="home.coalesce",
                occupation="worker", schedule=(
                    ScheduleBlock(0, 1, "work", "work.coalesce"),
                    ScheduleBlock(1, 2, "leisure", "leisure.coalesce"),
                    ScheduleBlock(2, 3, "eat", "lunch.coalesce"),
                    ScheduleBlock(3, 5, "sleep", "home.coalesce"),
                )
            )
            engine.register_npc(p)
            engine.advance(4)
            pending = engine.pending_commands()
            self.assertEqual(len(pending), 1)
            self.assertEqual(pending[0]["payload"]["activity"], "sleep")
            statuses = store.connection.execute(
                "SELECT status,COUNT(*) AS n FROM command_outbox GROUP BY status"
            ).fetchall()
            counts = {row["status"]: row["n"] for row in statuses}
            self.assertEqual(counts.get("pending"), 1)
            self.assertEqual(counts.get("superseded"), 2)
            store.close()

    def test_command_ack_requires_success(self):
        engine = LifeSimulationEngine()
        with self.assertRaises(ValueError):
            engine.complete_command("unknown", {"success": False})

    def test_need_driven_override(self):
        engine = LifeSimulationEngine()
        npc = engine.register_npc(profile())
        npc.hunger = 95
        engine.advance(1)
        self.assertEqual(npc.current_activity, "eat")


if __name__ == "__main__":
    unittest.main()
