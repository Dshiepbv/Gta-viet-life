"""Deterministic, game-neutral NPC life simulation with durable SQLite state.

This is the first M2 vertical slice. It deliberately does not spawn game entities:
the GTA adapter must map semantic locations and execute physical actions separately.
"""
from __future__ import annotations

from dataclasses import asdict, dataclass, field
import json
import sqlite3
import threading
import time
import uuid
from pathlib import Path
from typing import Dict, List, Optional, Sequence


MAX_ADVANCE_MINUTES = 7 * 24 * 60
# Body observations arrive every two seconds; their replay receipts only matter briefly.
VOLATILE_REQUEST_PREFIXES = ("player_observation:",)
VOLATILE_RETENTION_SECONDS = 3600
PRUNE_EVERY_VOLATILE_SAVES = 200
NEED_KEYS = ("hunger", "thirst", "energy", "social", "stress", "hygiene", "bladder")
MINUTES_PER_LIFE_YEAR = 365 * 24 * 60


def life_stage(age_years: int) -> str:
    if age_years < 3: return "infant"
    if age_years < 13: return "child"
    if age_years < 18: return "teen"
    if age_years < 30: return "young_adult"
    if age_years < 60: return "adult"
    if age_years < 75: return "senior"
    return "elder"


@dataclass(frozen=True)
class ScheduleBlock:
    start_minute: int
    end_minute: int
    activity: str
    location_id: str

    def __post_init__(self) -> None:
        if not 0 <= self.start_minute < 1440 or not 0 <= self.end_minute <= 1440:
            raise ValueError("schedule minute must be within a day")
        if self.start_minute >= self.end_minute:
            raise ValueError("schedule blocks must not wrap midnight")
        if not self.activity or not self.location_id:
            raise ValueError("schedule activity and location are required")


@dataclass(frozen=True)
class NPCProfile:
    npc_id: str
    display_name: str
    home_location_id: str
    occupation: str
    traits: Sequence[str] = ()
    schedule: Sequence[ScheduleBlock] = ()
    life_goal: str = "career"
    household_id: str = ""
    age_years: int = 30
    biography: str = ""
    skills: Dict[str, float] = field(default_factory=dict)
    knowledge: Sequence[dict] = ()
    actor_kind: str = "npc"

    def __post_init__(self) -> None:
        if not self.npc_id or len(self.npc_id) > 128:
            raise ValueError("invalid npc_id")
        if not self.display_name or not self.home_location_id:
            raise ValueError("display_name and home_location_id are required")
        if self.life_goal not in ("career", "health", "social"):
            raise ValueError("unsupported_life_goal")
        if self.actor_kind not in ("player", "npc"):
            raise ValueError("invalid_actor_kind")
        if isinstance(self.age_years, bool) or not isinstance(self.age_years, int) or not 0 <= self.age_years <= 120:
            raise ValueError("invalid_age_years")
        if len(self.biography) > 2000 or len(self.household_id) > 128:
            raise ValueError("profile_text_too_long")
        if len(self.skills) > 64 or any(not isinstance(k, str) or not k or not isinstance(v, (int, float)) or isinstance(v, bool) or not 0 <= v <= 100 for k, v in self.skills.items()):
            raise ValueError("invalid_skill_profile")
        if len(self.knowledge) > 128:
            raise ValueError("too_many_knowledge_facts")
        for fact in self.knowledge:
            if not isinstance(fact, dict) or not isinstance(fact.get("fact"), str) or not fact["fact"] or len(fact["fact"]) > 500:
                raise ValueError("invalid_knowledge_fact")
            if not isinstance(fact.get("source", ""), str) or len(fact.get("source", "")) > 128:
                raise ValueError("invalid_knowledge_source")
            confidence = fact.get("confidence", 1.0)
            if isinstance(confidence, bool) or not isinstance(confidence, (int, float)) or not 0 <= confidence <= 1:
                raise ValueError("invalid_knowledge_confidence")
        if len(self.schedule) > 32:
            raise ValueError("too_many_schedule_blocks")
        ordered = sorted(self.schedule, key=lambda block: block.start_minute)
        for previous, current in zip(ordered, ordered[1:]):
            if current.start_minute < previous.end_minute:
                raise ValueError("overlapping_schedule_blocks")
        # JSON round trips turn tuples into lists; one canonical type keeps equality stable after restart.
        for name in ("traits", "schedule", "knowledge"):
            object.__setattr__(self, name, tuple(getattr(self, name)))


@dataclass
class NPCState:
    profile: NPCProfile
    hunger: float = 35.0
    thirst: float = 25.0
    energy: float = 85.0
    social: float = 65.0
    stress: float = 10.0
    affinity: Dict[str, float] = field(default_factory=dict)
    current_activity: str = "idle"
    location_id: str = ""
    last_transition_minute: int = 0
    memories: List[dict] = field(default_factory=list)
    goal_progress: float = 0.0
    health: float = 100.0
    hygiene: float = 85.0
    bladder: float = 10.0
    fitness: float = 50.0
    mood: float = 70.0
    age_minutes: int = 0
    work_minutes: int = 0
    manual_activity: str = ""
    manual_activity_until: int = 0
    manual_location_id: str = ""

    def __post_init__(self) -> None:
        if not self.location_id:
            self.location_id = self.profile.home_location_id


class SQLiteLifeStore:
    """Versioned, transactional persistence for canonical NPC state and events."""

    SCHEMA_VERSION = 4

    def __init__(self, path: str | Path = ":memory:") -> None:
        self._lock = threading.RLock()
        self.path = str(path)
        self.connection = self._connect(self.path)
        self._volatile_saves = 0
        try:
            self._backup_before_upgrade()
            self._migrate()
        except BaseException:
            self.connection.close()
            raise
        self.prune_volatile_requests()

    def _connect(self, path: str) -> sqlite3.Connection:
        connection = sqlite3.connect(path, check_same_thread=False, timeout=10.0)
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA busy_timeout=10000")
        connection.execute("PRAGMA foreign_keys=ON")
        if path != ":memory:":
            connection.execute("PRAGMA journal_mode=WAL")
            connection.execute("PRAGMA synchronous=FULL")
        return connection

    def _stored_version(self) -> int:
        if not self.connection.execute(
                "SELECT 1 FROM sqlite_master WHERE type='table' AND name='schema_meta'").fetchone():
            return 0
        row = self.connection.execute("SELECT value FROM schema_meta WHERE key='schema_version'").fetchone()
        return int(row["value"]) if row else 0

    def _backup_before_upgrade(self) -> None:
        """Copy an existing database aside before any schema change touches it."""
        version = self._stored_version()
        if self.path == ":memory:" or not 0 < version < self.SCHEMA_VERSION:
            return
        target = "%s.v%d.bak" % (self.path, version)
        if Path(target).exists():
            return
        partial = target + ".tmp"
        copy = sqlite3.connect(partial)
        try:
            self.connection.backup(copy)
        finally:
            copy.close()
        Path(partial).replace(target)

    def prune_volatile_requests(self) -> int:
        """Delete expired receipts of high-frequency idempotent requests; durable receipts are kept."""
        removed = 0
        with self._lock, self.connection:
            for prefix in VOLATILE_REQUEST_PREFIXES:
                removed += self.connection.execute(
                    """DELETE FROM request_dedup WHERE request_id>=? AND request_id<?
                    AND created_at < datetime('now', ?)""",
                    (prefix, prefix + "\uffff", "-%d seconds" % VOLATILE_RETENTION_SECONDS)).rowcount
        return removed

    def _migrate(self) -> None:
        with self._lock, self.connection:
            # sqlite3 autocommits DDL unless a transaction is opened explicitly.
            self.connection.execute("BEGIN IMMEDIATE")
            self.connection.execute("CREATE TABLE IF NOT EXISTS schema_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)")
            row = self.connection.execute("SELECT value FROM schema_meta WHERE key='schema_version'").fetchone()
            version = int(row["value"]) if row else 0
            if version > self.SCHEMA_VERSION:
                raise RuntimeError("database_schema_newer_than_engine")
            if version < 1:
                self.connection.execute("""CREATE TABLE IF NOT EXISTS npc_state (
                    npc_id TEXT PRIMARY KEY, profile_json TEXT NOT NULL, state_json TEXT NOT NULL,
                    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP)""")
                self.connection.execute("""CREATE TABLE IF NOT EXISTS life_events (
                    event_id TEXT PRIMARY KEY, sim_minute INTEGER NOT NULL, npc_id TEXT NOT NULL,
                    event_type TEXT NOT NULL, payload_json TEXT NOT NULL)""")
                self.connection.execute("INSERT OR IGNORE INTO schema_meta(key,value) VALUES('simulation_minute','0')")
                version = 1
            if version < 2:
                self.connection.execute("""CREATE TABLE IF NOT EXISTS request_dedup (
                    request_id TEXT PRIMARY KEY, response_json TEXT NOT NULL,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP)""")
                version = 2
            if version < 3:
                self.connection.execute("""CREATE TABLE IF NOT EXISTS command_outbox (
                    command_id TEXT PRIMARY KEY, npc_id TEXT NOT NULL, command_type TEXT NOT NULL,
                    payload_json TEXT NOT NULL, created_minute INTEGER NOT NULL,
                    status TEXT NOT NULL DEFAULT 'pending', result_json TEXT,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, completed_at TEXT)""")
                self.connection.execute("CREATE INDEX IF NOT EXISTS idx_command_outbox_status ON command_outbox(status, created_minute)")
                version = 3
            # Version 4 adds the ledger guards and opening-balance records owned by the society module.
            version = max(version, 4)
            self.connection.execute(
                "INSERT OR REPLACE INTO schema_meta(key,value) VALUES('schema_version',?)", (str(version),)
            )

    def load(self) -> tuple[int, List[NPCState]]:
        with self._lock:
            row = self.connection.execute("SELECT value FROM schema_meta WHERE key='simulation_minute'").fetchone()
            minute = int(row["value"]) if row else 0
            result = []
            for row in self.connection.execute("SELECT profile_json,state_json FROM npc_state ORDER BY npc_id"):
                p = json.loads(row["profile_json"])
                profile = NPCProfile(
                    npc_id=p["npc_id"], display_name=p["display_name"],
                    home_location_id=p["home_location_id"], occupation=p["occupation"],
                    traits=tuple(p.get("traits", ())),
                    schedule=tuple(ScheduleBlock(**b) for b in p.get("schedule", ())),
                    life_goal=p.get("life_goal", "career"), household_id=p.get("household_id", ""),
                    age_years=p.get("age_years", 30), biography=p.get("biography", ""),
                    skills=p.get("skills", {}), knowledge=p.get("knowledge", ()),
                    actor_kind=p.get("actor_kind", "npc"),
                )
                s = json.loads(row["state_json"])
                result.append(NPCState(
                    profile=profile, hunger=s["hunger"], thirst=s.get("thirst",25.0), energy=s["energy"],
                    social=s["social"], stress=s["stress"], affinity=s.get("affinity", {}),
                    current_activity=s["current_activity"], location_id=s["location_id"],
                    last_transition_minute=s["last_transition_minute"], memories=s.get("memories", []),
                    goal_progress=s.get("goal_progress", 0.0),
                    health=s.get("health", 100.0), hygiene=s.get("hygiene", 85.0),
                    bladder=s.get("bladder", 10.0), fitness=s.get("fitness", 50.0),
                    mood=s.get("mood", 70.0), age_minutes=s.get("age_minutes", 0),
                    work_minutes=s.get("work_minutes", 0),
                    manual_activity=s.get("manual_activity", ""),
                    manual_activity_until=s.get("manual_activity_until", 0),
                    manual_location_id=s.get("manual_location_id", ""),
                ))
            return minute, result

    def get_request_result(self, request_id: str) -> Optional[dict]:
        with self._lock:
            row = self.connection.execute(
                "SELECT response_json FROM request_dedup WHERE request_id=?", (request_id,)
            ).fetchone()
            return json.loads(row["response_json"]) if row else None

    def save(self, sim_minute: int, states: Sequence[NPCState], events: Sequence[dict],
             request_result: Optional[tuple[str, dict]] = None,
             commands: Sequence[dict] = ()) -> None:
        with self._lock, self.connection:
            for state in states:
                p = asdict(state.profile)
                s = {
                    "hunger": state.hunger, "thirst": state.thirst, "energy": state.energy, "social": state.social,
                    "stress": state.stress, "affinity": state.affinity,
                    "current_activity": state.current_activity, "location_id": state.location_id,
                    "last_transition_minute": state.last_transition_minute, "memories": state.memories[-100:],
                    "goal_progress": state.goal_progress, "health": state.health,
                    "hygiene": state.hygiene, "bladder": state.bladder,
                    "fitness": state.fitness, "mood": state.mood,
                    "age_minutes": state.age_minutes, "work_minutes": state.work_minutes,
                    "manual_activity": state.manual_activity,
                    "manual_activity_until": state.manual_activity_until,
                    "manual_location_id": state.manual_location_id,
                }
                self.connection.execute(
                    """INSERT INTO npc_state(npc_id,profile_json,state_json,updated_at)
                    VALUES(?,?,?,CURRENT_TIMESTAMP) ON CONFLICT(npc_id) DO UPDATE SET
                    profile_json=excluded.profile_json,state_json=excluded.state_json,updated_at=CURRENT_TIMESTAMP""",
                    (state.profile.npc_id, json.dumps(p, ensure_ascii=False), json.dumps(s, ensure_ascii=False)),
                )
            for event in events:
                self.connection.execute(
                    "INSERT OR IGNORE INTO life_events(event_id,sim_minute,npc_id,event_type,payload_json) VALUES(?,?,?,?,?)",
                    (event["event_id"], event["sim_minute"], event["npc_id"], event["event_type"],
                     json.dumps(event["payload"], ensure_ascii=False)),
                )
            self.connection.execute(
                "INSERT OR REPLACE INTO schema_meta(key,value) VALUES('simulation_minute',?)", (str(sim_minute),)
            )
            self.connection.execute(
                "INSERT OR REPLACE INTO schema_meta(key,value) VALUES('last_wallclock_epoch',?)", (str(time.time()),)
            )
            for command in commands:
                self.connection.execute(
                    """UPDATE command_outbox SET status='superseded',
                    result_json=?,completed_at=CURRENT_TIMESTAMP
                    WHERE npc_id=? AND command_type=? AND status='pending'""",
                    (json.dumps({"success": False, "reason": "superseded_by_newer_state"}),
                     command["npc_id"], command["command_type"]),
                )
                self.connection.execute(
                    """INSERT OR IGNORE INTO command_outbox
                    (command_id,npc_id,command_type,payload_json,created_minute,status)
                    VALUES(?,?,?,?,?,'pending')""",
                    (command["command_id"], command["npc_id"], command["command_type"],
                     json.dumps(command["payload"], ensure_ascii=False), command["created_minute"]),
                )
            if request_result is not None:
                request_id, response = request_result
                self.connection.execute(
                    "INSERT OR IGNORE INTO request_dedup(request_id,response_json) VALUES(?,?)",
                    (request_id, json.dumps(response, ensure_ascii=False)),
                )
        if request_result is not None and request_result[0].startswith(VOLATILE_REQUEST_PREFIXES):
            self._volatile_saves += 1
            if self._volatile_saves % PRUNE_EVERY_VOLATILE_SAVES == 0:
                self.prune_volatile_requests()

    def pending_commands(self, limit: int = 50) -> List[dict]:
        if isinstance(limit, bool) or not isinstance(limit, int) or not 1 <= limit <= 200:
            raise ValueError("invalid_command_limit")
        with self._lock:
            rows = self.connection.execute(
                """SELECT command_id,npc_id,command_type,payload_json,created_minute
                FROM command_outbox WHERE status='pending'
                ORDER BY created_minute,command_id LIMIT ?""", (limit,)
            ).fetchall()
            return [{"command_id": r["command_id"], "npc_id": r["npc_id"],
                     "command_type": r["command_type"], "payload": json.loads(r["payload_json"]),
                     "created_minute": r["created_minute"]} for r in rows]

    def complete_command(self, command_id: str, result: dict) -> bool:
        status = "completed" if result.get("success") is True else result.get("status")
        with self._lock, self.connection:
            cur = self.connection.execute(
                """UPDATE command_outbox SET status=?,result_json=?,completed_at=CURRENT_TIMESTAMP
                WHERE command_id=? AND status='pending'""",
                (status, json.dumps(result, ensure_ascii=False), command_id)
            )
            return cur.rowcount == 1

    def get_meta(self, key: str) -> Optional[str]:
        if not isinstance(key, str) or not key or len(key) > 128:
            raise ValueError("invalid_meta_key")
        with self._lock:
            row = self.connection.execute("SELECT value FROM schema_meta WHERE key=?", (key,)).fetchone()
            return row["value"] if row else None

    def set_meta(self, key: str, value: str) -> None:
        if not isinstance(key, str) or not key or len(key) > 128:
            raise ValueError("invalid_meta_key")
        if not isinstance(value, str) or len(value) > 256:
            raise ValueError("invalid_meta_value")
        with self._lock, self.connection:
            self.connection.execute("INSERT OR REPLACE INTO schema_meta(key,value) VALUES(?,?)", (key, value))

    def get_wallclock_epoch(self) -> Optional[float]:
        with self._lock:
            row = self.connection.execute("SELECT value FROM schema_meta WHERE key='last_wallclock_epoch'").fetchone()
            return float(row["value"]) if row else None

    def close(self) -> None:
        with self._lock:
            self.connection.close()


class LifeSimulationEngine:
    """Deterministic minute-step NPC life rules; game-neutral and restart-safe."""

    def __init__(self, store: Optional[SQLiteLifeStore] = None) -> None:
        self.store = store or SQLiteLifeStore()
        self._lock = threading.RLock()
        self.simulation_minute, loaded = self.store.load()
        self.npcs: Dict[str, NPCState] = {s.profile.npc_id: s for s in loaded}
        self.events: List[dict] = []
        self.commands: List[dict] = []

    @staticmethod
    def _clamp(value: float) -> float:
        return round(max(0.0, min(100.0, value)), 3)

    def set_clock_baseline(self, minute: int) -> bool:
        """Align an empty simulation to the first live game-clock observation."""
        if isinstance(minute, bool) or not isinstance(minute, int) or minute < 0:
            raise ValueError("invalid_clock_baseline")
        with self._lock:
            if self.npcs:
                return False
            self.simulation_minute = minute
            self.store.save(minute, [], [])
            return True

    def register_npc(self, profile: NPCProfile) -> NPCState:
        with self._lock:
            existing = self.npcs.get(profile.npc_id)
            if existing:
                if existing.profile != profile:
                    raise ValueError("npc_identity_conflict")
                return existing
            state = NPCState(profile=profile)
            self.npcs[profile.npc_id] = state
            self.store.save(self.simulation_minute, [state], [])
            return state

    def _scheduled(self, state: NPCState, minute_of_day: int) -> tuple[str, str]:
        for block in state.profile.schedule:
            if block.start_minute <= minute_of_day < block.end_minute:
                return block.activity, block.location_id
        return "free_time", state.location_id

    def _begin_batch(self) -> None:
        """Start a new event/command batch so an operation never re-submits an earlier one."""
        self.events = []
        self.commands = []

    def _record(self, state: NPCState, event_type: str, payload: dict) -> None:
        event_id = uuid.uuid4().hex
        self.events.append({
            "event_id": event_id, "sim_minute": self.simulation_minute,
            "npc_id": state.profile.npc_id, "event_type": event_type, "payload": payload,
        })
        if event_type == "activity_changed" and state.profile.actor_kind != "player":
            self.commands.append({
                "command_id": event_id, "npc_id": state.profile.npc_id,
                "command_type": "actor.set_activity", "created_minute": self.simulation_minute,
                "payload": {"activity": payload["to"], "location_id": payload["location_id"],
                            "transition_minute": self.simulation_minute},
            })

    def _tick_npc(self, state: NPCState) -> None:
        minute_of_day = self.simulation_minute % 1440
        planned, planned_location = self._scheduled(state, minute_of_day)
        if state.profile.actor_kind == "player" and state.manual_activity:
            if self.simulation_minute <= state.manual_activity_until:
                planned, planned_location = state.manual_activity, state.manual_location_id or state.location_id
            else:
                state.manual_activity = ""
                state.manual_activity_until = 0
                state.manual_location_id = ""
        activity, location = planned, planned_location
        if (state.profile.actor_kind != "player" and ((state.current_activity == "eat" and state.hunger > 55) or state.hunger >= 92)):
            activity = "eat"
        elif state.profile.actor_kind != "player" and ((state.current_activity == "drink" and state.thirst > 55) or state.thirst >= 92):
            activity = "drink"
        elif state.profile.actor_kind != "player" and ((state.current_activity == "sleep" and state.energy < 80) or state.energy <= 8):
            activity, location = "sleep", state.profile.home_location_id
        elif state.profile.actor_kind != "player" and ((state.current_activity == "socialize" and state.social < 55)
              or (state.social <= 8 and planned in ("free_time", "leisure"))):
            activity = "socialize"

        is_player = state.profile.actor_kind == "player"
        state.age_minutes += 1
        state.bladder += 0.11
        if not is_player:
            state.hygiene -= 0.035
            if (state.current_activity in ("toilet", "use_bathroom") and state.bladder > 20) or state.bladder >= 94:
                activity, location = "toilet", state.profile.home_location_id
            elif (state.current_activity in ("shower", "bathe") and state.hygiene < 80) or state.hygiene <= 8:
                activity, location = "shower", state.profile.home_location_id
            state.thirst += 0.08
        if activity == "sleep":
            if not is_player:
                state.energy += 1.7
                state.health += 0.025
                state.hunger += 0.35
                state.thirst += 0.15
            state.stress -= 0.35
        elif activity == "eat":
            if not is_player:
                state.hunger -= 4.0
                state.thirst += 0.08
                state.bladder += 0.04
                state.energy += 0.15
            state.stress -= 0.1
        elif activity == "drink":
            state.thirst -= 4.0
            state.stress -= 0.05
        elif activity in ("work", "commute"):
            if not is_player:
                state.energy -= 0.65
                state.hunger += 0.75
                state.thirst += 0.15
            work_stress = 0.45 if activity == "work" else 0.2
            if "anxious" in state.profile.traits:
                work_stress += 0.25
            if "calm" in state.profile.traits:
                work_stress = max(0.05, work_stress - 0.15)
            state.stress += work_stress
        elif activity == "socialize":
            social_recovery = 2.2
            if "extroverted" in state.profile.traits:
                social_recovery += 0.8
            if "introverted" in state.profile.traits:
                social_recovery -= 0.7
            if "friendly" in state.profile.traits:
                social_recovery += 0.2
            state.social += max(0.5, social_recovery)
            if not is_player:
                state.energy -= 0.25
                state.hunger += 0.35
                state.thirst += 0.08
            state.stress -= 0.3
        elif activity in ("leisure", "free_time"):
            if not is_player:
                state.energy -= 0.2
                state.hunger += 0.35
                state.thirst += 0.03
            state.social += 0.15 + (0.15 if "extroverted" in state.profile.traits else 0.0)
            state.stress -= 0.25 + (0.1 if "calm" in state.profile.traits else 0.0)
        elif activity in ("shower", "bathe"):
            if not is_player:
                state.hygiene += 3.5
            state.stress -= 0.2
        elif activity in ("toilet", "use_bathroom"):
            state.bladder -= 12.0
            state.stress -= 0.1
        elif activity == "exercise":
            if not is_player:
                state.fitness += 0.12
                state.energy -= 0.55
                state.hunger += 0.65
                state.thirst += 0.35
            state.stress -= 0.35
        elif activity == "study":
            if not is_player:
                state.energy -= 0.3
            state.stress += 0.15
            study_gain = 0.015 + (0.01 if "curious" in state.profile.traits or "disciplined" in state.profile.traits else 0.0)
            state.goal_progress = self._clamp(state.goal_progress + study_gain)
        else:
            if not is_player:
                state.energy -= 0.15
                state.hunger += 0.4

        # Basic autonomic needs apply equally to the player avatar and every NPC.
        if not is_player and (state.hunger >= 98 or state.thirst >= 98 or state.energy <= 2 or state.hygiene <= 1 or state.bladder >= 99):
            state.health -= 0.08
        if activity == "work":
            state.work_minutes += 1
            if state.work_minutes % 60 == 0:
                self._record(state, "work_hour_completed", {"hours": 1, "total_work_minutes": state.work_minutes})
        if state.profile.life_goal == "career" and activity == "work":
            career_gain = 0.02 + (0.01 if "ambitious" in state.profile.traits or "disciplined" in state.profile.traits else 0.0)
            state.goal_progress = self._clamp(state.goal_progress + career_gain)
        elif state.profile.life_goal == "health" and activity in ("sleep", "eat"):
            state.goal_progress = self._clamp(state.goal_progress + 0.01)
        elif state.profile.life_goal == "social" and activity in ("socialize", "leisure"):
            state.goal_progress = self._clamp(state.goal_progress + 0.02)
        state.health = self._clamp(state.health)
        state.hygiene = self._clamp(state.hygiene)
        state.bladder = self._clamp(state.bladder)
        state.fitness = self._clamp(state.fitness - (0.001 if activity != "exercise" else 0.0))
        state.mood = self._clamp(100.0 - (state.stress * 0.55) - (state.hunger * 0.15)
                                 - ((100.0 - state.social) * 0.12) - ((100.0 - state.energy) * 0.08))
        for key in NEED_KEYS:
            setattr(state, key, self._clamp(getattr(state, key)))
        previous_stage = life_stage(state.profile.age_years + ((state.age_minutes - 1) // MINUTES_PER_LIFE_YEAR))
        current_stage = life_stage(state.profile.age_years + (state.age_minutes // MINUTES_PER_LIFE_YEAR))
        if current_stage != previous_stage:
            self._record(state, "life_stage_changed", {"from": previous_stage, "to": current_stage,
                                                       "age_years": state.profile.age_years + (state.age_minutes // MINUTES_PER_LIFE_YEAR)})
        if activity != state.current_activity or location != state.location_id:
            previous = state.current_activity
            state.current_activity = activity
            state.location_id = location
            state.last_transition_minute = self.simulation_minute
            self._record(state, "activity_changed", {"from": previous, "to": activity, "location_id": location})

    def _process_autonomous_social_interactions(self) -> None:
        """Pair co-located NPCs on a social activity; deterministic and bounded per 15-minute pulse."""
        groups: Dict[str, List[NPCState]] = {}
        for state in self.npcs.values():
            if state.current_activity in ("socialize", "leisure") and state.location_id:
                groups.setdefault(state.location_id, []).append(state)
        for location_id in sorted(groups):
            participants = sorted(groups[location_id], key=lambda item: item.profile.npc_id)
            for index in range(0, len(participants) - 1, 2):
                left, right = participants[index], participants[index + 1]
                shared_traits = set(left.profile.traits).intersection(right.profile.traits)
                affinity_gain = 2.0 if left.profile.household_id and left.profile.household_id == right.profile.household_id else 1.0
                affinity_gain += min(3, len(shared_traits)) * 0.25
                if "rival" in left.profile.traits or "rival" in right.profile.traits:
                    affinity_gain = -0.5
                left.affinity[right.profile.npc_id] = round(max(-100.0, min(100.0, left.affinity.get(right.profile.npc_id, 0.0) + affinity_gain)), 3)
                right.affinity[left.profile.npc_id] = round(max(-100.0, min(100.0, right.affinity.get(left.profile.npc_id, 0.0) + affinity_gain)), 3)
                for actor, other in ((left, right), (right, left)):
                    actor.memories.append({
                        "minute": self.simulation_minute, "kind": "npc_conversation",
                        "participant_id": other.profile.npc_id,
                        "summary": "Talked with " + other.profile.display_name + " at " + location_id,
                        "source": "autonomous_social_simulation", "confidence": 1.0,
                    })
                    actor.memories = actor.memories[-100:]
                    self._record(actor, "social_interaction", {
                        "participant_id": other.profile.npc_id, "location_id": location_id,
                        "affinity_gain": affinity_gain,
                    })

    def advance(self, minutes: int, request_id: Optional[str] = None) -> dict:
        if isinstance(minutes, bool) or not isinstance(minutes, int) or not 0 <= minutes <= MAX_ADVANCE_MINUTES:
            raise ValueError("minutes_must_be_integer_between_0_and_10080")
        if request_id is not None and (not request_id or len(request_id) > 128):
            raise ValueError("invalid_request_id")
        with self._lock:
            dedup_key = "advance:" + request_id if request_id else None
            if dedup_key:
                cached = self.store.get_request_result(dedup_key)
                if cached is not None:
                    return cached
            self._begin_batch()
            for _ in range(minutes):
                self.simulation_minute += 1
                for state in self.npcs.values():
                    self._tick_npc(state)
                if self.simulation_minute % 15 == 0:
                    self._process_autonomous_social_interactions()
            result = {"simulation_minute": self.simulation_minute, "advanced_minutes": minutes,
                      "npc_count": len(self.npcs), "events": list(self.events)}
            request_result = (dedup_key, result) if dedup_key else None
            self.store.save(self.simulation_minute, list(self.npcs.values()), self.events, request_result, self.commands)
            return result

    def record_dialogue_memory(self, npc_id: str, player_id: str,
                               player_message: str, npc_reply: str,
                               request_id: Optional[str] = None, affinity_delta: int = 0) -> dict:
        """Persist the actual voice conversation without letting an LLM mutate canonical relationships."""
        if not player_id or len(player_id) > 128:
            raise ValueError("invalid_player_id")
        if not isinstance(player_message, str) or not player_message.strip() or len(player_message) > 2000:
            raise ValueError("invalid_player_message")
        if not isinstance(npc_reply, str) or not npc_reply.strip() or len(npc_reply) > 2000:
            raise ValueError("invalid_npc_reply")
        if request_id is not None and (not request_id or len(request_id) > 128):
            raise ValueError("invalid_request_id")
        if isinstance(affinity_delta, bool) or not isinstance(affinity_delta, int) or not -2 <= affinity_delta <= 2:
            raise ValueError("invalid_affinity_delta")
        dedup_key = "dialogue_memory:" + request_id if request_id else None
        with self._lock:
            if dedup_key:
                cached = self.store.get_request_result(dedup_key)
                if cached is not None:
                    return cached
            state = self.npcs.get(npc_id)
            if state is None:
                raise KeyError("npc_not_found")
            self._begin_batch()
            if affinity_delta:
                state.affinity[player_id] = round(max(-100.0, min(100.0,
                    state.affinity.get(player_id, 0.0) + affinity_delta * 5.0)), 3)
            player_state = self.npcs.get(player_id)
            if player_state is not None and player_state is not state:
                if affinity_delta:
                    player_state.affinity[npc_id] = round(max(-100.0, min(100.0,
                        player_state.affinity.get(npc_id, 0.0) + affinity_delta * 5.0)), 3)
                player_state.social = self._clamp(player_state.social + 4.0)
            for kind, summary in (("player_dialogue", player_message), ("npc_dialogue", npc_reply)):
                memory = {"minute": self.simulation_minute, "kind": kind,
                          "participant_id": player_id, "summary": summary[:500],
                          "source": "voice_bridge", "confidence": 1.0}
                state.memories.append(memory)
                state.memories = state.memories[-100:]
                if player_state is not None and player_state is not state:
                    player_memory = dict(memory)
                    player_memory["participant_id"] = npc_id
                    player_memory["source"] = "voice_bridge_mirror"
                    player_state.memories.append(player_memory)
                    player_state.memories = player_state.memories[-100:]
                self._record(state, kind, {"participant_id": player_id, "summary": summary[:160]})
            result = {"memory_recorded": True, "npc_id": npc_id, "memory_count": len(state.memories),
                      "player_memory_recorded": player_state is not None,
                      "affinity": state.affinity.get(player_id, 0.0)}
            self.store.save(self.simulation_minute, list(self.npcs.values()), self.events,
                            (dedup_key, result) if dedup_key else None, self.commands)
            return result

    def dialogue_context(self, npc_id: str, player_id: str, topic: str = "") -> dict:
        """Return bounded, provenance-aware facts for a cognition provider; never mutate state."""
        if not isinstance(topic, str) or len(topic) > 500:
            raise ValueError("invalid_dialogue_topic")
        with self._lock:
            state = self.npcs.get(npc_id)
            if state is None:
                raise KeyError("npc_not_found")
            profile = state.profile
            return {
                "identity": {"npc_id": profile.npc_id, "name": profile.display_name,
                             "age_years": profile.age_years + (state.age_minutes // MINUTES_PER_LIFE_YEAR),
                             "life_stage": life_stage(profile.age_years + (state.age_minutes // MINUTES_PER_LIFE_YEAR)),
                             "actor_kind": profile.actor_kind, "occupation": profile.occupation,
                             "household_id": profile.household_id, "biography": profile.biography,
                             "traits": list(profile.traits), "skills": dict(profile.skills),
                             "life_goal": profile.life_goal, "goal_progress": state.goal_progress},
                "current_state": {"activity": state.current_activity, "location_id": state.location_id,
                                  "needs": {"hunger": state.hunger, "thirst": state.thirst, "energy": state.energy,
                                            "social": state.social, "stress": state.stress,
                                            "hygiene": state.hygiene, "bladder": state.bladder,
                                            "health": state.health, "fitness": state.fitness, "mood": state.mood},
                                  "simulation_minute": self.simulation_minute},
                "relationship": {"player_id": player_id, "affinity": state.affinity.get(player_id, 0.0)},
                "known_facts": list(profile.knowledge),
                "recent_memories": [dict(m) for m in state.memories[-10:]],
                "topic": topic,
                "knowledge_boundary": "Only known_facts and recent_memories are established knowledge. Do not claim unlisted events, unseen locations or private facts. Any proposed world action must be validated by the Engine.",
            }

    def interact(self, npc_id: str, player_id: str, interaction: str = "talk",
                 request_id: Optional[str] = None, message: str = "") -> dict:
        if interaction != "talk":
            raise ValueError("unsupported_interaction")
        if not player_id or len(player_id) > 128:
            raise ValueError("invalid_player_id")
        if not isinstance(message, str) or len(message) > 2000:
            raise ValueError("invalid_player_message")
        if request_id is not None and (not request_id or len(request_id) > 128):
            raise ValueError("invalid_request_id")
        with self._lock:
            dedup_key = "interact:" + request_id if request_id else None
            if dedup_key:
                cached = self.store.get_request_result(dedup_key)
                if cached is not None:
                    return cached
            state = self.npcs.get(npc_id)
            if state is None:
                raise KeyError("npc_not_found")
            old = state.affinity.get(player_id, 0.0)
            interaction_gain = 2.0
            if "friendly" in state.profile.traits or "kind" in state.profile.traits:
                interaction_gain += 1.0
            if "introverted" in state.profile.traits:
                interaction_gain -= 0.5
            state.affinity[player_id] = self._clamp(old + interaction_gain)
            state.social = self._clamp(state.social + 12.0)
            memory = {"minute": self.simulation_minute, "kind": "player_conversation",
                      "participant_id": player_id, "summary": message[:500] if message else "Player spoke with NPC",
                      "source": "direct_player_interaction", "confidence": 1.0}
            state.memories.append(memory)
            state.memories = state.memories[-100:]
            states_to_save = [state]
            player_state = self.npcs.get(player_id)
            if player_state is not None and player_state is not state:
                player_state.affinity[npc_id] = self._clamp(player_state.affinity.get(npc_id, 0.0) + 2.0)
                player_state.social = self._clamp(player_state.social + 12.0)
                player_state.memories.append({
                    "minute": self.simulation_minute, "kind": "npc_conversation",
                    "participant_id": npc_id,
                    "summary": (state.profile.display_name + ": " + (message[:450] if message else "Player interaction")),
                    "source": "direct_player_interaction", "confidence": 1.0,
                })
                player_state.memories = player_state.memories[-100:]
                states_to_save.append(player_state)
            self._begin_batch()
            result = {"npc_id": npc_id, "interaction": interaction, "affinity": state.affinity[player_id],
                      "social_need": state.social, "memory_recorded": True,
                      "player_memory_recorded": player_state is not None,
                      "response_context": self.dialogue_context(npc_id, player_id, message)}
            self._record(state, "conversation", {"player_id": player_id, "interaction": interaction})
            request_result = (dedup_key, result) if dedup_key else None
            self.store.save(self.simulation_minute, states_to_save, self.events, request_result)
            return result

    def record_player_work(self, actor_id: str, duration_minutes: int,
                           request_id: Optional[str] = None) -> dict:
        if not isinstance(actor_id, str) or not actor_id or len(actor_id) > 128:
            raise ValueError("invalid_actor_id")
        if isinstance(duration_minutes, bool) or not isinstance(duration_minutes, int) or not 1 <= duration_minutes <= 1440:
            raise ValueError("invalid_work_duration")
        if request_id is not None and (not request_id or len(request_id) > 128):
            raise ValueError("invalid_request_id")
        dedup_key = "player_work:" + request_id if request_id else None
        with self._lock:
            if dedup_key:
                cached = self.store.get_request_result(dedup_key)
                if cached is not None:
                    return cached
            state = self.npcs.get(actor_id)
            if state is None:
                raise KeyError("actor_not_found")
            if state.profile.actor_kind != "player":
                raise ValueError("work_report_only_for_player_actor")
            previous = state.work_minutes
            state.work_minutes += duration_minutes
            completed_hours = state.work_minutes // 60 - previous // 60
            self._begin_batch()
            for index in range(completed_hours):
                boundary = ((previous // 60) + index + 1) * 60
                self._record(state, "work_hour_completed", {
                    "hours": 1, "total_work_minutes": boundary, "source": "native_player_job",
                })
            self._record(state, "player_work_completed", {
                "duration_minutes": duration_minutes, "total_work_minutes": state.work_minutes,
            })
            result = {"recorded": True, "actor_id": actor_id, "duration_minutes": duration_minutes,
                      "total_work_minutes": state.work_minutes, "completed_hours": completed_hours}
            self.store.save(self.simulation_minute, [state], self.events,
                            (dedup_key, result) if dedup_key else None)
            return result

    def set_player_activity(self, actor_id: str, activity: str, location_id: str,
                            duration_minutes: int, request_id: Optional[str] = None) -> dict:
        if not isinstance(actor_id, str) or not actor_id or len(actor_id) > 128:
            raise ValueError("invalid_actor_id")
        if not isinstance(activity, str) or activity not in ("work", "commute", "study", "exercise", "socialize", "leisure", "free_time"):
            raise ValueError("unsupported_player_activity")
        if not isinstance(location_id, str) or not location_id or len(location_id) > 128:
            raise ValueError("invalid_activity_location")
        if isinstance(duration_minutes, bool) or not isinstance(duration_minutes, int) or not 1 <= duration_minutes <= 1440:
            raise ValueError("invalid_activity_duration")
        if request_id is not None and (not request_id or len(request_id) > 128):
            raise ValueError("invalid_request_id")
        dedup_key = "player_activity:" + request_id if request_id else None
        with self._lock:
            if dedup_key:
                cached = self.store.get_request_result(dedup_key)
                if cached is not None:
                    return cached
            state = self.npcs.get(actor_id)
            if state is None:
                raise KeyError("actor_not_found")
            if state.profile.actor_kind != "player":
                raise ValueError("manual_activity_only_for_player")
            state.manual_activity = activity
            state.manual_activity_until = self.simulation_minute + duration_minutes
            state.manual_location_id = location_id
            state.current_activity = activity
            state.location_id = location_id
            state.last_transition_minute = self.simulation_minute
            self._begin_batch()
            self._record(state, "player_activity_started", {
                "activity": activity, "location_id": location_id, "duration_minutes": duration_minutes,
            })
            result = {"accepted": True, "actor_id": actor_id, "activity": activity,
                      "location_id": location_id, "until_minute": state.manual_activity_until}
            self.store.save(self.simulation_minute, [state], self.events,
                            (dedup_key, result) if dedup_key else None)
            return result

    def observe_player_state(self, actor_id: str, observed: dict, request_id: Optional[str] = None) -> dict:
        """Accept adapter-observed player body meters; the game remains authoritative for native player physiology."""
        if not isinstance(actor_id, str) or not actor_id or len(actor_id) > 128:
            raise ValueError("invalid_actor_id")
        if not isinstance(observed, dict) or len(observed) > 12:
            raise ValueError("invalid_observed_state")
        if request_id is not None and (not request_id or len(request_id) > 128):
            raise ValueError("invalid_request_id")
        allowed = {"hunger", "thirst", "energy", "hygiene", "health", "fitness"}
        if set(observed) - allowed:
            raise ValueError("unsupported_observed_field")
        normalized = {}
        for key, value in observed.items():
            if isinstance(value, bool) or not isinstance(value, (int, float)) or not 0 <= value <= 100:
                raise ValueError("invalid_observed_" + key)
            normalized[key] = float(value)
        dedup_key = "player_observation:" + request_id if request_id else None
        with self._lock:
            if dedup_key:
                cached = self.store.get_request_result(dedup_key)
                if cached is not None:
                    return cached
            state = self.npcs.get(actor_id)
            if state is None:
                raise KeyError("actor_not_found")
            if state.profile.actor_kind != "player":
                raise ValueError("observations_only_allowed_for_player_actor")
            for key, value in normalized.items():
                setattr(state, key, self._clamp(value))
            result = {"updated": True, "actor_id": actor_id, "observed": normalized}
            self.store.save(self.simulation_minute, [state], [],
                            (dedup_key, result) if dedup_key else None)
            return result

    def apply_physical_interaction(self, actor_id: str, object_kind: str) -> dict:
        """Apply canonical human-life consequences only after the adapter confirms a physical object use."""
        if not isinstance(actor_id, str) or not actor_id or len(actor_id) > 128:
            raise ValueError("invalid_actor_id")
        if not isinstance(object_kind, str) or not object_kind or len(object_kind) > 64:
            raise ValueError("invalid_object_kind")
        effects = {
            "food": ("eat", {"hunger": -35.0, "mood": 3.0, "bladder": 8.0}),
            "meal": ("eat", {"hunger": -35.0, "mood": 3.0, "bladder": 8.0}),
            "fridge": ("eat", {"hunger": -15.0, "thirst": -15.0, "bladder": 4.0}),
            "counter": ("eat", {"hunger": -20.0, "thirst": -10.0, "bladder": 4.0}),
            "vending_machine": ("drink", {"thirst": -20.0}),
            "bed": ("sleep", {"energy": 35.0, "health": 1.0, "stress": -8.0}),
            "toilet": ("toilet", {"bladder": -90.0}),
            "shower": ("shower", {"hygiene": 85.0, "stress": -5.0}),
            "sofa": ("leisure", {"energy": 8.0, "mood": 3.0, "stress": -2.0}),
            "gym_equipment": ("exercise", {"fitness": 8.0, "energy": -12.0, "stress": -5.0}),
            "computer": ("study", {"goal_progress": 0.5, "energy": -3.0}),
            "book": ("study", {"goal_progress": 0.5, "mood": 2.0}),
            "hobby": ("leisure", {"mood": 8.0, "stress": -6.0}),
            "social_seating": ("socialize", {"social": 15.0, "mood": 4.0}),
        }
        key = object_kind.strip().lower().replace("-", "_").replace(" ", "_")
        if key not in effects:
            return {"applied": False, "reason": "no_life_effect_mapping", "actor_id": actor_id}
        activity, changes = effects[key]
        with self._lock:
            state = self.npcs.get(actor_id)
            if state is None:
                return {"applied": False, "reason": "actor_not_registered", "actor_id": actor_id}
            for field_name, delta in changes.items():
                if field_name == "goal_progress":
                    state.goal_progress = self._clamp(state.goal_progress + delta)
                else:
                    setattr(state, field_name, self._clamp(getattr(state, field_name) + delta))
            state.current_activity = activity
            state.last_transition_minute = self.simulation_minute
            self._begin_batch()
            self._record(state, "physical_activity_completed", {
                "object_kind": key, "activity": activity, "actor_kind": state.profile.actor_kind,
            })
            self.store.save(self.simulation_minute, [state], self.events)
            return {"applied": True, "actor_id": actor_id, "actor_kind": state.profile.actor_kind,
                    "activity": activity, "changes": changes}

    def pending_commands(self, limit: int = 50) -> List[dict]:
        return self.store.pending_commands(limit)

    def complete_command(self, command_id: str, result: dict) -> bool:
        if not isinstance(command_id, str) or not command_id or len(command_id) > 128:
            raise ValueError("invalid_command_id")
        if not isinstance(result, dict):
            raise ValueError("command_result_object_required")
        success = result.get("success")
        status = result.get("status")
        if success is not True and not (success is False and status in ("failed", "unsupported", "rejected", "cancelled")):
            raise ValueError("command_result_status_required")
        return self.store.complete_command(command_id, result)

    def snapshot(self) -> dict:
        with self._lock:
            return {
                "simulation_minute": self.simulation_minute,
                "day": self.simulation_minute // 1440 + 1,
                "time_of_day": self.simulation_minute % 1440,
                "npc_count": len(self.npcs),
                "npcs": [{
                    "npc_id": s.profile.npc_id, "name": s.profile.display_name,
                    "occupation": s.profile.occupation, "home_location_id": s.profile.home_location_id,
                    "life_goal": s.profile.life_goal, "goal_progress": s.goal_progress,
                    "location_id": s.location_id, "activity": s.current_activity,
                    "actor_kind": s.profile.actor_kind,
                    "age_years": s.profile.age_years + (s.age_minutes // MINUTES_PER_LIFE_YEAR),
                    "life_stage": life_stage(s.profile.age_years + (s.age_minutes // MINUTES_PER_LIFE_YEAR)),
                    "health": s.health, "fitness": s.fitness, "mood": s.mood,
                    "needs": {"hunger": s.hunger, "thirst": s.thirst, "energy": s.energy, "social": s.social,
                              "stress": s.stress, "hygiene": s.hygiene, "bladder": s.bladder},
                    "relationships": dict(s.affinity), "memory_count": len(s.memories),
                } for s in sorted(self.npcs.values(), key=lambda item: item.profile.npc_id)],
            }
