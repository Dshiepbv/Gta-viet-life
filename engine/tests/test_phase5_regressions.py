"""Batch B and C regressions: one owner for relationships, building tiers in SQLite, killed processes."""
import os
import sqlite3
import subprocess
import sys
import tempfile
import textwrap
import time
import unittest
from pathlib import Path

from engine.life_simulation import LifeSimulationEngine, NPCProfile, SQLiteLifeStore
from engine.society import SocietySimulation

ROOT = Path(__file__).resolve().parents[2]
MAPS = {"gta5.shvdn": {"entrance": "d", "interior": "i"}}


def society_for(engine):
    return SocietySimulation(engine.store.connection, engine.store._lock, lambda: engine.simulation_minute, engine)


class RelationshipOwnerTests(unittest.TestCase):
    def test_relationship_strength_follows_the_npc_state(self):
        engine = LifeSimulationEngine(SQLiteLifeStore())
        society = society_for(engine)
        for npc in ("a", "b"):
            engine.register_npc(NPCProfile(npc, npc, "park", "worker", schedule=()))
        society.set_social_relationship("a", "b", "friend", 10)
        engine.npcs["a"].affinity["b"] = 42.0   # autonomous social life moves it later
        rows = society.snapshot()["relationships"]
        self.assertEqual([(r["actor_id"], r["relation_type"], r["strength"]) for r in rows], [("a", "friend", 42.0)])
        engine.store.close()


class BuildingAttributeTests(unittest.TestCase):
    def setUp(self):
        self.engine = LifeSimulationEngine(SQLiteLifeStore())
        self.society = society_for(self.engine)

    def tearDown(self):
        self.engine.store.close()

    def register(self, attributes):
        return self.society.register_building("building.uis.1_2_3", "residential", "Nha 1_2_3", MAPS, ["enter"], attributes)

    def test_tier_is_stored_with_the_stable_building_id(self):
        self.register(None)
        self.register({"tier": 2, "purpose": "shop", "area_m2": 60, "zone": "DOWNT"})
        self.register(None)   # a later plain registration keeps the measurement
        building = self.society.snapshot()["buildings"][0]
        self.assertEqual(building["attributes"], {"area_m2": 60, "purpose": "shop", "tier": 2, "zone": "DOWNT"})

    def test_invalid_attributes_are_refused(self):
        for bad in ({"tier": 5}, {"tier": True}, {"purpose": "castle"}, {"x" * 40: 1}, {"note": "y" * 80}, "tier=2"):
            with self.assertRaises(ValueError):
                self.register(bad)


WRITER = textwrap.dedent("""
    import sys
    sys.path.insert(0, sys.argv[1])
    from engine.life_simulation import LifeSimulationEngine, NPCProfile, ScheduleBlock, SQLiteLifeStore
    from engine.society import SocietySimulation
    engine = LifeSimulationEngine(SQLiteLifeStore(sys.argv[2]))
    society = SocietySimulation(engine.store.connection, engine.store._lock, lambda: engine.simulation_minute, engine)
    for n in range(40):
        try:
            engine.register_npc(NPCProfile("n%d" % n, "N", "home", "worker", schedule=(ScheduleBlock(0, 1440, "work", "shop"),)))
        except ValueError:
            pass
    for account in ("a", "b"):
        society.ensure_account(account, "owner", 1000000)
    print("ready", flush=True)
    step = 0
    while True:
        step += 1
        engine.advance(5, "tick-%s-%d" % (sys.argv[3], step))
        society.post_transaction("move-%s-%d" % (sys.argv[3], step), "stress", "move",
                                 [{"account_id": "a", "amount": -1}, {"account_id": "b", "amount": 1}])
""")


class KilledProcessTests(unittest.TestCase):
    def test_killing_the_host_process_while_it_writes_never_corrupts_the_database(self):
        with tempfile.TemporaryDirectory() as folder:
            script = Path(folder) / "writer.py"
            script.write_text(WRITER, encoding="utf-8")
            path = Path(folder) / "life.sqlite3"
            minutes = []
            for run in range(3):
                writer = subprocess.Popen([sys.executable, "-B", str(script), str(ROOT), str(path), str(run)],
                                          stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
                self.assertEqual(writer.stdout.readline().strip(), "ready", writer.stderr.read() if writer.poll() else "")
                time.sleep(0.8 + run * 0.3)
                writer.kill()   # no clean shutdown, like a power cut for the process
                writer.wait(10)
                writer.stdout.close(); writer.stderr.close()
                check = sqlite3.connect(str(path))
                self.assertEqual(check.execute("PRAGMA integrity_check").fetchone()[0], "ok")
                check.close()
                engine = LifeSimulationEngine(SQLiteLifeStore(path))
                society = society_for(engine)
                self.assertEqual(society.verify_ledger(), [])
                self.assertEqual(len(engine.npcs), 40)
                balances = society.account("a")["balance"] + society.account("b")["balance"]
                self.assertEqual(balances, 2000000)
                minutes.append(engine.simulation_minute)
                engine.store.close()
            self.assertTrue(minutes[0] > 0 and minutes == sorted(minutes), minutes)


if __name__ == "__main__":
    unittest.main()
