"""Vietnam Data Pack: schema, fixed identities, dialogue in context, regional prices, safe migration."""
import json
import logging
import shutil
import sqlite3
import tempfile
import threading
import unittest
from pathlib import Path
from urllib.request import Request, urlopen

from engine import datapack, host
from engine.datapack import DataPack, DataPackError, IdentityStore
from engine.life_simulation import LifeSimulationEngine, NPCProfile, SQLiteLifeStore
from engine.society import SocietySimulation

PACK = Path(__file__).resolve().parents[2] / "data" / "packs" / "vi-VN"


class SchemaTests(unittest.TestCase):
    def copy(self):
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        target = Path(folder.name) / "packs"
        shutil.copytree(PACK.parent, target)
        return target / "vi-VN"

    def test_shipped_pack_is_valid(self):
        pack = DataPack.load(PACK)
        self.assertEqual((pack.version, pack.currency), ("1.0.0", "VND"))
        self.assertEqual((len(pack.names), len(pack.occupations), len(pack.menu), len(pack.goods), len(pack.dialogue),
                          len(pack.mlo_candidates)), (40, 30, 30, 25, 40, 10))
        self.assertTrue(all(c["status"].startswith("candidate_") for c in pack.mlo_candidates))

    def test_damaged_pack_is_refused_with_the_reason(self):
        cases = {
            "npc_names.json": lambda rows: rows[0].__setitem__("gender", "robot"),
            "goods_prices.json": lambda rows: rows[0].__setitem__("price_min", 10 ** 9),
            "dialogue_templates.json": lambda rows: rows.append(dict(rows[0])),
            "occupations.json": lambda rows: rows[0].pop("sector"),
        }
        for name, damage in cases.items():
            with self.subTest(file=name):
                folder = self.copy()
                rows = json.loads((folder / name).read_text(encoding="utf-8"))
                damage(rows)
                (folder / name).write_text(json.dumps(rows, ensure_ascii=False), encoding="utf-8")
                with self.assertRaises(DataPackError) as caught:
                    DataPack.load(folder)
                self.assertIn(name.split(".")[0], str(caught.exception))

    def test_a_broken_pack_never_stops_the_engine(self):
        folder = self.copy()
        (folder / "pack.json").write_text("{", encoding="utf-8")
        original = datapack.default_pack_folder
        datapack.default_pack_folder = lambda: folder
        try:
            with self.assertLogs("gvl.engine", "ERROR"):
                state = host.State("t" * 40)
            self.assertIsNone(state.pack)
            state.simulation.store.close()
        finally:
            datapack.default_pack_folder = original


class IdentityTests(unittest.TestCase):
    def test_identity_is_created_once_and_survives_restart_and_pack_updates(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "life.sqlite3"
            pack = DataPack.load(PACK)
            store = SQLiteLifeStore(path)
            first = IdentityStore(store.connection, store._lock, pack).identity("npc.street.0001")
            store.close()
            names = [n["last_name"] + " " + n["first_name"] for n in pack.names]
            self.assertIn(first["display_name"], names)
            job = next(j for j in pack.occupations if j["id"] == first["occupation_id"])
            self.assertTrue(job["monthly_income_min"] <= first["monthly_income"] <= job["monthly_income_max"])
            # A newer pack with other names must not rename people who already exist.
            pack.names = [{"id": "name_999", "first_name": "Khác", "last_name": "Tên", "gender": "male"}]
            pack.version = "2.0.0"
            store = SQLiteLifeStore(path)
            again = IdentityStore(store.connection, store._lock, pack)
            self.assertEqual(again.identity("npc.street.0001"), first)
            self.assertEqual(again.identity("npc.street.0002")["display_name"], "Tên Khác")
            store.close()

    def test_different_ids_spread_over_the_names(self):
        store = SQLiteLifeStore()
        identities = IdentityStore(store.connection, store._lock, DataPack.load(PACK))
        names = {identities.identity("npc.%d" % n)["name_id"] for n in range(200)}
        self.assertGreater(len(names), 25)
        store.close()


class DialogueAndPriceTests(unittest.TestCase):
    def setUp(self):
        self.pack = DataPack.load(PACK)

    def test_intent_is_read_from_what_the_player_says(self):
        self.assertEqual(self.pack.detect_intent("Cho tôi một tô phở bò"), "food_order")
        self.assertEqual(self.pack.detect_intent("Món này bao nhiêu tiền?"), "food_order")
        self.assertEqual(self.pack.detect_intent("Xe tôi có tiếng kêu"), "repair")
        self.assertEqual(self.pack.detect_intent("Ừm"), "small_talk")

    def test_templates_respect_relationship_mood_and_place(self):
        pick = lambda *a, **k: {r["intent"] for r in self.pack.dialogue_for(*a, **k)}
        self.assertEqual(pick("greeting", 10.0, 10.0, "street"), {"greeting"})
        self.assertEqual(pick("relationship", -60.0, 10.0, "street"), {"conflict"})
        self.assertEqual(pick("small_talk", 0.0, 90.0, "street"), {"emotional"})
        self.assertEqual(pick("food_order", 0.0, 0.0, "home"), {"small_talk"})
        self.assertEqual(pick("food_order", 0.0, 0.0, "restaurant"), {"food_order"})
        first = self.pack.dialogue_for("greeting", seed="npc.1")
        self.assertEqual(first, self.pack.dialogue_for("greeting", seed="npc.1"))

    def test_prices_follow_zone_wealth_and_stay_in_range(self):
        for row in self.pack.menu + self.pack.goods:
            poor = self.pack.price(row["id"], "STRAW", 3)
            rich = self.pack.price(row["id"], "RICHM", 3)
            self.assertLessEqual(poor, rich, row["id"])
            for value in (poor, rich, self.pack.price(row["id"], "DOWNT", 9)):
                self.assertTrue(row["price_min"] - 100 <= value <= row["price_max"] + 100, (row["id"], value))
        self.assertEqual({r["category"] for r in self.pack.menu_for("cafe")} & {"food"}, set())
        with self.assertRaises(KeyError):
            self.pack.price("nope")


class MigrationTests(unittest.TestCase):
    def test_installing_the_pack_on_an_existing_save_changes_nothing_that_was_there(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "life-simulation.sqlite3"   # the file the host opens
            engine = LifeSimulationEngine(SQLiteLifeStore(path))
            society = SocietySimulation(engine.store.connection, engine.store._lock, lambda: 0, engine)
            engine.register_npc(NPCProfile("res_1", "Lan", "res_1.home", "resident"))
            society.create_account("legacy:player_bank", "player.main", 5000)
            engine.store.close()
            raw = sqlite3.connect(str(path))
            before = {t: raw.execute("SELECT * FROM %s ORDER BY 1" % t).fetchall()
                      for t in ("npc_state", "world_accounts", "world_ledger_entries")}
            raw.close()
            state = host.State("t" * 40, folder)
            try:
                self.assertIsNotNone(state.pack)
                self.assertEqual(state.simulation.npcs["res_1"].profile.display_name, "Lan")
                self.assertEqual(state.society.account("legacy:player_bank")["balance"], 5000)
            finally:
                state.simulation.store.close()
            raw = sqlite3.connect(str(path))
            after = {t: raw.execute("SELECT * FROM %s ORDER BY 1" % t).fetchall() for t in before}
            raw.close()
            self.assertEqual({t: [r[:2] for r in rows] for t, rows in before.items()},
                             {t: [r[:2] for r in rows] for t, rows in after.items()})
            self.assertEqual(before["world_accounts"], after["world_accounts"])


class HostRouteTests(unittest.TestCase):
    def setUp(self):
        self.state = host.State("T" * 48)
        self.server = host.EngineServer(("127.0.0.1", 0), host.handler_for(self.state, logging.getLogger("t")))
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        self.base = "http://127.0.0.1:%d" % self.server.server_address[1]

    def tearDown(self):
        self.server.shutdown(); self.server.server_close(); self.state.simulation.store.close()

    def post(self, path, body):
        request = Request(self.base + path, data=json.dumps(dict(body, request_id="r")).encode(), headers={"X-GVL-Token": "T" * 48})
        with urlopen(request, timeout=5) as response:
            return json.loads(response.read())

    def test_routes(self):
        npc = self.post("/engine/v1/simulation/npcs", {"npc": {"npc_id": "npc.walker.7", "home_location_id": "h", "schedule": []}})
        identity = self.post("/engine/v1/world/identities", {"persistent_id": "npc.walker.7"})
        self.assertTrue(npc["registered"])
        self.assertEqual(self.state.simulation.npcs["npc.walker.7"].profile.display_name, identity["display_name"])
        self.assertEqual(self.post("/engine/v1/world/prices/quote", {"item_id": "food_001", "zone": "STRAW"})["currency"], "VND")
        reply = self.post("/engine/v1/simulation/dialogue/template", {"npc_id": "npc.walker.7", "text": "Cho tôi một ly trà đá", "place": "cafe"})
        self.assertEqual(reply["intent"], "food_order")
        self.assertTrue(reply["templates"])
        context = self.state.simulation.dialogue_context("npc.walker.7", "player.main", "Chào anh")
        self.assertTrue(context["dialogue_examples"])


if __name__ == "__main__":
    unittest.main()
