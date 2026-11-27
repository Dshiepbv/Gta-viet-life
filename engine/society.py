"""Persistent game-neutral world, interaction, property, business, ledger and traffic systems.

The base game adapter supplies semantic-to-native mappings and executes world commands.
This module owns canonical facts and consequences; it never imports a game SDK.
"""
from __future__ import annotations

import json
import sqlite3
import threading
import time
import uuid
from typing import Any, Callable, Dict, Optional


MAX_ID = 128
MAX_TEXT = 500


def _valid_id(value: Any, label: str) -> str:
    if not isinstance(value, str) or not value or len(value) > MAX_ID:
        raise ValueError("invalid_" + label)
    return value


class SocietySimulation:
    """Game-neutral persistent society services sharing the Engine Host SQLite connection."""

    def __init__(self, connection: sqlite3.Connection, lock: threading.RLock,
                 minute_provider: Optional[Callable[[], int]] = None, life_engine=None):
        self.db = connection
        self.lock = lock
        self.minute_provider = minute_provider or (lambda: 0)
        self.life_engine = life_engine
        self._migrate()

    def _migrate(self):
        with self.lock, self.db:
            self.db.executescript("""
                CREATE TABLE IF NOT EXISTS world_buildings(
                    building_id TEXT PRIMARY KEY, category TEXT NOT NULL, name TEXT NOT NULL,
                    adapter_maps_json TEXT NOT NULL, interactions_json TEXT NOT NULL,
                    revision INTEGER NOT NULL DEFAULT 1);
                CREATE TABLE IF NOT EXISTS world_objects(
                    object_id TEXT PRIMARY KEY, building_id TEXT NOT NULL, kind TEXT NOT NULL,
                    adapter_anchors_json TEXT NOT NULL, state_json TEXT NOT NULL,
                    FOREIGN KEY(building_id) REFERENCES world_buildings(building_id));
                CREATE TABLE IF NOT EXISTS world_command_outbox(
                    command_id TEXT PRIMARY KEY, request_id TEXT UNIQUE NOT NULL, actor_id TEXT NOT NULL,
                    command_type TEXT NOT NULL, payload_json TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'pending', result_json TEXT,
                    created_minute INTEGER NOT NULL, completed_at TEXT);
                CREATE INDEX IF NOT EXISTS idx_world_commands ON world_command_outbox(status,created_minute);
                CREATE TABLE IF NOT EXISTS world_request_dedup(
                    request_id TEXT PRIMARY KEY, response_json TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS world_accounts(
                    account_id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, balance INTEGER NOT NULL,
                    allow_negative INTEGER NOT NULL DEFAULT 0, currency TEXT NOT NULL DEFAULT 'USD');
                CREATE TABLE IF NOT EXISTS world_transactions(
                    transaction_id TEXT PRIMARY KEY, source TEXT NOT NULL, description TEXT NOT NULL,
                    sim_minute INTEGER NOT NULL, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
                CREATE TABLE IF NOT EXISTS world_ledger_entries(
                    entry_id INTEGER PRIMARY KEY AUTOINCREMENT, transaction_id TEXT NOT NULL,
                    account_id TEXT NOT NULL, amount INTEGER NOT NULL,
                    FOREIGN KEY(transaction_id) REFERENCES world_transactions(transaction_id),
                    FOREIGN KEY(account_id) REFERENCES world_accounts(account_id));
                CREATE INDEX IF NOT EXISTS idx_world_ledger_account ON world_ledger_entries(account_id,entry_id);
                CREATE TABLE IF NOT EXISTS world_properties(
                    property_id TEXT PRIMARY KEY, building_id TEXT NOT NULL, owner_account_id TEXT NOT NULL,
                    value INTEGER NOT NULL, rent INTEGER NOT NULL, tenant_account_id TEXT,
                    lease_until INTEGER NOT NULL DEFAULT 0, condition REAL NOT NULL DEFAULT 100,
                    FOREIGN KEY(building_id) REFERENCES world_buildings(building_id));
                CREATE TABLE IF NOT EXISTS world_businesses(
                    business_id TEXT PRIMARY KEY, property_id TEXT NOT NULL, owner_account_id TEXT NOT NULL,
                    cash_account_id TEXT NOT NULL, category TEXT NOT NULL,
                    inventory_json TEXT NOT NULL, employees_json TEXT NOT NULL,
                    is_open INTEGER NOT NULL DEFAULT 1,
                    FOREIGN KEY(property_id) REFERENCES world_properties(property_id));
                CREATE TABLE IF NOT EXISTS world_traffic_segments(
                    segment_id TEXT PRIMARY KEY, road_class TEXT NOT NULL,
                    congestion REAL NOT NULL DEFAULT 0, capacity REAL NOT NULL DEFAULT 1,
                    updated_minute INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE IF NOT EXISTS world_traffic_incidents(
                    incident_id TEXT PRIMARY KEY, segment_id TEXT NOT NULL, severity INTEGER NOT NULL,
                    kind TEXT NOT NULL, details TEXT NOT NULL, sim_minute INTEGER NOT NULL,
                    FOREIGN KEY(segment_id) REFERENCES world_traffic_segments(segment_id));
                CREATE TABLE IF NOT EXISTS world_event_journal(
                    event_id TEXT PRIMARY KEY, event_type TEXT NOT NULL, actor_id TEXT,
                    target_id TEXT, payload_json TEXT NOT NULL, sim_minute INTEGER NOT NULL);
            """)
            self.db.execute("INSERT OR IGNORE INTO world_accounts(account_id,owner_id,balance,allow_negative,currency) VALUES('system:opening_equity','system',0,1,'USD')")

    def register_building(self, building_id: str, category: str, name: str,
                          adapter_maps: dict, interactions: list) -> dict:
        building_id = _valid_id(building_id, "building_id")
        if not isinstance(category, str) or not category or len(category) > 64:
            raise ValueError("invalid_building_category")
        if not isinstance(name, str) or not name or len(name) > MAX_TEXT:
            raise ValueError("invalid_building_name")
        if not isinstance(adapter_maps, dict) or len(adapter_maps) > 16:
            raise ValueError("invalid_adapter_maps")
        for adapter_id, mapping in adapter_maps.items():
            _valid_id(adapter_id, "adapter_id")
            if not isinstance(mapping, dict) or any(not isinstance(k, str) or not isinstance(v, str) or not v for k, v in mapping.items()):
                raise ValueError("invalid_adapter_mapping")
        if not isinstance(interactions, list) or len(interactions) > 32 or any(x not in ("enter", "exit", "inspect", "open", "close") for x in interactions):
            raise ValueError("invalid_building_interactions")
        with self.lock, self.db:
            old = self.db.execute("SELECT category,name FROM world_buildings WHERE building_id=?", (building_id,)).fetchone()
            if old and (old["category"] != category or old["name"] != name):
                raise ValueError("building_identity_conflict")
            self.db.execute("""INSERT INTO world_buildings(building_id,category,name,adapter_maps_json,interactions_json)
                VALUES(?,?,?,?,?) ON CONFLICT(building_id) DO UPDATE SET
                adapter_maps_json=excluded.adapter_maps_json,interactions_json=excluded.interactions_json,
                revision=world_buildings.revision+1""",
                (building_id, category, name, json.dumps(adapter_maps, ensure_ascii=False),
                 json.dumps(sorted(set(interactions)))))
        return {"building_id": building_id, "registered": True}

    def register_object(self, object_id: str, building_id: str, kind: str,
                        adapter_anchors: dict, initial_state: Optional[dict] = None) -> dict:
        object_id = _valid_id(object_id, "object_id")
        building_id = _valid_id(building_id, "building_id")
        if not isinstance(kind, str) or not kind or len(kind) > 64:
            raise ValueError("invalid_object_kind")
        if not isinstance(adapter_anchors, dict) or not adapter_anchors:
            raise ValueError("object_adapter_anchor_required")
        for adapter_id, anchor in adapter_anchors.items():
            _valid_id(adapter_id, "adapter_id")
            _valid_id(anchor, "adapter_anchor")
        with self.lock, self.db:
            if not self.db.execute("SELECT 1 FROM world_buildings WHERE building_id=?", (building_id,)).fetchone():
                raise KeyError("building_not_found")
            self.db.execute("""INSERT INTO world_objects(object_id,building_id,kind,adapter_anchors_json,state_json)
                VALUES(?,?,?,?,?) ON CONFLICT(object_id) DO UPDATE SET
                adapter_anchors_json=excluded.adapter_anchors_json,state_json=excluded.state_json""",
                (object_id, building_id, kind, json.dumps(adapter_anchors), json.dumps(initial_state or {})))
        return {"object_id": object_id, "registered": True}

    def interact(self, request_id: str, actor_id: str, target_kind: str, target_id: str,
                 action: str, adapter_id: str, payload: Optional[dict] = None) -> dict:
        request_id = _valid_id(request_id, "request_id")
        actor_id = _valid_id(actor_id, "actor_id")
        target_id = _valid_id(target_id, "target_id")
        adapter_id = _valid_id(adapter_id, "adapter_id")
        if action not in ("enter", "exit", "inspect", "open", "close", "use"):
            raise ValueError("unsupported_world_interaction")
        if not isinstance(payload or {}, dict) or len(json.dumps(payload or {})) > 8000:
            raise ValueError("invalid_interaction_payload")
        with self.lock, self.db:
            cached = self.db.execute("SELECT response_json FROM world_request_dedup WHERE request_id=?", (request_id,)).fetchone()
            if cached:
                return json.loads(cached["response_json"])
            if target_kind == "building":
                row = self.db.execute("SELECT * FROM world_buildings WHERE building_id=?", (target_id,)).fetchone()
                if not row:
                    raise KeyError("building_not_found")
                allowed = json.loads(row["interactions_json"])
                maps = json.loads(row["adapter_maps_json"]).get(adapter_id, {})
                semantic_target = maps.get("interior" if action in ("enter", "open") else "entrance" if action in ("exit", "close") else "interaction")
            elif target_kind == "object":
                row = self.db.execute("SELECT * FROM world_objects WHERE object_id=?", (target_id,)).fetchone()
                if not row:
                    raise KeyError("object_not_found")
                allowed = ["use", "inspect"]
                semantic_target = json.loads(row["adapter_anchors_json"]).get(adapter_id)
            else:
                raise ValueError("invalid_target_kind")
            if action not in allowed:
                raise ValueError("interaction_not_allowed")
            if not semantic_target:
                raise ValueError("adapter_mapping_unavailable")
            command_id = uuid.uuid4().hex
            command_type = "space.transition" if target_kind == "building" and action in ("enter", "exit", "open", "close") else "interaction.use_object"
            command_payload = {"actor_id": actor_id, "target_kind": target_kind, "target_id": target_id, "action": action,
                               "semantic_target": semantic_target, "adapter_id": adapter_id,
                               "parameters": payload or {}}
            response = {"accepted": True, "command_id": command_id, "status": "pending",
                        "target_id": target_id, "action": action}
            self.db.execute("""INSERT INTO world_command_outbox
                (command_id,request_id,actor_id,command_type,payload_json,created_minute)
                VALUES(?,?,?,?,?,?)""",
                (command_id, request_id, actor_id, command_type, json.dumps(command_payload, ensure_ascii=False),
                 int(self.minute_provider())))
            self.db.execute("INSERT INTO world_request_dedup(request_id,response_json) VALUES(?,?)",
                            (request_id, json.dumps(response)))
            self.db.execute("INSERT INTO world_event_journal(event_id,event_type,actor_id,target_id,payload_json,sim_minute) VALUES(?,?,?,?,?,?)",
                            (uuid.uuid4().hex, "world.interaction_requested", actor_id, target_id,
                             json.dumps(command_payload, ensure_ascii=False), int(self.minute_provider())))
            return response

    def pending_commands(self, limit: int = 50) -> list:
        if isinstance(limit, bool) or not isinstance(limit, int) or not 1 <= limit <= 200:
            raise ValueError("invalid_command_limit")
        with self.lock:
            rows = self.db.execute("""SELECT command_id,actor_id,command_type,payload_json,created_minute
                FROM world_command_outbox WHERE status='pending' ORDER BY created_minute,command_id LIMIT ?""",
                (limit,)).fetchall()
            return [{"command_id": r["command_id"], "npc_id": r["actor_id"],
                     "command_type": r["command_type"], "payload": json.loads(r["payload_json"]),
                     "created_minute": r["created_minute"]} for r in rows]

    def acknowledge_command(self, command_id: str, result: dict) -> bool:
        _valid_id(command_id, "command_id")
        if not isinstance(result, dict):
            raise ValueError("command_result_object_required")
        status = "completed" if result.get("success") is True else result.get("status")
        if status not in ("completed", "failed", "unsupported", "rejected", "cancelled"):
            raise ValueError("invalid_command_result")
        with self.lock, self.db:
            row = self.db.execute("SELECT command_type,payload_json FROM world_command_outbox WHERE command_id=? AND status='pending'",
                                  (command_id,)).fetchone()
            if not row:
                return False
            cur = self.db.execute("""UPDATE world_command_outbox SET status=?,result_json=?,completed_at=CURRENT_TIMESTAMP
                WHERE command_id=? AND status='pending'""", (status, json.dumps(result), command_id))
            if cur.rowcount == 1 and status == "completed" and row["command_type"] == "interaction.use_object":
                payload = json.loads(row["payload_json"])
                target_id = payload.get("target_id", "")
                object_row = self.db.execute("SELECT state_json FROM world_objects WHERE object_id=?", (target_id,)).fetchone()
                if object_row:
                    state = json.loads(object_row["state_json"])
                    state["uses"] = int(state.get("uses", 0)) + 1
                    state["last_used_minute"] = int(self.minute_provider())
                    state["last_actor_id"] = payload.get("actor_id", "")
                    self.db.execute("UPDATE world_objects SET state_json=? WHERE object_id=?",
                                    (json.dumps(state), target_id))
            return cur.rowcount == 1

    def create_account(self, account_id: str, owner_id: str, opening_balance: int = 0,
                       allow_negative: bool = False, currency: str = "USD") -> dict:
        account_id = _valid_id(account_id, "account_id")
        owner_id = _valid_id(owner_id, "owner_id")
        if isinstance(opening_balance, bool) or not isinstance(opening_balance, int) or opening_balance < 0:
            raise ValueError("invalid_opening_balance")
        if not isinstance(allow_negative, bool) or currency not in ("USD", "VND"):
            raise ValueError("invalid_account_policy")
        with self.lock, self.db:
            if self.db.execute("SELECT 1 FROM world_accounts WHERE account_id=?", (account_id,)).fetchone():
                raise ValueError("account_already_exists")
            self.db.execute("INSERT INTO world_accounts(account_id,owner_id,balance,allow_negative,currency) VALUES(?,?,0,?,?)",
                            (account_id, owner_id, 1 if allow_negative else 0, currency))
            if opening_balance:
                self._post_transaction_locked("opening:" + account_id, "opening", "Opening balance",
                    [{"account_id": account_id, "amount": opening_balance},
                     {"account_id": "system:opening_equity", "amount": -opening_balance}])
        return self.account(account_id)

    def _post_transaction_locked(self, transaction_id: str, source: str, description: str, entries: list) -> dict:
        transaction_id = _valid_id(transaction_id, "transaction_id")
        if not isinstance(source, str) or not source or len(source) > 64:
            raise ValueError("invalid_transaction_source")
        if not isinstance(description, str) or len(description) > MAX_TEXT:
            raise ValueError("invalid_transaction_description")
        if not isinstance(entries, list) or len(entries) < 2 or len(entries) > 16:
            raise ValueError("invalid_ledger_entries")
        total = 0
        normalized = []
        for entry in entries:
            if not isinstance(entry, dict) or isinstance(entry.get("amount"), bool) or not isinstance(entry.get("amount"), int):
                raise ValueError("invalid_ledger_entry")
            account_id = _valid_id(entry.get("account_id"), "account_id")
            total += entry["amount"]
            normalized.append((account_id, entry["amount"]))
        if total != 0:
            raise ValueError("unbalanced_transaction")
        existing = self.db.execute("SELECT 1 FROM world_transactions WHERE transaction_id=?", (transaction_id,)).fetchone()
        if existing:
            return {"posted": True, "duplicate": True, "transaction_id": transaction_id}
        balances = {}
        for account_id, amount in normalized:
            row = self.db.execute("SELECT balance,allow_negative FROM world_accounts WHERE account_id=?", (account_id,)).fetchone()
            if not row:
                raise KeyError("account_not_found:" + account_id)
            balances[account_id] = balances.get(account_id, row["balance"]) + amount
            if not row["allow_negative"] and balances[account_id] < 0:
                raise ValueError("insufficient_funds:" + account_id)
        self.db.execute("INSERT INTO world_transactions(transaction_id,source,description,sim_minute) VALUES(?,?,?,?)",
                        (transaction_id, source, description, int(self.minute_provider())))
        for account_id, amount in normalized:
            self.db.execute("INSERT INTO world_ledger_entries(transaction_id,account_id,amount) VALUES(?,?,?)",
                            (transaction_id, account_id, amount))
            self.db.execute("UPDATE world_accounts SET balance=balance+? WHERE account_id=?", (amount, account_id))
        self.db.execute("INSERT INTO world_event_journal(event_id,event_type,target_id,payload_json,sim_minute) VALUES(?,?,?,?,?)",
                        (uuid.uuid4().hex, "economy.transaction_posted", transaction_id,
                         json.dumps({"source": source, "description": description, "entries": normalized}),
                         int(self.minute_provider())))
        return {"posted": True, "duplicate": False, "transaction_id": transaction_id}

    def post_transaction(self, transaction_id: str, source: str, description: str, entries: list) -> dict:
        with self.lock, self.db:
            return self._post_transaction_locked(transaction_id, source, description, entries)

    def ensure_account(self, account_id: str, owner_id: str, opening_balance: int = 0,
                       allow_negative: bool = True, currency: str = "USD") -> dict:
        """Create an opening balance once; repeated adapter sync never resets a live ledger."""
        account_id = _valid_id(account_id, "account_id")
        with self.lock, self.db:
            row = self.db.execute("SELECT account_id,balance FROM world_accounts WHERE account_id=?", (account_id,)).fetchone()
            if row:
                entries = self.db.execute("SELECT COUNT(*) AS n FROM world_ledger_entries WHERE account_id=?", (account_id,)).fetchone()["n"]
                if entries == 0 and row["balance"] == 0 and opening_balance > 0:
                    self._post_transaction_locked("opening:" + account_id, "opening", "Opening balance",
                        [{"account_id": account_id, "amount": opening_balance},
                         {"account_id": "system:opening_equity", "amount": -opening_balance}])
                return self.account(account_id)
        return self.create_account(account_id, owner_id, opening_balance, allow_negative, currency)

    def account(self, account_id: str) -> dict:
        with self.lock:
            row = self.db.execute("SELECT account_id,owner_id,balance,currency,allow_negative FROM world_accounts WHERE account_id=?",
                                  (account_id,)).fetchone()
            if not row:
                raise KeyError("account_not_found")
            return dict(row)

    def create_property(self, property_id: str, building_id: str, owner_account_id: str,
                        value: int, rent: int) -> dict:
        property_id = _valid_id(property_id, "property_id")
        with self.lock, self.db:
            if not self.db.execute("SELECT 1 FROM world_buildings WHERE building_id=?", (building_id,)).fetchone():
                raise KeyError("building_not_found")
            if not self.db.execute("SELECT 1 FROM world_accounts WHERE account_id=?", (owner_account_id,)).fetchone():
                raise KeyError("account_not_found")
            if any(isinstance(v, bool) or not isinstance(v, int) or v < 0 for v in (value, rent)):
                raise ValueError("invalid_property_finance")
            existing = self.db.execute("SELECT building_id,owner_account_id FROM world_properties WHERE property_id=?", (property_id,)).fetchone()
            if existing:
                if existing["building_id"] != building_id or existing["owner_account_id"] != owner_account_id:
                    raise ValueError("property_identity_or_owner_conflict")
                self.db.execute("UPDATE world_properties SET value=?,rent=? WHERE property_id=?", (value, rent, property_id))
            else:
                self.db.execute("INSERT INTO world_properties(property_id,building_id,owner_account_id,value,rent) VALUES(?,?,?,?,?)",
                                (property_id, building_id, owner_account_id, value, rent))
        return {"property_id": property_id, "created": True}

    def purchase_property(self, transaction_id: str, property_id: str, buyer_account_id: str,
                          price: int) -> dict:
        if isinstance(price, bool) or not isinstance(price, int) or price <= 0:
            raise ValueError("invalid_purchase_price")
        with self.lock, self.db:
            row = self.db.execute("SELECT owner_account_id FROM world_properties WHERE property_id=?", (property_id,)).fetchone()
            if not row:
                raise KeyError("property_not_found")
            existing = self.db.execute(
                "SELECT source,description FROM world_transactions WHERE transaction_id=?", (transaction_id,)
            ).fetchone()
            if existing:
                prior = self.db.execute(
                    "SELECT account_id,amount FROM world_ledger_entries WHERE transaction_id=? ORDER BY entry_id",
                    (transaction_id,)
                ).fetchall()
                if (existing["source"] != "property_purchase" or existing["description"] != property_id
                        or len(prior) != 2 or prior[0]["account_id"] != buyer_account_id
                        or prior[0]["amount"] != -price or prior[1]["amount"] != price
                        or row["owner_account_id"] != buyer_account_id):
                    raise ValueError("transaction_id_conflict")
                return {"posted": True, "duplicate": True, "transaction_id": transaction_id}
            seller = row["owner_account_id"]
            if seller == buyer_account_id:
                raise ValueError("buyer_already_owns_property")
            result = self._post_transaction_locked(transaction_id, "property_purchase", property_id,
                [{"account_id": buyer_account_id, "amount": -price}, {"account_id": seller, "amount": price}])
            if not result["duplicate"]:
                self.db.execute("UPDATE world_properties SET owner_account_id=?,tenant_account_id=NULL,lease_until=0 WHERE property_id=?",
                                (buyer_account_id, property_id))
            return result

    def lease_property(self, transaction_id: str, property_id: str, tenant_account_id: str,
                       rent: int, lease_minutes: int) -> dict:
        if isinstance(rent, bool) or not isinstance(rent, int) or rent <= 0:
            raise ValueError("invalid_rent")
        if isinstance(lease_minutes, bool) or not isinstance(lease_minutes, int) or not 1 <= lease_minutes <= 525600:
            raise ValueError("invalid_lease_duration")
        with self.lock, self.db:
            row = self.db.execute("SELECT owner_account_id,tenant_account_id,lease_until FROM world_properties WHERE property_id=?", (property_id,)).fetchone()
            if not row:
                raise KeyError("property_not_found")
            if row["tenant_account_id"] and row["lease_until"] > int(self.minute_provider()) and row["tenant_account_id"] != tenant_account_id:
                raise ValueError("property_already_leased")
            result = self._post_transaction_locked(transaction_id, "property_rent", property_id,
                [{"account_id": tenant_account_id, "amount": -rent}, {"account_id": row["owner_account_id"], "amount": rent}])
            if not result["duplicate"]:
                self.db.execute("UPDATE world_properties SET tenant_account_id=?,lease_until=? WHERE property_id=?",
                                (tenant_account_id, int(self.minute_provider()) + lease_minutes, property_id))
            return result

    def create_business(self, business_id: str, property_id: str, owner_account_id: str,
                        cash_account_id: str, category: str) -> dict:
        business_id = _valid_id(business_id, "business_id")
        with self.lock, self.db:
            if not self.db.execute("SELECT 1 FROM world_properties WHERE property_id=?", (property_id,)).fetchone():
                raise KeyError("property_not_found")
            if not self.db.execute("SELECT 1 FROM world_accounts WHERE account_id=?", (owner_account_id,)).fetchone():
                raise KeyError("owner_account_not_found")
            if not self.db.execute("SELECT 1 FROM world_accounts WHERE account_id=?", (cash_account_id,)).fetchone():
                raise KeyError("cash_account_not_found")
            property_owner = self.db.execute("SELECT owner_account_id FROM world_properties WHERE property_id=?", (property_id,)).fetchone()
            if property_owner["owner_account_id"] != owner_account_id:
                raise ValueError("business_owner_must_own_property")
            existing = self.db.execute("SELECT property_id,owner_account_id,cash_account_id FROM world_businesses WHERE business_id=?", (business_id,)).fetchone()
            if existing:
                if (existing["property_id"], existing["owner_account_id"], existing["cash_account_id"]) != (property_id, owner_account_id, cash_account_id):
                    raise ValueError("business_identity_conflict")
                self.db.execute("UPDATE world_businesses SET category=? WHERE business_id=?", (category, business_id))
            else:
                self.db.execute("""INSERT INTO world_businesses(business_id,property_id,owner_account_id,cash_account_id,category,inventory_json,employees_json)
                    VALUES(?,?,?,?,?,'{}','[]')""", (business_id, property_id, owner_account_id, cash_account_id, category))
        return {"business_id": business_id, "created": True}

    def set_inventory(self, business_id: str, inventory: dict) -> dict:
        if not isinstance(inventory, dict) or len(inventory) > 256:
            raise ValueError("invalid_inventory")
        normalized = {}
        for item_id, qty in inventory.items():
            _valid_id(item_id, "item_id")
            if isinstance(qty, bool) or not isinstance(qty, int) or qty < 0:
                raise ValueError("invalid_inventory_quantity")
            normalized[item_id] = qty
        with self.lock, self.db:
            cur = self.db.execute("UPDATE world_businesses SET inventory_json=? WHERE business_id=?",
                                  (json.dumps(normalized), business_id))
            if cur.rowcount != 1:
                raise KeyError("business_not_found")
        return {"updated": True, "inventory": normalized}

    def sell_product(self, transaction_id: str, business_id: str, buyer_account_id: str,
                     item_id: str, quantity: int, unit_price: int) -> dict:
        if isinstance(quantity, bool) or not isinstance(quantity, int) or not 1 <= quantity <= 10000:
            raise ValueError("invalid_sale_quantity")
        if isinstance(unit_price, bool) or not isinstance(unit_price, int) or unit_price < 0:
            raise ValueError("invalid_unit_price")
        with self.lock, self.db:
            row = self.db.execute("SELECT cash_account_id,inventory_json,is_open FROM world_businesses WHERE business_id=?",
                                  (business_id,)).fetchone()
            if not row:
                raise KeyError("business_not_found")
            if not row["is_open"]:
                raise ValueError("business_closed")
            inventory = json.loads(row["inventory_json"])
            if inventory.get(item_id, 0) < quantity:
                raise ValueError("insufficient_inventory")
            total = quantity * unit_price
            result = self._post_transaction_locked(transaction_id, "retail_sale", business_id,
                [{"account_id": buyer_account_id, "amount": -total},
                 {"account_id": row["cash_account_id"], "amount": total}])
            if not result["duplicate"]:
                inventory[item_id] -= quantity
                self.db.execute("UPDATE world_businesses SET inventory_json=? WHERE business_id=?",
                                (json.dumps(inventory), business_id))
            return result

    def register_traffic_segment(self, segment_id: str, road_class: str,
                                 capacity: float = 1.0) -> dict:
        segment_id = _valid_id(segment_id, "segment_id")
        if not isinstance(road_class, str) or not road_class or not 0.1 <= float(capacity) <= 10000:
            raise ValueError("invalid_traffic_segment")
        with self.lock, self.db:
            self.db.execute("""INSERT INTO world_traffic_segments(segment_id,road_class,capacity,updated_minute)
                VALUES(?,?,?,?) ON CONFLICT(segment_id) DO UPDATE SET road_class=excluded.road_class,
                capacity=excluded.capacity""", (segment_id, road_class, float(capacity), int(self.minute_provider())))
        return {"segment_id": segment_id, "registered": True}

    def record_traffic_incident(self, incident_id: str, segment_id: str, severity: int,
                                kind: str, details: str = "") -> dict:
        incident_id = _valid_id(incident_id, "incident_id")
        if isinstance(severity, bool) or not isinstance(severity, int) or not 1 <= severity <= 5:
            raise ValueError("invalid_incident_severity")
        if not isinstance(kind, str) or not kind or len(kind) > 64 or len(details) > MAX_TEXT:
            raise ValueError("invalid_traffic_incident")
        with self.lock, self.db:
            existing = self.db.execute("SELECT 1 FROM world_traffic_incidents WHERE incident_id=?", (incident_id,)).fetchone()
            if existing:
                return {"recorded": True, "duplicate": True, "incident_id": incident_id}
            row = self.db.execute("SELECT congestion,capacity FROM world_traffic_segments WHERE segment_id=?", (segment_id,)).fetchone()
            if not row:
                raise KeyError("traffic_segment_not_found")
            congestion = min(100.0, row["congestion"] + severity * 12.0 / max(0.1, row["capacity"]))
            minute = int(self.minute_provider())
            self.db.execute("UPDATE world_traffic_segments SET congestion=?,updated_minute=? WHERE segment_id=?",
                            (congestion, minute, segment_id))
            self.db.execute("INSERT INTO world_traffic_incidents(incident_id,segment_id,severity,kind,details,sim_minute) VALUES(?,?,?,?,?,?)",
                            (incident_id, segment_id, severity, kind, details[:MAX_TEXT], minute))
            self.db.execute("INSERT INTO world_event_journal(event_id,event_type,target_id,payload_json,sim_minute) VALUES(?,?,?,?,?)",
                            (incident_id, "traffic.incident", segment_id,
                             json.dumps({"severity": severity, "kind": kind, "details": details[:MAX_TEXT],
                                         "congestion": congestion}), minute))
            affected = 0
            if self.life_engine is not None:
                for npc in self.life_engine.npcs.values():
                    if npc.location_id == segment_id:
                        npc.stress = self.life_engine._clamp(npc.stress + severity * 2.0)
                        affected += 1
                if affected:
                    self.life_engine.store.save(self.life_engine.simulation_minute,
                        list(self.life_engine.npcs.values()), [])
            return {"recorded": True, "duplicate": False, "incident_id": incident_id,
                    "congestion": congestion, "affected_npcs": affected}

    def ingest_legacy_events(self, source_id: str, events: list) -> dict:
        """Idempotently import the protected legacy GVL event stream into the canonical journal."""
        source_id = _valid_id(source_id, "source_id")
        if not isinstance(events, list) or len(events) > 100:
            raise ValueError("invalid_event_batch")
        accepted = 0
        duplicates = 0
        with self.lock, self.db:
            self.db.execute("INSERT OR IGNORE INTO world_accounts(account_id,owner_id,balance,allow_negative,currency) VALUES('legacy:player_cash','player.main',0,1,'USD')")
            self.db.execute("INSERT OR IGNORE INTO world_accounts(account_id,owner_id,balance,allow_negative,currency) VALUES('legacy:player_bank','player.main',0,1,'USD')")
            self.db.execute("INSERT OR IGNORE INTO world_accounts(account_id,owner_id,balance,allow_negative,currency) VALUES('system:legacy_counterparty','system',0,1,'USD')")
            for event in events:
                if not isinstance(event, dict):
                    raise ValueError("invalid_legacy_event")
                seq = event.get("sequence")
                if isinstance(seq, bool) or not isinstance(seq, int) or seq < 0:
                    raise ValueError("invalid_event_sequence")
                event_type = event.get("event_type")
                if not isinstance(event_type, str) or not event_type or len(event_type) > 96:
                    raise ValueError("invalid_event_type")
                raw = event.get("data", "")
                if not isinstance(raw, str) or len(raw) > 4000:
                    raise ValueError("invalid_event_payload")
                event_id = source_id + ":" + str(seq)
                payload = {"sequence": seq, "data": raw, "source": source_id}
                cur = self.db.execute("""INSERT OR IGNORE INTO world_event_journal
                    (event_id,event_type,actor_id,payload_json,sim_minute) VALUES(?,?,?,?,?)""",
                    (event_id, event_type, "player.main", json.dumps(payload, ensure_ascii=False),
                     int(event.get("sim_minute", self.minute_provider()))))
                if cur.rowcount == 0:
                    duplicates += 1
                    continue
                accepted += 1
                if event_type in ("ledger.income", "ledger.expense"):
                    fields = {}
                    for item in raw.split(";"):
                        if "=" in item:
                            key, value = item.split("=", 1)
                            fields[key.strip()] = value.strip()
                    try:
                        amount = int(fields.get("amount", "0"))
                    except ValueError:
                        amount = 0
                    if amount > 0:
                        sign = amount if event_type == "ledger.income" else -amount
                        tx_id = "legacy-ledger:" + source_id + ":" + str(seq)
                        account_id = "legacy:player_bank" if fields.get("acct", "cash") == "bank" else "legacy:player_cash"
                        self._post_transaction_locked(tx_id, fields.get("cat", "legacy"),
                            fields.get("note", event_type), [
                                {"account_id": account_id, "amount": sign},
                                {"account_id": "system:legacy_counterparty", "amount": -sign}])
        return {"accepted": accepted, "duplicates": duplicates, "source": source_id}

    def advance(self, minutes: int) -> None:
        """Apply bounded time-based decay and lease expiry using the canonical simulation clock."""
        if isinstance(minutes, bool) or not isinstance(minutes, int) or minutes < 0 or minutes > 10080:
            raise ValueError("invalid_world_advance")
        if minutes == 0:
            return
        now = int(self.minute_provider())
        with self.lock, self.db:
            self.db.execute("UPDATE world_traffic_segments SET congestion=MAX(0,congestion-?),updated_minute=?",
                            (minutes * 0.5, now))
            self.db.execute("UPDATE world_properties SET tenant_account_id=NULL,lease_until=0 "
                            "WHERE tenant_account_id IS NOT NULL AND lease_until>0 AND lease_until<=?",
                            (now,))

    def snapshot(self) -> dict:
        with self.lock:
            buildings = [dict(r) for r in self.db.execute("SELECT building_id,category,name,revision FROM world_buildings ORDER BY building_id")]
            properties = [dict(r) for r in self.db.execute("SELECT property_id,building_id,owner_account_id,value,rent,tenant_account_id,lease_until,condition FROM world_properties ORDER BY property_id")]
            businesses = []
            for r in self.db.execute("SELECT * FROM world_businesses ORDER BY business_id"):
                item = dict(r)
                item["inventory"] = json.loads(item.pop("inventory_json"))
                item["employees"] = json.loads(item.pop("employees_json"))
                businesses.append(item)
            objects = []
            for r in self.db.execute("SELECT * FROM world_objects ORDER BY object_id"):
                item = dict(r)
                item["state"] = json.loads(item.pop("state_json"))
                item.pop("adapter_anchors_json", None)
                objects.append(item)
            traffic = [dict(r) for r in self.db.execute("SELECT * FROM world_traffic_segments ORDER BY segment_id")]
            return {"buildings": buildings, "objects": objects, "properties": properties, "businesses": businesses, "traffic": traffic}
