"""Regressions for durability (E7, E10), the ledger (E4, E8) and the game clock (E6)."""
import logging
import sqlite3
import tempfile
import unittest
from pathlib import Path

from engine import host
from engine.life_simulation import LifeSimulationEngine, NPCProfile, ScheduleBlock, SQLiteLifeStore
from engine.society import SocietySimulation


def society_for(engine):
    return SocietySimulation(engine.store.connection, engine.store._lock,
                             lambda: engine.simulation_minute, engine)


class MigrationTests(unittest.TestCase):
    def test_database_left_half_migrated_by_an_older_build_still_opens(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "life.sqlite3"
            broken = sqlite3.connect(str(path))
            broken.execute("CREATE TABLE schema_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)")
            broken.execute("""CREATE TABLE npc_state (npc_id TEXT PRIMARY KEY, profile_json TEXT NOT NULL,
                state_json TEXT NOT NULL, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP)""")
            broken.commit()
            broken.close()
            store = SQLiteLifeStore(path)
            self.assertEqual(store.get_meta("schema_version"), str(SQLiteLifeStore.SCHEMA_VERSION))
            store.close()

    def test_interrupted_migration_leaves_no_partial_tables(self):
        class Interrupted(SQLiteLifeStore):
            def _connect(self, path):
                connection = super()._connect(path)

                def deny(action, name, *_):
                    if action == sqlite3.SQLITE_CREATE_TABLE and name == "command_outbox":
                        return sqlite3.SQLITE_DENY
                    return sqlite3.SQLITE_OK
                connection.set_authorizer(deny)
                return connection

        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "life.sqlite3"
            with self.assertRaises(sqlite3.DatabaseError):
                Interrupted(path)
            probe = sqlite3.connect(str(path))
            tables = [r[0] for r in probe.execute("SELECT name FROM sqlite_master WHERE type='table'")]
            probe.close()
            self.assertEqual(tables, [])
            SQLiteLifeStore(path).close()

    def test_upgrade_keeps_a_backup_and_enables_durability_pragmas(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "life.sqlite3"
            store = SQLiteLifeStore(path)
            store.set_meta("marker", "before-upgrade")
            store.set_meta("schema_version", "3")
            store.close()
            store = SQLiteLifeStore(path)
            pragma = lambda name: store.connection.execute("PRAGMA " + name).fetchone()[0]
            self.assertEqual(pragma("journal_mode"), "wal")
            self.assertEqual(pragma("foreign_keys"), 1)
            self.assertGreaterEqual(pragma("busy_timeout"), 5000)
            store.close()
            backup = sqlite3.connect(str(path) + ".v3.bak")
            self.assertEqual(backup.execute("SELECT value FROM schema_meta WHERE key='marker'").fetchone()[0],
                             "before-upgrade")
            backup.close()


class SingleProcessTests(unittest.TestCase):
    def test_second_host_on_the_same_data_folder_is_refused(self):
        with tempfile.TemporaryDirectory() as folder:
            first = host.DataDirLock(folder)
            with self.assertRaises(RuntimeError):
                host.DataDirLock(folder)
            first.release()
            host.DataDirLock(folder).release()

    def test_second_server_on_the_same_port_is_refused(self):
        handler = host.handler_for(host.State("t" * 40), logging.getLogger("test"))
        first = host.EngineServer(("127.0.0.1", 0), handler)
        try:
            with self.assertRaises(OSError):
                host.EngineServer(("127.0.0.1", first.server_address[1]), handler).server_close()
        finally:
            first.server_close()


class LedgerTests(unittest.TestCase):
    def setUp(self):
        self.engine = LifeSimulationEngine(SQLiteLifeStore())
        self.society = society_for(self.engine)
        self.society.create_account("a", "owner", 1000)
        self.society.create_account("b", "owner", 0)

    def tearDown(self):
        self.engine.store.close()

    def balance(self, account):
        return self.society.account(account)["balance"]

    def move(self, transaction_id, amount, source="test", target="b"):
        return self.society.post_transaction(transaction_id, source, "move", [
            {"account_id": "a", "amount": -amount}, {"account_id": target, "amount": amount}])

    def test_same_id_with_other_content_is_refused(self):
        self.move("t1", 100)
        self.assertTrue(self.move("t1", 100)["duplicate"])
        with self.assertRaisesRegex(ValueError, "transaction_id_conflict"):
            self.move("t1", 999)
        self.assertEqual(self.balance("a"), 900)

    def test_currencies_cannot_mix_and_each_opens_against_its_own_equity(self):
        self.society.create_account("dong", "owner", 5000, currency="VND")
        with self.assertRaisesRegex(ValueError, "currency_mismatch"):
            self.move("t2", 10, target="dong")
        mixed = self.engine.store.connection.execute("""SELECT COUNT(*) FROM (
            SELECT e.transaction_id FROM world_ledger_entries e JOIN world_accounts a USING(account_id)
            GROUP BY e.transaction_id HAVING COUNT(DISTINCT a.currency)>1)""").fetchone()[0]
        self.assertEqual(mixed, 0)

    def test_amounts_stay_integers_inside_the_declared_limit(self):
        with self.assertRaisesRegex(ValueError, "amount_out_of_range"):
            self.society.post_transaction("big", "test", "x", [
                {"account_id": "a", "amount": 2 ** 62}, {"account_id": "b", "amount": -(2 ** 62)}])
        with self.assertRaises(sqlite3.IntegrityError):
            with self.engine.store.connection:
                self.engine.store.connection.execute(
                    "UPDATE world_accounts SET balance=balance+9223372036854775807 WHERE account_id='a'")
        self.assertEqual(self.balance("a"), 1000)

    def test_engine_prefixes_cannot_be_claimed_by_callers(self):
        for reserved in ("payroll:x", "lease:x", "sale:x", "opening:x", "reconcile:x", "bsync:x",
                         "legacy-ledger:x"):
            with self.assertRaisesRegex(ValueError, "reserved_transaction_id"):
                self.move(reserved, 1)
        self.assertEqual(self.balance("a"), 1000)

    def shop(self):
        self.society.register_building("bld", "retail_business", "Shop",
                                       {"gta5.shvdn": {"entrance": "door"}}, ["enter"])
        self.society.create_account("till", "biz", 0)
        self.society.create_property("p", "bld", "b", 100, 10)
        self.society.create_business("biz", "p", "b", "till", "food")
        self.society.set_inventory("biz", {"stock": 2, "other": 2})

    def test_replaying_a_sale_that_emptied_the_shelf_succeeds_once(self):
        self.shop()
        self.assertFalse(self.society.sell_product("s1", "biz", "a", "stock", 2, 5)["duplicate"])
        self.assertTrue(self.society.sell_product("s1", "biz", "a", "stock", 2, 5)["duplicate"])
        with self.assertRaisesRegex(ValueError, "transaction_id_conflict"):
            self.society.sell_product("s1", "biz", "a", "other", 2, 5)
        self.assertEqual((self.balance("a"), self.balance("till")), (990, 10))

    def test_replaying_a_lease_returns_the_first_result(self):
        self.shop()
        self.society.lease_property("l1", "p", "a", 20, 60)
        self.assertTrue(self.society.lease_property("l1", "p", "a", 20, 60)["duplicate"])
        with self.assertRaisesRegex(ValueError, "transaction_id_conflict"):
            self.society.lease_property("l1", "p", "a", 30, 60)
        self.assertEqual(self.balance("a"), 980)

    def test_startup_check_reports_a_balance_that_does_not_match_its_entries(self):
        self.assertEqual(self.society.verify_ledger(), [])
        with self.engine.store.connection:
            self.engine.store.connection.execute("UPDATE world_accounts SET balance=7 WHERE account_id='b'")
        self.assertEqual(self.society.verify_ledger(), [{"account_id": "b", "balance": 7, "entries": 0}])


class OpeningBalanceTests(unittest.TestCase):
    def setUp(self):
        self.engine = LifeSimulationEngine(SQLiteLifeStore())
        self.society = society_for(self.engine)

    def tearDown(self):
        self.engine.store.close()

    def event(self, sequence, amount, account="cash"):
        return self.society.ingest_legacy_events("gta5.shvdn", [
            {"sequence": sequence, "event_type": "ledger.income",
             "data": "amount=%d;cat=wage;acct=%s" % (amount, account)}])

    def open(self, balance, as_of, account="legacy:player_bank"):
        return self.society.ensure_account(account, "player.main", balance,
                                           as_of_source="gta5.shvdn", as_of_sequence=as_of)

    def test_event_already_inside_the_opening_balance_is_not_added_again(self):
        self.open(1500, 4, "legacy:player_cash")
        self.event(4, 500)
        self.assertEqual(self.society.account("legacy:player_cash")["balance"], 1500)
        self.event(5, 100)
        self.assertEqual(self.society.account("legacy:player_cash")["balance"], 1600)

    def test_events_arriving_before_the_opening_balance_are_not_lost(self):
        self.event(3, 500, "bank")
        self.event(6, 40, "bank")
        self.assertEqual(self.open(1500, 4)["balance"], 1540)
        self.assertEqual(self.open(9999, 9)["balance"], 1540, "the opening entry is posted once")
        self.assertEqual(self.society.verify_ledger(), [])

    def test_events_from_a_newer_counter_do_not_break_the_old_sources_opening(self):
        self.society.ingest_legacy_events("gta5.shvdn:abc", [
            {"sequence": 1, "event_type": "ledger.income", "data": "amount=30;acct=bank"}])
        self.assertEqual(self.open(1000, 5)["balance"], 1000)
        self.assertEqual(self.society.verify_ledger(), [])

    def test_wallet_observation_beats_a_late_opening_balance(self):
        self.society.ingest_legacy_events("gta5.shvdn", [
            {"sequence": 2, "event_type": "wallet.observed", "data": "balance=700;char=SP0"}])
        self.assertEqual(self.open(1500, 1, "legacy:player_cash")["balance"], 700)


class GameClockTests(unittest.TestCase):
    def handshake(self, state, minute, clock_id="install-a", session="s"):
        state.handshake({"request_id": "h", "protocol": host.PROTOCOL, "session_id": session,
                         "world_minute": minute, "clock_id": clock_id,
                         "adapter": {"id": "gta5.shvdn", "version": "1"}})

    def worker(self):
        state = host.State("t" * 40)
        self.handshake(state, 600)
        state.simulation.register_npc(NPCProfile(
            "n", "N", "home", "worker", schedule=(ScheduleBlock(0, 1440, "work", "shop"),)))
        return state

    def test_loading_an_older_save_never_simulates_the_same_minutes_twice(self):
        state = self.worker()
        state.heartbeat("s", 720)
        reached = state.simulation.simulation_minute
        worked = state.simulation.npcs["n"].work_minutes
        state.heartbeat("s", 650)
        state.heartbeat("s", 720)
        self.assertEqual(state.simulation.simulation_minute, reached)
        self.assertEqual(state.simulation.npcs["n"].work_minutes, worked)
        state.heartbeat("s", 730)
        self.assertEqual(state.simulation.simulation_minute, reached + 10)
        self.assertEqual(state.simulation.store.get_meta("clock_epoch"), "1")

    def test_reconnecting_after_a_rewind_keeps_the_high_water_mark(self):
        state = self.worker()
        state.heartbeat("s", 720)
        reached = state.simulation.simulation_minute
        self.handshake(state, 100, session="s2")
        state.heartbeat("s2", 720)
        self.assertEqual(state.simulation.simulation_minute, reached)

    def test_a_large_jump_is_simulated_in_chunks_without_losing_the_rest(self):
        state = self.worker()
        start = state.simulation.simulation_minute
        target = 600 + 3 * 10080
        for _ in range(40):
            state.heartbeat("s", target)
        self.assertEqual(state.simulation.simulation_minute - start, target - 600)

    def test_a_new_adapter_clock_rebases_instead_of_freezing(self):
        state = self.worker()
        state.heartbeat("s", 5000)
        reached = state.simulation.simulation_minute
        self.handshake(state, 10, clock_id="install-b", session="s2")
        self.assertEqual(state.simulation.simulation_minute, reached)
        state.heartbeat("s2", 20)
        self.assertEqual(state.simulation.simulation_minute, reached + 10)


if __name__ == "__main__":
    unittest.main()
