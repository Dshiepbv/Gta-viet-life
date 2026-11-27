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
NEED_KEYS = ("hunger", "energy", "social", "stress")


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

    def __post_init__(self) -> None:
        if not self.npc_id or len(self.npc_id) > 128:
            raise ValueError("invalid npc_id")
        if not self.display_name or not self.home_location_id:
            raise ValueError("display_name and home_location_id are required")
        if self.life_goal not in ("career", "health", "social"):
            raise ValueError("unsupported_life_goal")
        if len(self.schedule) > 32:
            raise ValueError("too_many_schedule_blocks")
        ordered = sorted(self.schedule, key=lambda block: block.start_minute)
        for previous, current in zip(ordered, ordered[1:]):
            if current.start_minute < previous.end_minute:
                raise ValueError("overlapping_schedule_blocks")


@dataclass
class NPCState:
    profile: NPCProfile
    hunger: float = 35.0
    energy: float = 85.0
    social: float = 65.0
    stress: float = 10.0
    affinity: Dict[str, float] = field(default_factory=dict)
    current_activity: str = "idle"
    location_id: str = ""
    last_transition_minute: int = 0
    memories: List[dict] = field(default_factory=list)
    goal_progress: float = 0.0

    def __post_init__(self) -> None:
        if not self.location_id:
            self.location_id = self.profile.home_location_id


class SQLiteLifeStore:
    """Versioned, transactional persistence for canonical NPC state and events."""

    SCHEMA_VERSION = 3

    def __init__(self, path: str | Path = ":memory:") -> None:
        self._lock = threading.RLock()
        self.connection = sqlite3.connect(str(path), check_same_thread=False)
        self.connection.row_factory = sqlite3.Row
        self._migrate()

    def _migrate(self) -> None:
        with self._lock, self.connection:
            self.connection.execute("CREATE TABLE IF NOT EXISTS schema_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)")
            row = self.connection.execute("SELECT value FROM schema_meta WHERE key='schema_version'").fetchone()
            version = int(row["value"]) if row else 0
            if version > self.SCHEMA_VERSION:
                raise RuntimeError("database_schema_newer_than_engine")
            if version < 1:
                self.connection.execute("""CREATE TABLE npc_state (
                    npc_id TEXT PRIMARY KEY, profile_json TEXT NOT NULL, state_json TEXT NOT NULL,
                    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP)""")
                self.connection.execute("""CREATE TABLE life_events (
                    event_id TEXT PRIMARY KEY, sim_minute INTEGER NOT NULL, npc_id TEXT NOT NULL,
                    event_type TEXT NOT NULL, payload_json TEXT NOT NULL)""")
                self.connection.execute("INSERT OR REPLACE INTO schema_meta(key,value) VALUES('simulation_minute','0')")
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
                    life_goal=p.get("life_goal", "career"),
                )
                s = json.loads(row["state_json"])
                result.append(NPCState(
                    profile=profile, hunger=s["hunger"], energy=s["energy"],
                    social=s["social"], stress=s["stress"], affinity=s.get("affinity", {}),
                    current_activity=s["current_activity"], location_id=s["location_id"],
                    last_transition_minute=s["last_transition_minute"], memories=s.get("memories", []),
                    goal_progress=s.get("goal_progress", 0.0),
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
                    "hunger": state.hunger, "energy": state.energy, "social": state.social,
                    "stress": state.stress, "affinity": state.affinity,
                    "current_activity": state.current_activity, "location_id": state.location_id,
                    "last_transition_minute": state.last_transition_minute, "memories": state.memories[-100:],
                    "goal_progress": state.goal_progress,
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
        with self._lock, self.connection:
            cur = self.connection.execute(
                """UPDATE command_outbox SET status='completed',result_json=?,completed_at=CURRENT_TIMESTAMP
                WHERE command_id=? AND status='pending'""",
                (json.dumps(result, ensure_ascii=False), command_id)
            )
            return cur.rowcount == 1

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

    def _record(self, state: NPCState, event_type: str, payload: dict) -> None:
        event_id = uuid.uuid4().hex
        self.events.append({
            "event_id": event_id, "sim_minute": self.simulation_minute,
            "npc_id": state.profile.npc_id, "event_type": event_type, "payload": payload,
        })
        if event_type == "activity_changed":
            self.commands.append({
                "command_id": event_id, "npc_id": state.profile.npc_id,
                "command_type": "actor.set_activity", "created_minute": self.simulation_minute,
                "payload": {"activity": payload["to"], "location_id": payload["location_id"],
                            "transition_minute": self.simulation_minute},
            })

    def _tick_npc(self, state: NPCState) -> None:
        minute_of_day = self.simulation_minute % 1440
        planned, planned_location = self._scheduled(state, minute_of_day)
        activity, location = planned, planned_location
        if (state.current_activity == "eat" and state.hunger > 55) or state.hunger >= 92:
            activity = "eat"
        elif (state.current_activity == "sleep" and state.energy < 80) or state.energy <= 8:
            activity, location = "sleep", state.profile.home_location_id
        elif ((state.current_activity == "socialize" and state.social < 55)
              or (state.social <= 8 and planned in ("free_time", "leisure"))):
            activity = "socialize"

        if activity == "sleep":
            state.energy += 1.7
            state.hunger += 0.35
            state.stress -= 0.35
        elif activity == "eat":
            state.hunger -= 4.0
            state.energy += 0.15
            state.stress -= 0.1
        elif activity in ("work", "commute"):
            state.energy -= 0.65
            state.hunger += 0.75
            state.stress += 0.45 if activity == "work" else 0.2
        elif activity == "socialize":
            state.social += 2.2
            state.energy -= 0.25
            state.hunger += 0.35
            state.stress -= 0.3
        elif activity in ("leisure", "free_time"):
            state.energy -= 0.2
            state.hunger += 0.35
            state.social += 0.15
            state.stress -= 0.25
        else:
            state.energy -= 0.15
            state.hunger += 0.4

        if state.profile.life_goal == "career" and activity == "work":
            state.goal_progress = self._clamp(state.goal_progress + 0.02)
        elif state.profile.life_goal == "health" and activity in ("sleep", "eat"):
            state.goal_progress = self._clamp(state.goal_progress + 0.01)
        elif state.profile.life_goal == "social" and activity in ("socialize", "leisure"):
            state.goal_progress = self._clamp(state.goal_progress + 0.02)
        for key in NEED_KEYS:
            setattr(state, key, self._clamp(getattr(state, key)))
        if activity != state.current_activity or location != state.location_id:
            previous = state.current_activity
            state.current_activity = activity
            state.location_id = location
            state.last_transition_minute = self.simulation_minute
            self._record(state, "activity_changed", {"from": previous, "to": activity, "location_id": location})

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
            self.events = []
            self.commands = []
            for _ in range(minutes):
                self.simulation_minute += 1
                for state in self.npcs.values():
                    self._tick_npc(state)
            result = {"simulation_minute": self.simulation_minute, "advanced_minutes": minutes,
                      "npc_count": len(self.npcs), "events": list(self.events)}
            request_result = (dedup_key, result) if dedup_key else None
            self.store.save(self.simulation_minute, list(self.npcs.values()), self.events, request_result, self.commands)
            return result

    def interact(self, npc_id: str, player_id: str, interaction: str = "talk",
                 request_id: Optional[str] = None) -> dict:
        if interaction != "talk":
            raise ValueError("unsupported_interaction")
        if not player_id or len(player_id) > 128:
            raise ValueError("invalid_player_id")
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
            state.affinity[player_id] = self._clamp(old + 2.0)
            state.social = self._clamp(state.social + 12.0)
            memory = {"minute": self.simulation_minute, "kind": "player_conversation",
                      "participant_id": player_id, "summary": "Player spoke with NPC"}
            state.memories.append(memory)
            state.memories = state.memories[-100:]
            self.events = []
            result = {"npc_id": npc_id, "interaction": interaction, "affinity": state.affinity[player_id],
                      "social_need": state.social, "memory_recorded": True,
                      "response_context": {"name": state.profile.display_name,
                                           "occupation": state.profile.occupation,
                                           "activity": state.current_activity}}
            self._record(state, "conversation", {"player_id": player_id, "interaction": interaction})
            request_result = (dedup_key, result) if dedup_key else None
            self.store.save(self.simulation_minute, [state], self.events, request_result)
            return result

    def pending_commands(self, limit: int = 50) -> List[dict]:
        return self.store.pending_commands(limit)

    def complete_command(self, command_id: str, result: dict) -> bool:
        if not isinstance(command_id, str) or not command_id or len(command_id) > 128:
            raise ValueError("invalid_command_id")
        if not isinstance(result, dict) or result.get("success") is not True:
            raise ValueError("command_success_required")
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
                    "needs": {"hunger": s.hunger, "energy": s.energy, "social": s.social, "stress": s.stress},
                    "relationships": dict(s.affinity), "memory_count": len(s.memories),
                } for s in sorted(self.npcs.values(), key=lambda item: item.profile.npc_id)],
            }
