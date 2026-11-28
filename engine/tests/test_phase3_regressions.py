"""Regressions for atomic effects (E9) and per-endpoint request receipts (E11)."""
import sqlite3
import threading
import unittest

from engine import host
from engine.life_simulation import LifeSimulationEngine, NPCProfile, SQLiteLifeStore
from engine.society import SocietySimulation


def deny(table):
    def authorize(action, name, *_):
        if action in (sqlite3.SQLITE_INSERT, sqlite3.SQLITE_UPDATE) and name == table:
            return sqlite3.SQLITE_DENY
        return sqlite3.SQLITE_OK
    return authorize


class Case(unittest.TestCase):
    def setUp(self):
        self.engine = LifeSimulationEngine(SQLiteLifeStore())
        self.society = SocietySimulation(self.engine.store.connection, self.engine.store._lock,
                                         lambda: self.engine.simulation_minute, self.engine)
        self.engine.register_npc(NPCProfile("player.main", "P", "home", "citizen", actor_kind="player"))

    def tearDown(self):
        self.engine.store.close()

    def break_writes(self, table):
        self.engine.store.connection.set_authorizer(deny(table))

    def repair(self):
        self.engine.store.connection.set_authorizer(lambda *_: sqlite3.SQLITE_OK)

    def bed(self):
        self.society.register_building("home", "residential", "Home", {"a": {"interior": "i"}}, ["enter"])
        self.society.register_object("bed", "home", "bed", {"a": "anchor"})
        return self.society.interact("use-bed", "player.main", "object", "bed", "use", "a")["command_id"]

    def status(self, command_id):
        return self.engine.store.connection.execute(
            "SELECT status FROM world_command_outbox WHERE command_id=?", (command_id,)).fetchone()[0]


class AtomicityTests(Case):
    def test_failed_save_leaves_memory_unchanged(self):
        self.break_writes("npc_state")
        with self.assertRaises(sqlite3.DatabaseError):
            self.engine.advance(30, "tick")
        with self.assertRaises(sqlite3.DatabaseError):
            self.engine.register_npc(NPCProfile("n", "N", "home", "worker"))
        self.repair()
        self.assertEqual(self.engine.simulation_minute, 0)
        self.assertNotIn("n", self.engine.npcs)
        self.assertEqual(self.engine.npcs["player.main"].age_minutes, 0)
        self.assertEqual(self.engine.advance(30, "tick")["simulation_minute"], 30)

    def test_command_is_not_acknowledged_unless_its_effect_is_saved(self):
        command = self.bed()
        before = self.engine.npcs["player.main"].energy
        self.engine.npcs["player.main"].energy = before = 20.0
        self.engine.store.save(0, [self.engine.npcs["player.main"]], [])
        self.break_writes("npc_state")
        with self.assertRaises(sqlite3.DatabaseError):
            self.society.acknowledge_command(command, {"success": True})
        self.repair()
        self.assertEqual(self.status(command), "pending")
        self.assertEqual(self.engine.npcs["player.main"].energy, before)
        self.assertTrue(self.society.acknowledge_command(command, {"success": True}))
        self.assertEqual(self.engine.npcs["player.main"].energy, before + 35.0)
        self.assertFalse(self.society.acknowledge_command(command, {"success": True}))

    def test_relationship_and_npc_state_are_written_together(self):
        for npc in ("a", "b"):
            self.engine.register_npc(NPCProfile(npc, npc, "home", "worker"))
        self.break_writes("npc_state")
        with self.assertRaises(sqlite3.DatabaseError):
            self.society.set_social_relationship("a", "b", "friend", 50)
        self.repair()
        self.assertEqual(self.society.snapshot()["relationships"], [])
        self.assertEqual(self.engine.npcs["a"].affinity, {})


class RequestScopeTests(Case):
    def test_one_request_id_used_on_two_endpoints_runs_both(self):
        self.society.register_building("shop", "retail_business", "Shop",
                                       {"a": {"interior": "i"}}, ["enter"])
        self.society.create_account("owner", "p", 0)
        self.society.create_account("till", "biz", 0)
        self.society.create_property("prop", "shop", "owner", 1, 1)
        self.society.create_business("biz", "prop", "owner", "till", "food")
        self.assertTrue(self.society.interact("same", "player.main", "building", "shop", "enter", "a")["accepted"])
        result = self.society.sync_business_state("same", "biz", "a", 40, {"stock": 1})
        self.assertTrue(result.get("synced"), result)
        self.assertEqual(self.society.account("till")["balance"], 40)
        self.assertTrue(self.society.sync_business_state("same", "biz", "a", 40, {"stock": 1})["synced"])


class AdvanceReplayTests(unittest.TestCase):
    def test_concurrent_duplicate_advance_applies_society_once(self):
        state = host.State("t" * 40)
        calls = []
        original = state.society.advance
        state.society.advance = lambda minutes: (calls.append(minutes), original(minutes))
        barrier = threading.Barrier(8)

        def run():
            barrier.wait()
            for index in range(20):
                state.advance(1, "request-%d" % index)

        threads = [threading.Thread(target=run) for _ in range(8)]
        for thread in threads:
            thread.start()
        for thread in threads:
            thread.join()
        self.assertEqual((len(calls), state.simulation.simulation_minute), (20, 20))
        state.simulation.store.close()


if __name__ == "__main__":
    unittest.main()
