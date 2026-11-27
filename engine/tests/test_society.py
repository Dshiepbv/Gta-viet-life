import unittest

from engine.life_simulation import LifeSimulationEngine, NPCProfile, ScheduleBlock, SQLiteLifeStore
from engine.society import SocietySimulation


class SocietySimulationTests(unittest.TestCase):
    def setUp(self):
        self.store = SQLiteLifeStore()
        self.life = LifeSimulationEngine(self.store)
        self.society = SocietySimulation(
            self.store.connection, self.store._lock,
            lambda: self.life.simulation_minute, self.life
        )

    def tearDown(self):
        self.store.close()

    def test_building_object_interactions_are_mapped_durable_and_idempotent(self):
        self.society.register_building(
            "building.home.001", "residential", "Home 001",
            {"gta5.shvdn": {"entrance": "home.001.door", "interior": "home.001.interior"}},
            ["enter", "exit", "inspect"]
        )
        self.society.register_object(
            "object.home.001.bed", "building.home.001", "bed",
            {"gta5.shvdn": "home.001.bed.anchor"}, {"condition": 100}
        )
        first = self.society.interact(
            "request.enter.1", "player.main", "building", "building.home.001",
            "enter", "gta5.shvdn"
        )
        replay = self.society.interact(
            "request.enter.1", "player.main", "building", "building.home.001",
            "enter", "gta5.shvdn"
        )
        self.assertEqual(first, replay)
        self.assertTrue(first["accepted"])
        self.assertEqual(len(self.society.pending_commands()), 1)
        cmd = self.society.pending_commands()[0]
        self.assertEqual(cmd["command_type"], "space.transition")
        self.assertEqual(cmd["payload"]["semantic_target"], "home.001.interior")
        self.assertTrue(self.society.acknowledge_command(
            cmd["command_id"], {"success": True, "status": "completed", "observed": "entered"}
        ))
        use = self.society.interact(
            "request.bed.1", "player.main", "object", "object.home.001.bed",
            "use", "gta5.shvdn"
        )
        self.assertTrue(use["accepted"])
        object_command = self.society.pending_commands()[0]
        self.assertEqual(object_command["command_type"], "interaction.use_object")
        self.society.acknowledge_command(object_command["command_id"], {"success": True, "status": "completed"})
        objects = self.society.snapshot()["objects"]
        self.assertEqual(objects[0]["state"]["uses"], 1)
        self.assertEqual(objects[0]["state"]["last_actor_id"], "player.main")

    def test_building_interaction_fails_closed_without_adapter_mapping(self):
        self.society.register_building("building.remote", "shop", "Remote shop", {}, ["enter"])
        with self.assertRaisesRegex(ValueError, "adapter_mapping_unavailable"):
            self.society.interact("request.remote", "player.main", "building",
                                  "building.remote", "enter", "gta5.shvdn")

    def test_double_entry_ledger_property_business_and_sales(self):
        self.society.register_building(
            "building.shop.001", "commercial", "Shop 001",
            {"gta5.shvdn": {"entrance": "shop.001.door"}}, ["enter", "inspect"]
        )
        self.society.create_account("account.seller", "seller", 0, allow_negative=True)
        self.society.create_account("account.buyer", "buyer", 10000)
        self.society.create_account("account.business", "business", 0)
        self.society.create_account("account.tenant", "tenant", 1000)
        self.society.create_property("property.shop.001", "building.shop.001", "account.seller", 5000, 500)
        bought = self.society.purchase_property("tx.purchase.1", "property.shop.001", "account.buyer", 4000)
        self.assertTrue(bought["posted"])
        self.assertEqual(self.society.account("account.buyer")["balance"], 6000)
        self.assertEqual(self.society.account("account.seller")["balance"], 4000)
        replay = self.society.purchase_property("tx.purchase.1", "property.shop.001", "account.buyer", 4000)
        self.assertTrue(replay["duplicate"])
        self.assertEqual(self.society.account("account.buyer")["balance"], 6000)
        self.society.lease_property("tx.lease.1", "property.shop.001", "account.tenant", 500, 1440)
        self.society.create_business("business.shop.001", "property.shop.001",
                                     "account.buyer", "account.business", "retail")
        self.society.set_inventory("business.shop.001", {"rice": 20})
        self.society.sell_product("tx.sale.1", "business.shop.001", "account.buyer", "rice", 2, 100)
        self.assertEqual(self.society.account("account.buyer")["balance"], 6300)
        self.assertEqual(self.society.account("account.business")["balance"], 200)
        snap = self.society.snapshot()
        self.assertEqual(snap["businesses"][0]["inventory"]["rice"], 18)
        total = self.store.connection.execute(
            "SELECT SUM(amount) FROM world_ledger_entries WHERE transaction_id='tx.sale.1'"
        ).fetchone()[0]
        self.assertEqual(total, 0)
        with self.assertRaisesRegex(ValueError, "insufficient_funds"):
            self.society.sell_product("tx.sale.2", "business.shop.001", "account.buyer", "rice", 1, 10000)

    def test_traffic_incident_affects_commuting_npc_and_is_idempotent(self):
        profile = NPCProfile(
            npc_id="npc.driver", display_name="Nam", home_location_id="home",
            occupation="driver", schedule=(ScheduleBlock(0, 1440, "commute", "road.main"),)
        )
        npc = self.life.register_npc(profile)
        self.life.advance(1)
        self.assertEqual(npc.location_id, "road.main")
        before = npc.stress
        self.society.register_traffic_segment("road.main", "arterial", 1.0)
        result = self.society.record_traffic_incident("incident.1", "road.main", 3, "collision", "Lane blocked")
        self.assertEqual(result["affected_npcs"], 1)
        self.assertGreater(npc.stress, before)
        duplicate = self.society.record_traffic_incident("incident.1", "road.main", 3, "collision", "Lane blocked")
        self.assertTrue(duplicate["duplicate"])
        self.assertEqual(self.society.snapshot()["traffic"][0]["congestion"], 36.0)

    def test_wallet_baseline_is_idempotent_and_never_resets_live_balance(self):
        first = self.society.ensure_account("legacy:player_cash", "player.main", 1000)
        self.assertEqual(first["balance"], 1000)
        self.society.ingest_legacy_events("gta5.shvdn", [
            {"sequence": 1, "event_type": "ledger.expense",
             "data": "amount=125;cat=shopping;note=food;acct=cash", "sim_minute": 1}
        ])
        before = self.society.account("legacy:player_cash")["balance"]
        replay = self.society.ensure_account("legacy:player_cash", "player.main", 9000)
        self.assertEqual(replay["balance"], before)
        self.assertEqual(before, 875)

    def test_legacy_cash_events_import_once_into_balanced_ledger(self):
        batch = [
            {"sequence": 10, "event_type": "ledger.income",
             "data": "amount=250;cat=business;note=retail sale;acct=cash", "sim_minute": 12},
            {"sequence": 11, "event_type": "ledger.expense",
             "data": "amount=40;cat=food;note=lunch;acct=cash", "sim_minute": 13},
            {"sequence": 12, "event_type": "ledger.income",
             "data": "amount=70;cat=deposit;note=bank deposit;acct=bank", "sim_minute": 14},
        ]
        result = self.society.ingest_legacy_events("gta5.shvdn", batch)
        self.assertEqual(result["accepted"], 3)
        replay = self.society.ingest_legacy_events("gta5.shvdn", batch)
        self.assertEqual(replay["duplicates"], 3)
        self.assertEqual(self.society.account("legacy:player_cash")["balance"], 210)
        self.assertEqual(self.society.account("legacy:player_bank")["balance"], 70)
        unbalanced = self.store.connection.execute(
            "SELECT SUM(e.amount) FROM world_ledger_entries e JOIN world_transactions t USING(transaction_id) "
            "WHERE t.transaction_id LIKE 'legacy-ledger:%' GROUP BY t.transaction_id"
        ).fetchall()
        self.assertTrue(all(row[0] == 0 for row in unbalanced))


if __name__ == "__main__":
    unittest.main()
