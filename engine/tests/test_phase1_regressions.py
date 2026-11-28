"""Regressions for audit items E2 (restart re-registration), E3 (dialogue vs. commands) and E13 (poison batch)."""
import tempfile
import unittest
from pathlib import Path

from engine.life_simulation import LifeSimulationEngine, NPCProfile, ScheduleBlock, SQLiteLifeStore
from engine.society import SocietySimulation


def resident(npc_id="res_1", **extra):
    home = npc_id + ".home"
    fields = dict(
        npc_id=npc_id, display_name="Lan " + npc_id, home_location_id=home, occupation="resident",
        life_goal="social", traits=("community_resident",),
        schedule=(ScheduleBlock(0, 360, "sleep", home),
                  ScheduleBlock(360, 1440, "work", npc_id + ".work")),
    )
    fields.update(extra)
    return NPCProfile(**fields)


class RestartRegistrationTests(unittest.TestCase):
    def reopen(self, folder, first_profile, second_profile):
        path = Path(folder) / "life.sqlite3"
        first = LifeSimulationEngine(SQLiteLifeStore(path))
        first.register_npc(first_profile)
        first.advance(30)
        first.store.close()
        second = LifeSimulationEngine(SQLiteLifeStore(path))
        try:
            return second.register_npc(second_profile)
        finally:
            second.store.close()

    def test_identical_profile_is_accepted_after_restart(self):
        with tempfile.TemporaryDirectory() as folder:
            state = self.reopen(folder, resident(), resident())
            self.assertEqual(state.profile.npc_id, "res_1")

    def test_rich_profile_is_accepted_after_restart(self):
        def rich():
            # Lists are what the HTTP layer and JSON storage can hand to the profile.
            return resident(traits=["friendly", "calm"], skills={"cooking": 10},
                            knowledge=[{"fact": "Lives near Burton", "source": "seed", "confidence": 1.0}],
                            household_id="h1", biography="Neighbour")
        with tempfile.TemporaryDirectory() as folder:
            state = self.reopen(folder, rich(), rich())
            self.assertEqual(state.profile.knowledge[0]["fact"], "Lives near Burton")

    def test_changed_identity_is_still_rejected_after_restart(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaisesRegex(ValueError, "npc_identity_conflict"):
                self.reopen(folder, resident(), resident(display_name="Someone else"))


class DialogueMemoryCommandTests(unittest.TestCase):
    def setUp(self):
        self.engine = LifeSimulationEngine(SQLiteLifeStore())
        self.engine.register_npc(resident("res_1"))
        self.engine.register_npc(resident("res_2"))
        self.engine.advance(5)

    def tearDown(self):
        self.engine.store.close()

    def pending_ids(self):
        return sorted(c["command_id"] for c in self.engine.pending_commands())

    def test_dialogue_memory_keeps_pending_commands_deliverable(self):
        before = self.pending_ids()
        self.assertEqual(len(before), 2)
        self.engine.record_dialogue_memory("res_1", "player.main", "Chao", "Chao ban", "dialogue-1")
        self.assertEqual(self.pending_ids(), before)
        self.assertTrue(self.engine.complete_command(before[0], {"success": True}))

    def test_dialogue_memory_after_a_reported_command_does_not_resurrect_it(self):
        first, second = self.pending_ids()
        self.engine.complete_command(first, {"success": True})
        self.engine.record_dialogue_memory("res_2", "player.main", "Chao", "Chao ban", "dialogue-2")
        self.assertEqual(self.pending_ids(), [second])

    def test_dialogue_memory_journals_only_its_own_events(self):
        count = lambda: self.engine.store.connection.execute("SELECT COUNT(*) FROM life_events").fetchone()[0]
        before = count()
        self.engine.record_dialogue_memory("res_1", "player.main", "Chao", "Chao ban", "dialogue-3")
        self.assertEqual(count(), before + 2)
        self.assertEqual({e["event_type"] for e in self.engine.events}, {"player_dialogue", "npc_dialogue"})


class PoisonEventBatchTests(unittest.TestCase):
    def setUp(self):
        self.engine = LifeSimulationEngine(SQLiteLifeStore())
        self.society = SocietySimulation(self.engine.store.connection, self.engine.store._lock,
                                         lambda: self.engine.simulation_minute, self.engine)

    def tearDown(self):
        self.engine.store.close()

    def balance(self):
        return self.engine.store.connection.execute(
            "SELECT balance FROM world_accounts WHERE account_id='legacy:player_cash'").fetchone()[0]

    def test_one_bad_event_does_not_block_the_rest_of_the_batch(self):
        events = [
            {"sequence": 1, "event_type": "ledger.income", "data": "amount=100;cat=wage"},
            {"sequence": 2, "event_type": "ledger.income", "data": "amount=50;cat="},
            {"sequence": 3, "event_type": "ledger.expense", "data": "amount=30;cat=food"},
        ]
        result = self.society.ingest_legacy_events("gta5.shvdn", events)
        self.assertEqual(result["accepted"], 2)
        self.assertEqual([r["sequence"] for r in result["rejected"]], [2])
        self.assertEqual(self.balance(), 70)
        journal = self.engine.store.connection.execute(
            "SELECT event_id FROM world_event_journal WHERE event_id LIKE 'gta5.shvdn:%' ORDER BY event_id").fetchall()
        self.assertEqual([row[0] for row in journal], ["gta5.shvdn:1", "gta5.shvdn:3"])

    def test_replaying_a_partly_rejected_batch_changes_nothing(self):
        events = [
            {"sequence": 1, "event_type": "ledger.income", "data": "amount=100;cat=wage"},
            {"sequence": -1, "event_type": "ledger.income", "data": "amount=5;cat=wage"},
        ]
        self.society.ingest_legacy_events("gta5.shvdn", events)
        again = self.society.ingest_legacy_events("gta5.shvdn", events)
        self.assertEqual((again["accepted"], again["duplicates"], len(again["rejected"])), (0, 1, 1))
        self.assertEqual(self.balance(), 100)


if __name__ == "__main__":
    unittest.main()
