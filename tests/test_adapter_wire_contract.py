"""Wire contract: what the GTA C# scripts send must be allowed by Core and accepted by the Engine Host.

The C# sources are read from disk, so adding or renaming a path or a JSON field on either side
fails here. Requests go to a real Host over loopback HTTP.
"""
import json
import logging
import re
import tempfile
import threading
import unittest
from http.server import ThreadingHTTPServer
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import Request, urlopen

from engine.host import PROTOCOL, State, handler_for

ROOT = Path(__file__).resolve().parents[1]
SOURCES = {p.name: p.read_text(encoding="utf-8-sig") for p in sorted((ROOT / "game").glob("*.cs"))}
CORE = SOURCES["GtaVietLifeCore.cs"]
TOKEN = "T" * 48
ENGINE_PATH = re.compile(r'"(/engine/v1/[A-Za-z0-9_\-/]*[A-Za-z0-9_\-])"')
QUEUED_CALL = re.compile(r'\b(?:post|PostWorldRequest)\("(/engine/[^"]+)",\s*(\w+)\)')
DIRECT_GET = re.compile(r'DownloadString\(hostServer \+ "(/engine/[^"]+)"')
DIRECT_POST = re.compile(r'UploadString\(hostServer \+ "(/engine/[^"]+)", "POST"')


def method_body(source, signature):
    start = source.index(signature)
    depth = 0
    for index in range(source.index("{", start), len(source)):
        depth += {"{": 1, "}": -1}.get(source[index], 0)
        if depth == 0:
            return source[start:index + 1]
    raise AssertionError("unterminated method " + signature)


def core_accepts(path):
    """Evaluate the allow-list written in Core (the literal rules of its path filter)."""
    signature = ("static bool IsEnginePostPath(string path)" if "bool IsEnginePostPath(" in CORE
                 else "void PostWorldRequest(string path, string body)")
    body = method_body(CORE, signature)
    prefixes = re.findall(r'path\.StartsWith\("(/engine/[^"]+)"', body)
    exact = re.findall(r'path == "(/engine/[^"]+)"', body)
    return path in exact or any(path.startswith(prefix) for prefix in prefixes)


def queued_calls():
    calls = []
    for name, source in SOURCES.items():
        for match in QUEUED_CALL.finditer(source):
            calls.append((name, match.group(1), match.group(2), match.start()))
    return calls


def csharp_keys(source, variable, before):
    """JSON keys written into the C# string `variable` assigned closest before position `before`."""
    assignments = list(re.finditer(r"\bstring %s = " % re.escape(variable), source[:before]))
    assert assignments, "no string assignment for " + variable
    start = assignments[-1].start()
    statement = source[start:source.index(";\n", start) if ";\n" in source[start:] else source.index(";\r\n", start)]
    keys = set(re.findall(r'\\"([A-Za-z0-9_.]+)\\":', statement))
    code_only = re.sub(r'"(?:\\.|[^"\\])*"', '""', statement)
    for nested in set(re.findall(r"\+\s*([a-z]\w*)\s*\+", code_only)):
        if nested != variable and re.search(r'\bstring %s = "\{\\"' % nested, source[:start]):
            keys |= csharp_keys(source, nested, start)
    return keys


def sample_keys(value):
    found = set()
    if isinstance(value, dict):
        for key, child in value.items():
            found.add(key)
            found |= sample_keys(child)
    return found


MAPS = {"gta5.shvdn": {"entrance": "shop.gta.a.anchor", "interior": "shop.gta.a.anchor"}}

# Same shapes and the same order as the C# scripts send them.
QUEUED_SAMPLES = [
    ("/engine/v1/world/buildings", {"building_id": "building.shop.a", "category": "retail_business",
        "name": "Burton - cua hang", "adapter_maps": MAPS, "interactions": ["open", "close", "inspect"],
        "attributes": {"tier": 2, "purpose": "shop", "area_m2": 60, "zone": "DOWNT"}}),
    ("/engine/v1/world/objects", {"object_id": "object.home.0.bed", "building_id": "building.shop.a",
        "kind": "bed", "adapter_anchors": {"gta5.shvdn": "bed"}, "initial_state": {}}),
    ("/engine/v1/world/accounts/ensure", {"account_id": "legacy:player_cash", "owner_id": "player.main",
        "opening_balance": 500, "as_of_source": "gta5.shvdn:abc", "as_of_sequence": 12,
        "allow_negative": True, "currency": "USD"}),
    ("/engine/v1/world/accounts/ensure", {"account_id": "legacy:shop.a.cash", "owner_id": "business.shop.a",
        "opening_balance": 40, "allow_negative": True, "currency": "USD"}),
    ("/engine/v1/world/properties", {"property_id": "property.shop.a", "building_id": "building.shop.a",
        "owner_account_id": "legacy:player_cash", "value": 1000, "rent": 20}),
    ("/engine/v1/world/businesses", {"business_id": "business.shop.a", "property_id": "property.shop.a",
        "owner_account_id": "legacy:player_cash", "cash_account_id": "legacy:shop.a.cash", "category": "Quan an"}),
    ("/engine/v1/world/businesses/inventory", {"business_id": "business.shop.a", "inventory": {"stock": 5}}),
    ("/engine/v1/world/businesses/sync", {"business_id": "business.shop.a", "adapter_id": "gta5.shvdn",
        "cash_balance": 55, "inventory": {"stock": 4}, "is_open": True}),
    ("/engine/v1/world/interact", {"actor_id": "player.main", "target_kind": "building",
        "target_id": "building.shop.a", "action": "open", "adapter_id": "gta5.shvdn"}),
    ("/engine/v1/simulation/actors/observe", {"actor_id": "player.main",
        "observed": {"hunger": 61.5, "thirst": 40.0, "energy": 33.0, "hygiene": 72.0, "health": 90.0}}),
    ("/engine/v1/simulation/actors/work-completed", {"actor_id": "player.main", "duration_minutes": 240}),
]


class HostCase(unittest.TestCase):
    def start_host(self, data_dir=None):
        self.state = State(TOKEN, data_dir)
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), handler_for(self.state, logging.getLogger("wire")))
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        self.base = "http://127.0.0.1:%d" % self.server.server_address[1]

    def stop_host(self):
        self.server.shutdown()
        self.server.server_close()
        self.state.simulation.store.close()

    def call(self, path, body=None):
        headers = {"Content-Type": "application/json", "X-GVL-Token": TOKEN}
        data = json.dumps(body).encode() if body is not None else None
        try:
            with urlopen(Request(self.base + path, data=data, headers=headers), timeout=5) as response:
                return response.status, json.loads(response.read())
        except HTTPError as error:
            return error.code, json.loads(error.read())

    def register_player(self):
        return self.call("/engine/v1/simulation/actors", {"request_id": "reg-player", "npc": {
            "npc_id": "player.main", "display_name": "Player", "home_location_id": "player.home",
            "occupation": "citizen", "actor_kind": "player", "age_years": 30, "life_goal": "career",
            "schedule": []}})


class StaticPathContractTests(unittest.TestCase):
    def test_scanner_finds_the_calls_it_is_meant_to_check(self):
        paths = {path for _, path, _, _ in queued_calls()}
        self.assertIn("/engine/v1/simulation/actors/observe", paths)
        self.assertIn("/engine/v1/world/buildings", paths)
        self.assertFalse(core_accepts("/engine/v1/shutdown"), "filter must reject unrelated paths")

    def test_every_queued_path_passes_the_core_filter(self):
        rejected = sorted({(name, path) for name, path, _, _ in queued_calls() if not core_accepts(path)})
        self.assertEqual(rejected, [], "Core would silently drop these requests")

    def test_every_engine_path_literal_is_covered_by_this_contract(self):
        known = {path for _, path, _, _ in queued_calls()}
        for source in SOURCES.values():
            known |= set(DIRECT_GET.findall(source)) | set(DIRECT_POST.findall(source))
        filter_rules = set(ENGINE_PATH.findall(method_body(
            CORE, "static bool IsEnginePostPath(string path)"))) if "bool IsEnginePostPath(" in CORE else set()
        durable = set(re.findall(r'const string WorkPath = "([^"]+)"', CORE))
        literals = {path for source in SOURCES.values() for path in ENGINE_PATH.findall(source)}
        self.assertEqual(sorted(literals - known - filter_rules - durable), [])

    def test_samples_cover_every_queued_path(self):
        self.assertEqual(sorted({p for _, p, _, _ in queued_calls()}), sorted({p for p, _ in QUEUED_SAMPLES}))

    def test_sample_fields_match_the_csharp_bodies(self):
        by_path = {}
        for path, body in QUEUED_SAMPLES:
            by_path.setdefault(path, set()).update(sample_keys(body) | {"request_id"})
        for name, path, variable, position in queued_calls():
            with self.subTest(script=name, path=path, variable=variable):
                sent = csharp_keys(SOURCES[name], variable, position)
                expected = by_path[path]
                self.assertEqual(sorted(sent - expected), [], "C# sends fields the sample does not model")
                optional = {"gta5.shvdn", "entrance", "interior", "stock", "attributes", "tier", "purpose", "area_m2", "zone"}
                if variable == "cashBody":   # shop tills receive no player events, so no opening sequence
                    optional |= {"as_of_source", "as_of_sequence"}
                self.assertEqual(sorted(expected - sent - optional), [],
                                 "C# no longer sends these fields")


class LiveHostContractTests(HostCase):
    def setUp(self):
        self.start_host()

    def tearDown(self):
        self.stop_host()

    def test_every_csharp_path_has_a_host_route(self):
        gets, posts = set(), {path for _, path, _, _ in queued_calls()}
        for source in SOURCES.values():
            gets |= set(DIRECT_GET.findall(source))
            posts |= set(DIRECT_POST.findall(source))
        self.assertTrue(gets and posts)
        for path in sorted(gets):
            with self.subTest(method="GET", path=path):
                self.assertEqual(self.call(path)[0], 200)
        for path in sorted(posts):
            with self.subTest(method="POST", path=path):
                body = self.call(path, {"request_id": "probe" + path})[1]
                self.assertNotEqual(body.get("error"), "not_found")
        # Control: an unknown route must be reported as such, otherwise the loop above proves nothing.
        self.assertEqual(self.call("/engine/v1/does-not-exist", {"request_id": "x"})[1]["error"], "not_found")

    def test_queued_requests_are_accepted_and_change_engine_state(self):
        self.assertEqual(self.register_player()[0], 201)
        for number, (path, body) in enumerate(QUEUED_SAMPLES):
            self.assertTrue(core_accepts(path), "Core drops " + path)
            status, reply = self.call(path, dict(body, request_id="queued-%d" % number))
            with self.subTest(path=path):
                self.assertIn(status, (200, 201, 202), reply)
        player = self.state.simulation.npcs["player.main"]
        self.assertEqual((player.hunger, player.energy, player.health), (61.5, 33.0, 90.0))
        self.assertEqual(player.work_minutes, 240)

    def test_replayed_work_report_is_counted_once(self):
        self.register_player()
        body = {"request_id": "shift-1", "actor_id": "player.main", "duration_minutes": 240}
        self.call("/engine/v1/simulation/actors/work-completed", body)
        self.call("/engine/v1/simulation/actors/work-completed", body)
        self.assertEqual(self.state.simulation.npcs["player.main"].work_minutes, 240)


class RestartContractTests(HostCase):
    def resident_body(self, request_id):
        home = "res_1.home"
        blocks = [(0, 360, "sleep", home), (360, 1140, "work", "res_1.work"),
                  (1140, 1440, "leisure", "residents.leisure")]
        return {"request_id": request_id, "npc": {
            "npc_id": "res_1", "display_name": "Lan", "home_location_id": home, "occupation": "resident",
            "life_goal": "social", "traits": ["community_resident"],
            "schedule": [dict(zip(("start_minute", "end_minute", "activity", "location_id"), b)) for b in blocks]}}

    def test_resident_and_player_register_again_after_host_restart(self):
        with tempfile.TemporaryDirectory() as folder:
            self.start_host(folder)
            try:
                self.assertEqual(self.call("/engine/v1/simulation/npcs", self.resident_body("a"))[0], 201)
                self.assertEqual(self.register_player()[0], 201)
            finally:
                self.stop_host()
            self.start_host(folder)
            try:
                status, reply = self.call("/engine/v1/simulation/npcs", self.resident_body("b"))
                self.assertEqual(status, 201, reply)
                status, reply = self.register_player()
                self.assertEqual(status, 201, reply)
            finally:
                self.stop_host()

    def test_handshake_shape_is_accepted(self):
        self.start_host()
        try:
            status, reply = self.call("/engine/v1/handshake", {
                "protocol": PROTOCOL, "request_id": "h", "session_id": "s", "world_minute": 600,
                "adapter": {"id": "gta5.shvdn", "version": "0.1.0", "base_game": "gta5-enhanced",
                            "base_game_build": "unknown"}, "capabilities": ["adapter.session"]})
            self.assertEqual(status, 200, reply)
        finally:
            self.stop_host()


if __name__ == "__main__":
    unittest.main()
