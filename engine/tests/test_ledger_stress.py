"""Brief V4, batch 3: reconcile 1,000 random transactions with zero drift."""
import random
import tempfile
import unittest
from pathlib import Path

from engine.life_simulation import LifeSimulationEngine, SQLiteLifeStore
from engine.society import SocietySimulation

ACCOUNTS = ["acct%d" % n for n in range(6)]
START = 10_000


class LedgerStressTests(unittest.TestCase):
    def test_thousand_random_transactions_replays_and_restart_leave_no_drift(self):
        random_source = random.Random(20261128)
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "life.sqlite3"
            engine = LifeSimulationEngine(SQLiteLifeStore(path))
            society = SocietySimulation(engine.store.connection, engine.store._lock, lambda: 0, engine)
            for account in ACCOUNTS:
                society.create_account(account, "owner", START)
            expected = dict.fromkeys(ACCOUNTS, START)
            posted = []
            refused = replayed = conflicted = 0
            for number in range(1000):
                payer, payee = random_source.sample(ACCOUNTS, 2)
                amount = random_source.randint(1, 4000)
                entries = [{"account_id": payer, "amount": -amount}, {"account_id": payee, "amount": amount}]
                transaction = "stress-%d" % number
                if expected[payer] < amount:
                    with self.assertRaisesRegex(ValueError, "insufficient_funds"):
                        society.post_transaction(transaction, "stress", "move", entries)
                    refused += 1
                    continue
                self.assertFalse(society.post_transaction(transaction, "stress", "move", entries)["duplicate"])
                expected[payer] -= amount
                expected[payee] += amount
                posted.append((transaction, entries))
                if number % 7 == 0:       # the sender retries an earlier transaction
                    old, old_entries = random_source.choice(posted)
                    self.assertTrue(society.post_transaction(old, "stress", "move", old_entries)["duplicate"])
                    replayed += 1
                if number % 11 == 0:      # same id, different content
                    old, old_entries = random_source.choice(posted)
                    changed = [dict(e, amount=e["amount"] * 2) for e in old_entries]
                    with self.assertRaisesRegex(ValueError, "transaction_id_conflict"):
                        society.post_transaction(old, "stress", "move", changed)
                    conflicted += 1
                if number == 500:         # restart in the middle of the run
                    engine.store.close()
                    engine = LifeSimulationEngine(SQLiteLifeStore(path))
                    society = SocietySimulation(engine.store.connection, engine.store._lock, lambda: 0, engine)
            balances = {account: society.account(account)["balance"] for account in ACCOUNTS}
            self.assertEqual(balances, expected)
            self.assertEqual(sum(balances.values()), START * len(ACCOUNTS))
            self.assertEqual(society.verify_ledger(), [])
            # Control: the scenario really exercised every path, otherwise zero drift proves nothing.
            self.assertGreater(min(len(posted), refused, replayed, conflicted), 20)
            engine.store.close()


if __name__ == "__main__":
    unittest.main()
