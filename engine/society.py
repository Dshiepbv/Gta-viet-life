"""Persistent game-neutral world, interaction, property, business, ledger and traffic systems.

The base game adapter supplies semantic-to-native mappings and executes world commands.
This module owns canonical facts and consequences; it never imports a game SDK.
"""
from __future__ import annotations

import hashlib
import json
import logging
import sqlite3
import threading
import time
import uuid
from typing import Any, Callable, Dict, Optional


LOG = logging.getLogger("gvl.engine")
MAX_ID = 128
MAX_TEXT = 500
SYNC_RETENTION_SECONDS = 3600
PRUNE_EVERY_SYNCS = 200
MAX_AMOUNT = 10 ** 15
CURRENCIES = ("USD", "VND")
# Transaction ids with these prefixes are created by the engine itself.
SYSTEM_PREFIXES = ("opening:", "payroll:", "reconcile:", "bsync:", "legacy-ledger:")
RESERVED_PREFIXES = SYSTEM_PREFIXES + ("lease:", "sale:")


def _system_account(name: str, currency: str) -> str:
    return "system:" + name if currency == "USD" else "system:%s:%s" % (name, currency)


def _public_transaction_id(value: Any, own_prefix: str = "") -> str:
    value = _valid_id(value, "transaction_id")
    for prefix in RESERVED_PREFIXES:
        if value.startswith(prefix) and prefix != own_prefix:
            raise ValueError("reserved_transaction_id")
    return value


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
        self._syncs_since_prune = 0
        self._migrate()
        self.prune_sync_history()
        for mismatch in self.verify_ledger():
            LOG.warning("ledger balance mismatch: %s", mismatch)

    def _migrate(self):
        with self.lock, self.db:
            self.db.executescript("""
                BEGIN IMMEDIATE;
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
                    is_open INTEGER NOT NULL DEFAULT 1, native_cash_balance INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY(property_id) REFERENCES world_properties(property_id));
                CREATE TABLE IF NOT EXISTS world_employment_contracts(
                    contract_id TEXT PRIMARY KEY, employee_id TEXT NOT NULL, employee_account_id TEXT NOT NULL,
                    business_id TEXT NOT NULL, hourly_wage INTEGER NOT NULL,
                    status TEXT NOT NULL DEFAULT 'active', started_minute INTEGER NOT NULL,
                    FOREIGN KEY(employee_account_id) REFERENCES world_accounts(account_id),
                    FOREIGN KEY(business_id) REFERENCES world_businesses(business_id));
                CREATE TABLE IF NOT EXISTS world_payroll_accruals(
                    accrual_id TEXT PRIMARY KEY, contract_id TEXT NOT NULL, employee_id TEXT NOT NULL,
                    employee_account_id TEXT NOT NULL, cash_account_id TEXT NOT NULL,
                    amount INTEGER NOT NULL, status TEXT NOT NULL DEFAULT 'pending',
                    created_minute INTEGER NOT NULL, paid_transaction_id TEXT);
                CREATE INDEX IF NOT EXISTS idx_payroll_pending ON world_payroll_accruals(status,created_minute);
                CREATE TABLE IF NOT EXISTS world_social_relationships(
                    actor_id TEXT NOT NULL, target_id TEXT NOT NULL, relation_type TEXT NOT NULL,
                    strength REAL NOT NULL DEFAULT 0, status TEXT NOT NULL DEFAULT 'active',
                    updated_minute INTEGER NOT NULL, PRIMARY KEY(actor_id,target_id));
                CREATE INDEX IF NOT EXISTS idx_employment_employee ON world_employment_contracts(employee_id,status);
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
                CREATE TABLE IF NOT EXISTS world_processed_life_events(
                    event_id TEXT PRIMARY KEY, processed_minute INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS world_account_openings(
                    account_id TEXT PRIMARY KEY, source_id TEXT NOT NULL, sequence INTEGER NOT NULL,
                    balance INTEGER NOT NULL);
            """)
            for name, action in (("insert", "INSERT"), ("update", "UPDATE OF balance")):
                self.db.execute("""CREATE TRIGGER IF NOT EXISTS trg_world_balance_%s
                    BEFORE %s ON world_accounts
                    WHEN typeof(NEW.balance)<>'integer' OR NEW.balance>%d OR NEW.balance<%d
                    BEGIN SELECT RAISE(ABORT,'balance_out_of_range'); END"""
                    % (name, action, MAX_AMOUNT, -MAX_AMOUNT))
            for table, column, kind in (("world_request_dedup", "kind", "TEXT NOT NULL DEFAULT ''"),
                                        ("world_request_dedup", "created_at", "INTEGER NOT NULL DEFAULT 0"),
                                        ("world_event_journal", "recorded_at", "INTEGER NOT NULL DEFAULT 0")):
                if column not in {row["name"] for row in self.db.execute("PRAGMA table_info(%s)" % table)}:
                    self.db.execute("ALTER TABLE %s ADD COLUMN %s %s" % (table, column, kind))
            business_columns = {row["name"] for row in self.db.execute("PRAGMA table_info(world_businesses)")}
            if "native_cash_balance" not in business_columns:
                self.db.execute("ALTER TABLE world_businesses ADD COLUMN native_cash_balance INTEGER NOT NULL DEFAULT 0")
                self.db.execute("""UPDATE world_businesses
                    SET native_cash_balance=COALESCE((SELECT balance FROM world_accounts
                    WHERE account_id=world_businesses.cash_account_id),0)""")
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
        physical_effect = None
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
                object_row = self.db.execute("SELECT state_json,kind FROM world_objects WHERE object_id=?", (target_id,)).fetchone()
                if object_row:
                    state = json.loads(object_row["state_json"])
                    state["uses"] = int(state.get("uses", 0)) + 1
                    state["last_used_minute"] = int(self.minute_provider())
                    state["last_actor_id"] = payload.get("actor_id", "")
                    self.db.execute("UPDATE world_objects SET state_json=? WHERE object_id=?",
                                    (json.dumps(state), target_id))
                    physical_effect = (payload.get("actor_id", ""), object_row["kind"])
        if physical_effect and self.life_engine is not None:
            self.life_engine.apply_physical_interaction(*physical_effect)
        return cur.rowcount == 1

    def create_account(self, account_id: str, owner_id: str, opening_balance: int = 0,
                       allow_negative: bool = False, currency: str = "USD") -> dict:
        account_id = _valid_id(account_id, "account_id")
        owner_id = _valid_id(owner_id, "owner_id")
        if isinstance(opening_balance, bool) or not isinstance(opening_balance, int) or opening_balance < 0:
            raise ValueError("invalid_opening_balance")
        if not isinstance(allow_negative, bool) or currency not in CURRENCIES:
            raise ValueError("invalid_account_policy")
        if opening_balance > MAX_AMOUNT:
            raise ValueError("amount_out_of_range")
        with self.lock, self.db:
            if self.db.execute("SELECT 1 FROM world_accounts WHERE account_id=?", (account_id,)).fetchone():
                raise ValueError("account_already_exists")
            self.db.execute("INSERT INTO world_accounts(account_id,owner_id,balance,allow_negative,currency) VALUES(?,?,0,?,?)",
                            (account_id, owner_id, 1 if allow_negative else 0, currency))
            if opening_balance:
                self._post_opening_locked(account_id, currency, opening_balance)
        return self.account(account_id)

    def _ensure_system_account_locked(self, name: str, currency: str) -> str:
        account_id = _system_account(name, currency)
        self.db.execute("""INSERT OR IGNORE INTO world_accounts(account_id,owner_id,balance,allow_negative,currency)
            VALUES(?,'system',0,1,?)""", (account_id, currency))
        return account_id

    def _post_opening_locked(self, account_id: str, currency: str, amount: int) -> None:
        equity = self._ensure_system_account_locked("opening_equity", currency)
        self._post_transaction_locked("opening:" + account_id, "opening", "Opening balance",
            [{"account_id": account_id, "amount": amount}, {"account_id": equity, "amount": -amount}],
            enforce_funds=False)

    def _existing_transaction_locked(self, transaction_id: str, source: str, entries: list,
                                     descriptions: tuple) -> bool:
        """True for an exact replay; the same id carrying different content is refused."""
        row = self.db.execute("SELECT source,description FROM world_transactions WHERE transaction_id=?",
                              (transaction_id,)).fetchone()
        if not row:
            return False
        stored = sorted((r["account_id"], r["amount"]) for r in self.db.execute(
            "SELECT account_id,amount FROM world_ledger_entries WHERE transaction_id=?", (transaction_id,)))
        if row["source"] != source or row["description"] not in descriptions or stored != sorted(entries):
            raise ValueError("transaction_id_conflict")
        return True

    def _post_transaction_locked(self, transaction_id: str, source: str, description: str, entries: list,
                                 enforce_funds: bool = True) -> dict:
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
            if abs(entry["amount"]) > MAX_AMOUNT:
                raise ValueError("amount_out_of_range")
            total += entry["amount"]
            normalized.append((account_id, entry["amount"]))
        if total != 0:
            raise ValueError("unbalanced_transaction")
        if self._existing_transaction_locked(transaction_id, source, normalized, (description,)):
            return {"posted": True, "duplicate": True, "transaction_id": transaction_id}
        balances = {}
        policies = {}
        currencies = set()
        for account_id, amount in normalized:
            row = self.db.execute("SELECT balance,allow_negative,currency FROM world_accounts WHERE account_id=?",
                                  (account_id,)).fetchone()
            if not row:
                raise KeyError("account_not_found:" + account_id)
            currencies.add(row["currency"])
            policies[account_id] = row["allow_negative"]
            balances[account_id] = balances.get(account_id, row["balance"]) + amount
        if len(currencies) != 1:
            raise ValueError("currency_mismatch")
        for account_id, balance in balances.items():
            if abs(balance) > MAX_AMOUNT:
                raise ValueError("amount_out_of_range")
            if enforce_funds and not policies[account_id] and balance < 0:
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
        transaction_id = _public_transaction_id(transaction_id)
        with self.lock, self.db:
            return self._post_transaction_locked(transaction_id, source, description, entries)

    def ensure_account(self, account_id: str, owner_id: str, opening_balance: int = 0,
                       allow_negative: bool = True, currency: str = "USD",
                       as_of_source: str = "", as_of_sequence: Optional[int] = None) -> dict:
        """Create an opening balance once; repeated adapter sync never resets a live ledger.

        With as_of_sequence the adapter states which of its events are already part of the balance.
        Those events never move money again, and later ones that arrived first are kept on top.
        """
        account_id = _valid_id(account_id, "account_id")
        owner_id = _valid_id(owner_id, "owner_id")
        if as_of_sequence is not None:
            as_of_source = _valid_id(as_of_source, "source_id")
            if isinstance(as_of_sequence, bool) or not isinstance(as_of_sequence, int) or as_of_sequence < 0:
                raise ValueError("invalid_event_sequence")
        if isinstance(opening_balance, bool) or not isinstance(opening_balance, int) or opening_balance < 0:
            raise ValueError("invalid_opening_balance")
        if opening_balance > MAX_AMOUNT:
            raise ValueError("amount_out_of_range")
        if not isinstance(allow_negative, bool) or currency not in CURRENCIES:
            raise ValueError("invalid_account_policy")
        with self.lock, self.db:
            self.db.execute("""INSERT OR IGNORE INTO world_accounts
                (account_id,owner_id,balance,allow_negative,currency) VALUES(?,?,0,?,?)""",
                (account_id, owner_id, 1 if allow_negative else 0, currency))
            self._open_account_locked(account_id, opening_balance, as_of_source, as_of_sequence)
        return self.account(account_id)

    def _open_account_locked(self, account_id: str, opening_balance: int, source_id: str,
                             sequence: Optional[int]) -> None:
        if self.db.execute("""SELECT 1 FROM world_transactions WHERE transaction_id=?
                UNION SELECT 1 FROM world_account_openings WHERE account_id=?""",
                ("opening:" + account_id, account_id)).fetchone():
            return
        row = self.db.execute("SELECT balance,currency FROM world_accounts WHERE account_id=?",
                              (account_id,)).fetchone()
        if sequence is None:
            untouched = not self.db.execute("SELECT 1 FROM world_ledger_entries WHERE account_id=?",
                                            (account_id,)).fetchone()
            if untouched and row["balance"] == 0 and opening_balance > 0:
                self._post_opening_locked(account_id, row["currency"], opening_balance)
            return
        # A wallet observation is newer evidence than any opening snapshot.
        reconciled = self.db.execute("""SELECT 1 FROM world_ledger_entries e
            JOIN world_transactions t USING(transaction_id)
            WHERE e.account_id=? AND t.source='reconcile'""", (account_id,)).fetchone()
        if not reconciled:
            prefix = "legacy-ledger:" + source_id + ":"
            later = 0
            for entry in self.db.execute("""SELECT transaction_id,amount FROM world_ledger_entries
                    WHERE account_id=? AND substr(transaction_id,1,?)=?""", (account_id, len(prefix), prefix)):
                # "gta5.shvdn" is also a prefix of newer sources such as "gta5.shvdn:<id>".
                number = entry["transaction_id"][len(prefix):]
                if number.isdigit() and int(number) > sequence:
                    later += entry["amount"]
            difference = opening_balance + later - row["balance"]
            if difference:
                self._post_opening_locked(account_id, row["currency"], difference)
        self.db.execute("INSERT INTO world_account_openings(account_id,source_id,sequence,balance) VALUES(?,?,?,?)",
                        (account_id, source_id, sequence, opening_balance))

    def verify_ledger(self) -> list:
        """Accounts whose stored balance differs from the sum of their ledger entries."""
        with self.lock:
            return [dict(row) for row in self.db.execute("""SELECT a.account_id, a.balance,
                COALESCE(SUM(e.amount),0) AS entries FROM world_accounts a
                LEFT JOIN world_ledger_entries e USING(account_id)
                GROUP BY a.account_id HAVING a.balance<>COALESCE(SUM(e.amount),0)
                ORDER BY a.account_id""")]

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
        transaction_id = _public_transaction_id(transaction_id)
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
                        or prior[1]["account_id"] == buyer_account_id):
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
        transaction_id = _public_transaction_id(transaction_id, "lease:")
        if isinstance(rent, bool) or not isinstance(rent, int) or rent <= 0:
            raise ValueError("invalid_rent")
        if isinstance(lease_minutes, bool) or not isinstance(lease_minutes, int) or not 1 <= lease_minutes <= 525600:
            raise ValueError("invalid_lease_duration")
        with self.lock, self.db:
            row = self.db.execute("SELECT owner_account_id,tenant_account_id,lease_until FROM world_properties WHERE property_id=?", (property_id,)).fetchone()
            if not row:
                raise KeyError("property_not_found")
            payee = self.db.execute("""SELECT account_id FROM world_ledger_entries
                WHERE transaction_id=? AND account_id<>? LIMIT 1""", (transaction_id, tenant_account_id)).fetchone()
            # Replays are answered first; ownership or the tenant may have changed since.
            if self._existing_transaction_locked(transaction_id, "property_rent",
                    [(tenant_account_id, -rent), (payee["account_id"] if payee else "", rent)], (property_id,)):
                return {"posted": True, "duplicate": True, "transaction_id": transaction_id}
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
                cash_balance = int(self.db.execute("SELECT balance FROM world_accounts WHERE account_id=?", (cash_account_id,)).fetchone()["balance"])
                self.db.execute("""INSERT INTO world_businesses
                    (business_id,property_id,owner_account_id,cash_account_id,category,inventory_json,employees_json,native_cash_balance)
                    VALUES(?,?,?,?,?,'{}','[]',?)""",
                    (business_id, property_id, owner_account_id, cash_account_id, category, cash_balance))
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

    def sync_business_state(self, request_id: str, business_id: str, adapter_id: str,
                            cash_balance: int, inventory: dict, is_open: bool = True) -> dict:
        """Reconcile a native game's authoritative shop snapshot into the canonical business ledger."""
        request_id = _valid_id(request_id, "request_id")
        if len(request_id) > 100:
            raise ValueError("invalid_request_id")
        business_id = _valid_id(business_id, "business_id")
        adapter_id = _valid_id(adapter_id, "adapter_id")
        if isinstance(cash_balance, bool) or not isinstance(cash_balance, int) or cash_balance < 0:
            raise ValueError("invalid_business_cash_balance")
        if not isinstance(is_open, bool):
            raise ValueError("invalid_business_open_state")
        if not isinstance(inventory, dict) or len(inventory) > 256:
            raise ValueError("invalid_inventory")
        normalized = {}
        for item_id, quantity in inventory.items():
            _valid_id(item_id, "item_id")
            if isinstance(quantity, bool) or not isinstance(quantity, int) or quantity < 0:
                raise ValueError("invalid_inventory_quantity")
            normalized[item_id] = quantity
        with self.lock, self.db:
            cached = self.db.execute(
                "SELECT response_json FROM world_request_dedup WHERE request_id=?", (request_id,)
            ).fetchone()
            if cached:
                return json.loads(cached["response_json"])
            row = self.db.execute(
                """SELECT b.cash_account_id,b.native_cash_balance,p.building_id,bu.adapter_maps_json
                FROM world_businesses b
                JOIN world_properties p ON p.property_id=b.property_id
                JOIN world_buildings bu ON bu.building_id=p.building_id
                WHERE b.business_id=?""", (business_id,)
            ).fetchone()
            if not row:
                raise KeyError("business_not_found")
            adapter_maps = json.loads(row["adapter_maps_json"])
            if adapter_id not in adapter_maps:
                raise ValueError("adapter_mapping_unavailable")
            cash_account_id = row["cash_account_id"]
            account = self.db.execute(
                "SELECT balance,currency FROM world_accounts WHERE account_id=?", (cash_account_id,)
            ).fetchone()
            if not account:
                raise KeyError("business_cash_account_not_found")
            delta = cash_balance - int(row["native_cash_balance"])
            equity_id = self._ensure_system_account_locked("business_sync_equity", account["currency"])
            if delta:
                transaction_id = "bsync:" + hashlib.sha256(request_id.encode("utf-8")).hexdigest()[:48]
                self._post_transaction_locked(
                    transaction_id, "business_snapshot_sync", business_id,
                    [{"account_id": cash_account_id, "amount": delta},
                     {"account_id": equity_id, "amount": -delta}],
                )
            self.db.execute(
                "UPDATE world_businesses SET inventory_json=?,is_open=?,native_cash_balance=? WHERE business_id=?",
                (json.dumps(normalized, ensure_ascii=False, sort_keys=True), int(is_open), cash_balance, business_id),
            )
            payroll_settled = self._settle_pending_payroll_locked()
            result = {"synced": True, "business_id": business_id, "cash_balance": cash_balance,
                      "cash_delta": delta, "inventory": normalized, "is_open": is_open,
                      "payroll_settled": payroll_settled}
            self.db.execute(
                "INSERT INTO world_request_dedup(request_id,response_json,kind,created_at) VALUES(?,?,'sync',?)",
                (request_id, json.dumps(result, ensure_ascii=False), int(time.time())),
            )
            self.db.execute(
                """INSERT INTO world_event_journal
                (event_id,event_type,target_id,payload_json,sim_minute,recorded_at) VALUES(?,?,?,?,?,?)""",
                (uuid.uuid4().hex, "business.state_synchronized", business_id,
                 json.dumps(result, ensure_ascii=False), int(self.minute_provider()), int(time.time())),
            )
        self._syncs_since_prune += 1
        if self._syncs_since_prune >= PRUNE_EVERY_SYNCS:
            self._syncs_since_prune = 0
            self.prune_sync_history()
        return result

    def sell_product(self, transaction_id: str, business_id: str, buyer_account_id: str,
                     item_id: str, quantity: int, unit_price: int) -> dict:
        if isinstance(quantity, bool) or not isinstance(quantity, int) or not 1 <= quantity <= 10000:
            raise ValueError("invalid_sale_quantity")
        if isinstance(unit_price, bool) or not isinstance(unit_price, int) or unit_price < 0:
            raise ValueError("invalid_unit_price")
        transaction_id = _public_transaction_id(transaction_id, "sale:")
        item_id = _valid_id(item_id, "item_id")
        description = "%s|%s|%d" % (business_id, item_id, quantity)
        with self.lock, self.db:
            row = self.db.execute("SELECT cash_account_id,inventory_json,is_open FROM world_businesses WHERE business_id=?",
                                  (business_id,)).fetchone()
            if not row:
                raise KeyError("business_not_found")
            total = quantity * unit_price
            # Replays are answered before stock checks: the first sale may have emptied the shelf.
            # Sales posted by earlier builds stored only the business id as description.
            if self._existing_transaction_locked(transaction_id, "retail_sale",
                    [(buyer_account_id, -total), (row["cash_account_id"], total)], (description, business_id)):
                return {"posted": True, "duplicate": True, "transaction_id": transaction_id}
            if not row["is_open"]:
                raise ValueError("business_closed")
            inventory = json.loads(row["inventory_json"])
            if inventory.get(item_id, 0) < quantity:
                raise ValueError("insufficient_inventory")
            result = self._post_transaction_locked(transaction_id, "retail_sale", description,
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
        rejected = []
        with self.lock, self.db:
            self.db.execute("INSERT OR IGNORE INTO world_accounts(account_id,owner_id,balance,allow_negative,currency) VALUES('legacy:player_cash','player.main',0,1,'USD')")
            self.db.execute("INSERT OR IGNORE INTO world_accounts(account_id,owner_id,balance,allow_negative,currency) VALUES('legacy:player_bank','player.main',0,1,'USD')")
            self.db.execute("INSERT OR IGNORE INTO world_accounts(account_id,owner_id,balance,allow_negative,currency) VALUES('system:legacy_counterparty','system',0,1,'USD')")
            for event in events:
                self.db.execute("SAVEPOINT legacy_event")
                try:
                    imported = self._import_legacy_event_locked(source_id, event)
                except (ValueError, TypeError, KeyError, OverflowError, sqlite3.IntegrityError) as exc:
                    # One malformed event is reported back instead of blocking every event behind it.
                    self.db.execute("ROLLBACK TO legacy_event")
                    self.db.execute("RELEASE legacy_event")
                    sequence = event.get("sequence") if isinstance(event, dict) else None
                    rejected.append({"sequence": sequence, "error": str(exc).strip("'")})
                    continue
                self.db.execute("RELEASE legacy_event")
                if imported:
                    accepted += 1
                else:
                    duplicates += 1
        return {"accepted": accepted, "duplicates": duplicates, "rejected": rejected, "source": source_id}

    def _import_legacy_event_locked(self, source_id: str, event: dict) -> bool:
        """Journal one legacy event and its ledger effect; False means it was already imported."""
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
            return False
        if event_type == "wallet.observed":
            self._reconcile_wallet_locked(source_id, seq, raw)
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
                if self.db.execute(
                        "SELECT 1 FROM world_account_openings WHERE account_id=? AND source_id=? AND sequence>=?",
                        (account_id, source_id, seq)).fetchone():
                    return True   # already part of the opening balance
                self._post_transaction_locked(tx_id, fields.get("cat", "legacy"),
                    fields.get("note", event_type), [
                        {"account_id": account_id, "amount": sign},
                        {"account_id": "system:legacy_counterparty", "amount": -sign}])
        return True

    def _reconcile_wallet_locked(self, source_id: str, seq: int, raw: str) -> None:
        """The in-game wallet owns cash: bring the ledger to the observed balance with a reasoned entry."""
        fields = dict(item.split("=", 1) for item in raw.split(";") if "=" in item)
        balance = int(fields.get("balance", ""))
        if not 0 <= balance <= 2_147_483_647:
            raise ValueError("invalid_wallet_balance")
        self.db.execute("INSERT OR IGNORE INTO world_accounts(account_id,owner_id,balance,allow_negative,currency) VALUES('system:wallet_reconcile','system',0,1,'USD')")
        current = self.db.execute(
            "SELECT balance FROM world_accounts WHERE account_id='legacy:player_cash'").fetchone()["balance"]
        difference = balance - int(current)
        if difference:
            self._post_transaction_locked("reconcile:" + source_id + ":" + str(seq), "reconcile",
                "wallet observed " + fields.get("char", "?")[:16], [
                    {"account_id": "legacy:player_cash", "amount": difference},
                    {"account_id": "system:wallet_reconcile", "amount": -difference}])

    def prune_sync_history(self, now: Optional[float] = None) -> int:
        """Drop replay receipts and telemetry of periodic business snapshots older than the retention window."""
        cutoff = int((time.time() if now is None else now) - SYNC_RETENTION_SECONDS)
        with self.lock, self.db:
            removed = self.db.execute(
                "DELETE FROM world_request_dedup WHERE kind='sync' AND created_at<?", (cutoff,)).rowcount
            self.db.execute("""DELETE FROM world_event_journal
                WHERE event_type='business.state_synchronized' AND recorded_at<?""", (cutoff,))
        return removed

    def set_social_relationship(self, actor_id: str, target_id: str,
                                relation_type: str, strength: float = 0.0,
                                status: str = "active") -> dict:
        actor_id = _valid_id(actor_id, "actor_id")
        target_id = _valid_id(target_id, "target_id")
        if actor_id == target_id:
            raise ValueError("self_relationship_not_allowed")
        allowed_types = ("parent", "child", "sibling", "spouse", "romantic_interest",
                         "friend", "coworker", "neighbor", "acquaintance", "rival")
        if relation_type not in allowed_types or status not in ("active", "estranged", "separated", "ended"):
            raise ValueError("invalid_social_relationship")
        if isinstance(strength, bool) or not isinstance(strength, (int, float)) or not -100 <= strength <= 100:
            raise ValueError("invalid_relationship_strength")
        if self.life_engine is not None and (actor_id not in self.life_engine.npcs or target_id not in self.life_engine.npcs):
            raise KeyError("relationship_actor_not_found")
        now = int(self.minute_provider())
        with self.lock, self.db:
            self.db.execute("""INSERT INTO world_social_relationships
                (actor_id,target_id,relation_type,strength,status,updated_minute) VALUES(?,?,?,?,?,?)
                ON CONFLICT(actor_id,target_id) DO UPDATE SET relation_type=excluded.relation_type,
                strength=excluded.strength,status=excluded.status,updated_minute=excluded.updated_minute""",
                (actor_id,target_id,relation_type,float(strength),status,now))
            self.db.execute("""INSERT INTO world_event_journal
                (event_id,event_type,actor_id,target_id,payload_json,sim_minute) VALUES(?,?,?,?,?,?)""",
                (uuid.uuid4().hex,"social.relationship_changed",actor_id,target_id,
                 json.dumps({"relation_type":relation_type,"strength":strength,"status":status}),now))
        if self.life_engine is not None:
            with self.life_engine._lock:
                left = self.life_engine.npcs[actor_id]
                right = self.life_engine.npcs[target_id]
                left.affinity[target_id] = float(strength)
                right.affinity[actor_id] = float(strength)
                self.life_engine.store.save(self.life_engine.simulation_minute,[left,right],[])
        return {"actor_id":actor_id,"target_id":target_id,"relation_type":relation_type,
                "strength":float(strength),"status":status,"updated_minute":now}

    def create_employment_contract(self, contract_id: str, employee_id: str,
                                   employee_account_id: str, business_id: str,
                                   hourly_wage: int) -> dict:
        contract_id = _valid_id(contract_id, "contract_id")
        employee_id = _valid_id(employee_id, "employee_id")
        employee_account_id = _valid_id(employee_account_id, "employee_account_id")
        business_id = _valid_id(business_id, "business_id")
        if isinstance(hourly_wage, bool) or not isinstance(hourly_wage, int) or hourly_wage < 0:
            raise ValueError("invalid_hourly_wage")
        with self.lock, self.db:
            if not self.db.execute("SELECT 1 FROM world_businesses WHERE business_id=?", (business_id,)).fetchone():
                raise KeyError("business_not_found")
            if not self.db.execute("SELECT 1 FROM world_accounts WHERE account_id=?", (employee_account_id,)).fetchone():
                raise KeyError("employee_account_not_found")
            old = self.db.execute("SELECT employee_id,employee_account_id,business_id,hourly_wage FROM world_employment_contracts WHERE contract_id=?",
                                  (contract_id,)).fetchone()
            identity = (employee_id, employee_account_id, business_id, hourly_wage)
            if old:
                if (old["employee_id"], old["employee_account_id"], old["business_id"], old["hourly_wage"]) != identity:
                    raise ValueError("employment_contract_identity_conflict")
            else:
                self.db.execute("""INSERT INTO world_employment_contracts
                    (contract_id,employee_id,employee_account_id,business_id,hourly_wage,started_minute)
                    VALUES(?,?,?,?,?,?)""",
                    (contract_id, employee_id, employee_account_id, business_id, hourly_wage,
                     int(self.minute_provider())))
        return {"contract_id": contract_id, "created": True}

    def _settle_pending_payroll_locked(self) -> list:
        settled = []
        rows = self.db.execute(
            "SELECT * FROM world_payroll_accruals WHERE status='pending' ORDER BY created_minute,accrual_id"
        ).fetchall()
        for accrual in rows:
            cash = self.db.execute(
                "SELECT balance,allow_negative FROM world_accounts WHERE account_id=?",
                (accrual["cash_account_id"],),
            ).fetchone()
            if not cash:
                continue
            if not cash["allow_negative"] and int(cash["balance"]) < int(accrual["amount"]):
                continue
            transaction_id = "payroll:" + accrual["accrual_id"]
            self.db.execute("SAVEPOINT payroll")
            try:
                transaction = self._post_transaction_locked(
                    transaction_id, "payroll", "Hourly wage for " + accrual["employee_id"],
                    [{"account_id": accrual["cash_account_id"], "amount": -int(accrual["amount"])},
                     {"account_id": accrual["employee_account_id"], "amount": int(accrual["amount"])}],
                )
            except (ValueError, KeyError, sqlite3.IntegrityError) as exc:
                # The wage stays pending and visible instead of being marked paid or blocking others.
                self.db.execute("ROLLBACK TO payroll")
                self.db.execute("RELEASE payroll")
                LOG.warning("payroll %s not settled: %s", accrual["accrual_id"], exc)
                continue
            self.db.execute("RELEASE payroll")
            self.db.execute(
                "UPDATE world_payroll_accruals SET status='paid',paid_transaction_id=? WHERE accrual_id=? AND status='pending'",
                (transaction_id, accrual["accrual_id"]),
            )
            settled.append({"accrual_id": accrual["accrual_id"], "transaction_id": transaction_id,
                            "duplicate": transaction.get("duplicate", False)})
        return settled

    def settle_pending_payroll(self) -> list:
        with self.lock, self.db:
            return self._settle_pending_payroll_locked()

    def process_life_event(self, event: dict) -> Optional[dict]:
        """Persist payroll obligations first; settle only when the employer cash account can fund them."""
        if not isinstance(event, dict):
            raise ValueError("life_event_object_required")
        event_id = event.get("event_id")
        if not isinstance(event_id, str) or not event_id:
            raise ValueError("life_event_id_required")
        with self.lock, self.db:
            if self.db.execute("SELECT 1 FROM world_processed_life_events WHERE event_id=?", (event_id,)).fetchone():
                return {"event_id": event_id, "duplicate": True}
            result = None
            if event.get("event_type") == "work_hour_completed":
                employee_id = event.get("npc_id")
                sim_minute = event.get("sim_minute")
                payload = event.get("payload", {})
                if not isinstance(payload, dict):
                    payload = {}
                if isinstance(employee_id, str) and isinstance(sim_minute, int):
                    work_total = payload.get("total_work_minutes")
                    payroll_key = work_total if isinstance(work_total, int) and not isinstance(work_total, bool) else sim_minute
                    contracts = self.db.execute("""SELECT c.contract_id,c.employee_account_id,c.hourly_wage,b.cash_account_id
                        FROM world_employment_contracts c JOIN world_businesses b ON b.business_id=c.business_id
                        WHERE c.employee_id=? AND c.status='active' ORDER BY c.contract_id""", (employee_id,)).fetchall()
                    accrual_ids = []
                    for contract in contracts:
                        wage = int(contract["hourly_wage"])
                        if wage <= 0:
                            continue
                        accrual_id = "payroll-accrual:" + hashlib.sha256(
                            (contract["contract_id"] + "|" + employee_id + "|" + str(payroll_key)).encode("utf-8")
                        ).hexdigest()
                        self.db.execute("""INSERT OR IGNORE INTO world_payroll_accruals
                            (accrual_id,contract_id,employee_id,employee_account_id,cash_account_id,amount,status,created_minute)
                            VALUES(?,?,?,?,?,?,'pending',?)""",
                            (accrual_id, contract["contract_id"], employee_id, contract["employee_account_id"],
                             contract["cash_account_id"], wage, sim_minute))
                        accrual_ids.append(accrual_id)
                    settled = self._settle_pending_payroll_locked()
                    result = {"employee_id": employee_id, "accrual_ids": accrual_ids,
                              "settled": settled}
            self.db.execute("INSERT INTO world_processed_life_events(event_id,processed_minute) VALUES(?,?)",
                            (event_id, int(self.minute_provider())))
            return result or {"event_id": event_id, "processed": True}

    def process_pending_life_events(self, limit: int = 1000) -> int:
        if isinstance(limit, bool) or not isinstance(limit, int) or not 1 <= limit <= 10000:
            raise ValueError("invalid_life_event_limit")
        with self.lock:
            rows = self.db.execute("""SELECT e.event_id,e.sim_minute,e.npc_id,e.event_type,e.payload_json
                FROM life_events e LEFT JOIN world_processed_life_events p ON p.event_id=e.event_id
                WHERE p.event_id IS NULL ORDER BY e.sim_minute,e.event_id LIMIT ?""", (limit,)).fetchall()
        processed = 0
        for row in rows:
            self.process_life_event({"event_id":row["event_id"],"sim_minute":row["sim_minute"],
                "npc_id":row["npc_id"],"event_type":row["event_type"],"payload":json.loads(row["payload_json"])})
            processed += 1
        return processed


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
            self._settle_pending_payroll_locked()

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
            employment = [dict(r) for r in self.db.execute("SELECT * FROM world_employment_contracts ORDER BY contract_id")]
            relationships = [dict(r) for r in self.db.execute("SELECT * FROM world_social_relationships ORDER BY actor_id,target_id")]
            payroll_accruals = [dict(r) for r in self.db.execute("SELECT * FROM world_payroll_accruals ORDER BY created_minute,accrual_id")]
            return {"buildings": buildings, "objects": objects, "properties": properties, "businesses": businesses,
                    "employment": employment, "payroll_accruals": payroll_accruals,
                    "relationships": relationships, "traffic": traffic}
