"""Regressions for world objects and commands (M3) and the host gateway (M5)."""
import json
import logging
import threading
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen

from engine import host
from engine.life_simulation import LifeSimulationEngine, SQLiteLifeStore
from engine.society import COMMAND_TTL_MINUTES, SocietySimulation

MAPS = {"gta5.shvdn": {"interior": "i"}, "godot.reference": {"interior": "g"}}


class WorldCommandTests(unittest.TestCase):
    def setUp(self):
        self.engine = LifeSimulationEngine(SQLiteLifeStore())
        self.minute = 100
        self.society = SocietySimulation(self.engine.store.connection, self.engine.store._lock,
                                         lambda: self.minute, self.engine)
        self.society.register_building("home", "residential", "Home", MAPS, ["enter"])

    def tearDown(self):
        self.engine.store.close()

    def enter(self, request_id, adapter="gta5.shvdn"):
        return self.society.interact(request_id, "player.main", "building", "home", "enter", adapter)["command_id"]

    def test_registering_an_object_again_keeps_its_state(self):
        anchors = {"gta5.shvdn": "bed"}
        self.society.register_object("bed", "home", "bed", anchors, {"uses": 0})
        command = self.society.interact("u", "player.main", "object", "bed", "use", "gta5.shvdn")["command_id"]
        self.society.acknowledge_command(command, {"success": True})
        self.society.register_object("bed", "home", "bed", anchors, {})
        self.assertEqual(self.society.snapshot()["objects"][0]["state"]["uses"], 1)

    def test_unanswered_commands_expire(self):
        old = self.enter("a")
        self.minute += COMMAND_TTL_MINUTES - 1
        self.assertEqual([c["command_id"] for c in self.society.pending_commands()], [old])
        self.minute += 1
        self.assertEqual(self.society.pending_commands(), [])
        self.assertFalse(self.society.acknowledge_command(old, {"success": True}))
        self.assertEqual(self.engine.store.connection.execute(
            "SELECT status FROM world_command_outbox").fetchone()[0], "expired")

    def test_a_late_answer_is_refused_even_before_the_next_poll(self):
        command = self.enter("b")
        self.minute += COMMAND_TTL_MINUTES
        self.assertFalse(self.society.acknowledge_command(command, {"success": True}))

    def test_each_adapter_receives_only_its_commands(self):
        gta = self.enter("g")
        godot = self.enter("d", "godot.reference")
        ids = lambda adapter: [c["command_id"] for c in self.society.pending_commands(50, adapter)]
        self.assertEqual(ids("gta5.shvdn"), [gta])
        self.assertEqual(ids("godot.reference"), [godot])
        self.assertEqual(len(self.society.pending_commands()), 2)


class GatewayTests(unittest.TestCase):
    def setUp(self):
        self.state = host.State("T" * 48)
        self.server = host.EngineServer(("127.0.0.1", 0), host.handler_for(self.state, logging.getLogger("t")))
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        self.base = "http://127.0.0.1:%d" % self.server.server_address[1]

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.state.simulation.store.close()

    def call(self, path, body=None, token="T" * 48):
        request = Request(self.base + path, data=None if body is None else json.dumps(body).encode(),
                          headers={"X-GVL-Token": token})
        try:
            with urlopen(request, timeout=5) as response:
                return response.status, json.loads(response.read())
        except HTTPError as error:
            with error:
                return error.code, json.loads(error.read())

    def test_non_ascii_token_is_simply_unauthorized(self):
        self.assertFalse(self.state.auth("mật-khẩu"))
        self.assertEqual(self.call("/engine/v1/status", token="é" * 48)[0], 401)
        self.assertEqual(self.call("/engine/v1/heartbeat", {"request_id": "x"}, token="é" * 48)[0], 401)

    def test_another_protocol_is_refused_on_every_post(self):
        status, reply = self.call("/engine/v1/world/events",
                                  {"request_id": "p", "protocol": "gvl-engine/9", "source_id": "s", "events": []})
        self.assertEqual((status, reply["error"]), (400, "protocol_mismatch"))
        self.assertEqual(self.call("/engine/v1/world/events",
                                   {"request_id": "q", "protocol": host.PROTOCOL, "source_id": "s", "events": []})[0], 200)


if __name__ == "__main__":
    unittest.main()
