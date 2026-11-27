"""Game-neutral contracts. Never import a base-game SDK in this module."""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Dict, FrozenSet, Mapping, Optional, Tuple


CONTRACT_VERSION = "1.0"


@dataclass(frozen=True)
class Vector3:
    x: float
    y: float
    z: float


@dataclass(frozen=True)
class ActorObservation:
    """A snapshot of a native actor; native handles must not escape the adapter."""
    actor_id: str
    position: Vector3
    heading: float = 0.0
    is_player: bool = False
    native_type: str = "actor"
    tags: Tuple[str, ...] = ()


@dataclass(frozen=True)
class WorldObservation:
    adapter_id: str
    game_session_id: str
    observed_at: float
    player_actor_id: str
    actors: Tuple[ActorObservation, ...] = ()
    location_id: Optional[str] = None
    world_time_seconds: Optional[float] = None


@dataclass(frozen=True)
class AdapterCapabilities:
    adapter_id: str
    adapter_version: str
    base_game: str
    base_game_build: str
    contract_version: str = CONTRACT_VERSION
    capabilities: FrozenSet[str] = frozenset()
    max_embodied_actors: int = 32
    max_active_interactions: int = 8


@dataclass(frozen=True)
class EngineCommand:
    """Semantic action requested by the engine, not a native game function."""
    command_id: str
    kind: str
    target_id: Optional[str] = None
    parameters: Mapping[str, Any] = field(default_factory=dict)
    correlation_id: Optional[str] = None


@dataclass(frozen=True)
class CommandResult:
    command_id: str
    success: bool
    outcome: str
    details: Mapping[str, Any] = field(default_factory=dict)
    error_code: Optional[str] = None


@dataclass(frozen=True)
class WorldEvent:
    event_id: str
    event_type: str
    occurred_at: float
    actor_id: Optional[str] = None
    target_id: Optional[str] = None
    payload: Mapping[str, Any] = field(default_factory=dict)
    causation_id: Optional[str] = None
    correlation_id: Optional[str] = None
