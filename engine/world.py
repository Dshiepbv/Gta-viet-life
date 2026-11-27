"""Minimal deterministic simulation host; deliberately independent of game APIs."""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict, List, Optional

from .contracts import ActorObservation, WorldEvent, WorldObservation


@dataclass
class ActorState:
    actor_id: str
    x: float
    y: float
    z: float
    heading: float
    last_observed_at: float
    is_player: bool = False
    tags: tuple = ()


@dataclass
class WorldState:
    simulation_time: float = 0.0
    tick: int = 0
    actors: Dict[str, ActorState] = field(default_factory=dict)
    last_observation_at: Optional[float] = None


class WorldEngine:
    """Owns simulation time and canonical observations, not native game entities.

    Domain systems will be registered as deterministic tick processors in later
    milestones. Observations are facts reported by an adapter; they are not
    silently promoted into persistent NPC identity or long-term world state.
    """

    def __init__(self, fixed_step_seconds: float = 1.0) -> None:
        if fixed_step_seconds <= 0:
            raise ValueError("fixed_step_seconds must be positive")
        self.fixed_step_seconds = float(fixed_step_seconds)
        self.state = WorldState()
        self._accumulator = 0.0
        self._events: List[WorldEvent] = []

    def ingest(self, observation: WorldObservation) -> None:
        if not observation.adapter_id or not observation.game_session_id:
            raise ValueError("observation must identify its adapter and game session")
        if self.state.last_observation_at is not None:
            if observation.observed_at < self.state.last_observation_at:
                raise ValueError("out-of-order world observation")
        for actor in observation.actors:
            self._ingest_actor(actor, observation.observed_at)
        self.state.last_observation_at = observation.observed_at

    def _ingest_actor(self, actor: ActorObservation, observed_at: float) -> None:
        if not actor.actor_id:
            raise ValueError("actor_id cannot be empty")
        self.state.actors[actor.actor_id] = ActorState(
            actor_id=actor.actor_id,
            x=actor.position.x,
            y=actor.position.y,
            z=actor.position.z,
            heading=actor.heading,
            last_observed_at=observed_at,
            is_player=actor.is_player,
            tags=tuple(actor.tags),
        )

    def advance(self, elapsed_seconds: float) -> int:
        """Advance fixed simulation steps; return the number of executed ticks."""
        if elapsed_seconds < 0:
            raise ValueError("elapsed_seconds cannot be negative")
        self._accumulator += float(elapsed_seconds)
        steps = 0
        # A safety ceiling prevents a huge catch-up burst after a debugger pause.
        # Offline catch-up will be implemented as a separate bounded policy.
        while self._accumulator + 1e-9 >= self.fixed_step_seconds and steps < 300:
            self._accumulator -= self.fixed_step_seconds
            self.state.simulation_time += self.fixed_step_seconds
            self.state.tick += 1
            steps += 1
        return steps

    def snapshot(self) -> WorldState:
        """Return a detached snapshot so callers cannot mutate authoritative state."""
        return WorldState(
            simulation_time=self.state.simulation_time,
            tick=self.state.tick,
            actors={
                key: ActorState(
                    actor_id=value.actor_id,
                    x=value.x,
                    y=value.y,
                    z=value.z,
                    heading=value.heading,
                    last_observed_at=value.last_observed_at,
                    is_player=value.is_player,
                    tags=tuple(value.tags),
                )
                for key, value in self.state.actors.items()
            },
            last_observation_at=self.state.last_observation_at,
        )

    def drain_events(self) -> List[WorldEvent]:
        events, self._events = self._events, []
        return events
