"""The GTA wallet owns cash (G2) and periodic replay receipts do not grow without bound."""
import tempfile
import time
import unittest
from pathlib import Path

from engine.life_simulation import LifeSimulationEngine, NPCProfile, SQLiteLifeStore
from engine.society import SocietySimulation


def society_for(engine):
    return SocietySimulation(engine.store.connection, engine.store._lock,
                             lambda: engine.simulation_minute, engine)


class WalletReconcileTests(unittest.TestCase):
    def setUp(self):
        self.engine = LifeSimulationEngine(SQLiteLifeStore())
        self.society = society_for(self.engine)

    def tearDown(self):
        self.engine.store.close()

    def balance(self, account="legacy:player_cash"):
        return self.engine.store.connection.execute(
            "SELECT balance FROM world_accounts WHERE account_id=?", (account,)).fetchone()[0]

    def send(self, sequence, kind, data):
        return self.society.ingest_legacy_events("gta5.shvdn", [
            {"sequence": sequence, "event_type": kind, "data": data}])

    def observe(self, sequence, balance):
        return self.send(sequence, "wallet.observed", "balance=%d;char=SP0" % balance)

    def test_ledger_follows_the_wallet_with_a_reasoned_entry(self):
        self.society.ensure_account("legacy:player_cash", "player.main", 1500)
        self.send(1, "ledger.income", "amount=500;cat=wage")
        self.assertEqual(self.balance(), 2000, "baseline already included the income")
        self.observe(2, 1500)
        self.assertEqual(self.balance(), 1500)
        row = self.engine.store.connection.execute(
            "SELECT source,description FROM world_transactions WHERE transaction_id='reconcile:gta5.shvdn:2'").fetchone()
        self.assertEqual(tuple(row), ("reconcile", "wallet observed SP0"))

    def test_loading_an_older_save_only_adjusts_cash_and_replay_is_harmless(self):
        self.society.create_account("bank", "player.main", 900)
        self.observe(1, 4000)
        self.observe(2, 100)
        self.observe(2, 100)
        self.assertEqual((self.balance(), self.balance("bank")), (100, 900))
        total = self.engine.store.connection.execute("SELECT SUM(amount) FROM world_ledger_entries").fetchone()[0]
        self.assertEqual(total, 0)
        self.assertEqual(self.engine.simulation_minute, 0, "reconciling must not re-simulate")

    def test_invalid_observation_is_rejected_alone(self):
        self.observe(1, 300)
        result = self.send(2, "wallet.observed", "balance=-5;char=SP0")
        self.assertEqual(len(result["rejected"]), 1)
        self.assertEqual(self.balance(), 300)


class PruningTests(unittest.TestCase):
    def count(self, engine, sql):
        return engine.store.connection.execute(sql).fetchone()[0]

    def test_expired_observation_receipts_are_removed_and_durable_ones_kept(self):
        engine = LifeSimulationEngine(SQLiteLifeStore())
        engine.register_npc(NPCProfile("player.main", "P", "home", "citizen", actor_kind="player"))
        engine.observe_player_state("player.main", {"hunger": 10}, "old")
        engine.record_player_work("player.main", 60, "shift")
        with engine.store.connection:
            engine.store.connection.execute("UPDATE request_dedup SET created_at=datetime('now','-2 hours')")
        engine.observe_player_state("player.main", {"hunger": 11}, "fresh")
        self.assertEqual(engine.store.prune_volatile_requests(), 1)
        keys = {r[0] for r in engine.store.connection.execute("SELECT request_id FROM request_dedup")}
        self.assertEqual(keys, {"player_observation:fresh", "player_work:shift"})
        engine.record_player_work("player.main", 60, "shift")
        self.assertEqual(engine.npcs["player.main"].work_minutes, 60)
        engine.store.close()

    def test_receipts_stay_bounded_while_observations_keep_arriving(self):
        engine = LifeSimulationEngine(SQLiteLifeStore())
        engine.register_npc(NPCProfile("player.main", "P", "home", "citizen", actor_kind="player"))
        for index in range(650):
            engine.observe_player_state("player.main", {"hunger": 10}, "r%d" % index)
            # Age every receipt so the periodic prune has something to remove.
            if index % 100 == 99:
                with engine.store.connection:
                    engine.store.connection.execute("UPDATE request_dedup SET created_at=datetime('now','-2 hours')")
        self.assertLess(self.count(engine, "SELECT COUNT(*) FROM request_dedup"), 250)
        engine.store.close()

    def test_old_business_sync_history_is_removed_on_an_upgraded_database(self):
        with tempfile.TemporaryDirectory() as folder:
            engine = LifeSimulationEngine(SQLiteLifeStore(Path(folder) / "life.sqlite3"))
            society = society_for(engine)
            society.register_building("b", "retail_business", "Shop", {"gta5.shvdn": {"entrance": "e", "interior": "i"}}, ["enter"])
            society.ensure_account("owner", "player.main", 0)
            society.ensure_account("till", "biz", 0)
            society.create_property("p", "b", "owner", 100, 10)
            society.create_business("biz", "p", "owner", "till", "food")
            society.sync_business_state("s1", "biz", "gta5.shvdn", 50, {"stock": 1}, True)
            society.interact("keep", "player.main", "building", "b", "enter", "gta5.shvdn")
            self.assertEqual(society.prune_sync_history(), 0, "fresh receipts are kept")
            self.assertEqual(society.prune_sync_history(time.time() + 7200), 1)
            self.assertEqual(self.count(engine, "SELECT COUNT(*) FROM world_request_dedup"), 1)
            self.assertEqual(self.count(engine, "SELECT COUNT(*) FROM world_event_journal "
                                                "WHERE event_type='business.state_synchronized'"), 0)
            engine.store.close()


if __name__ == "__main__":
    unittest.main()
