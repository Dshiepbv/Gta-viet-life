"""M4: the game keeps playing while the Engine is off, sends later, and then follows the ledger."""
import unittest

from engine.life_simulation import LifeSimulationEngine, SQLiteLifeStore
from engine.society import SocietySimulation

GTA = "gta5.shvdn:one"


class BankOwnershipTests(unittest.TestCase):
    def setUp(self):
        self.engine = LifeSimulationEngine(SQLiteLifeStore())
        self.society = SocietySimulation(self.engine.store.connection, self.engine.store._lock,
                                         lambda: self.engine.simulation_minute, self.engine)

    def tearDown(self):
        self.engine.store.close()

    def send(self, *events, source=GTA):
        return self.society.ingest_legacy_events(source, [
            {"sequence": s, "event_type": kind, "data": data} for s, kind, data in events])

    def state(self, source=GTA):
        return self.society.player_ledger(source)

    def test_nothing_is_owned_before_the_first_takeover(self):
        self.send((1, "ledger.income", "amount=900;acct=bank"))
        self.assertEqual(self.state(), {"owned": False, "bank": 900, "debt": 0, "applied_sequence": 1})

    def test_takeover_trusts_the_game_once(self):
        self.send((1, "ledger.income", "amount=900;acct=bank"))
        self.send((2, "bank.takeover", "balance=1500;debt=200"))
        self.assertEqual(self.state(), {"owned": True, "bank": 1500, "debt": 200, "applied_sequence": 2})
        self.send((3, "bank.takeover", "balance=7;debt=0"))
        self.assertEqual((self.state()["bank"], self.state()["debt"]), (1500, 200))
        self.assertEqual(self.society.verify_ledger(), [])

    def test_changes_made_while_offline_arrive_later_exactly_once(self):
        self.send((1, "bank.takeover", "balance=1000;debt=0"))
        offline = ((2, "ledger.expense", "amount=300;acct=bank"),
                   (3, "ledger.debt", "amount=500"),
                   (4, "ledger.income", "amount=50;acct=bank"),
                   (5, "ledger.debt", "amount=-120"))
        self.send(*offline)
        self.send(*offline)
        self.assertEqual(self.state(), {"owned": True, "bank": 750, "debt": 380, "applied_sequence": 5})
        self.assertEqual(self.society.verify_ledger(), [])

    def test_a_late_opening_balance_does_not_move_an_owned_account(self):
        self.send((4, "bank.takeover", "balance=1000;debt=0"))
        self.society.ensure_account("legacy:player_bank", "player.main", 9999,
                                    as_of_source=GTA, as_of_sequence=9)
        self.assertEqual(self.state()["bank"], 1000)
        self.send((5, "ledger.income", "amount=1;acct=bank"))
        self.assertEqual(self.state()["bank"], 1001)

    def test_a_recreated_game_counter_is_taken_over_again(self):
        self.send((9, "bank.takeover", "balance=1000;debt=0"))
        other = "gta5.shvdn:two"
        self.assertFalse(self.state(other)["owned"])
        self.assertEqual(self.state(other)["applied_sequence"], 0)
        self.send((1, "bank.takeover", "balance=400;debt=10"), source=other)
        self.assertEqual((self.state(other)["owned"], self.state(other)["bank"]), (True, 400))
        self.assertFalse(self.state()["owned"], "only the newest counter owns the account")

    def test_invalid_takeover_is_rejected_alone(self):
        result = self.send((1, "bank.takeover", "balance=-1;debt=0"), (2, "ledger.debt", "amount=abc"))
        self.assertEqual(len(result["rejected"]), 2)
        self.assertFalse(self.state()["owned"])


if __name__ == "__main__":
    unittest.main()
