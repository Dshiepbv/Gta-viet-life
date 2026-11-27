"""Base-game adapter interface expressed only in game-neutral types."""
from __future__ import annotations

from abc import ABC, abstractmethod
from typing import Iterable, Sequence

from ..contracts import (
    AdapterCapabilities,
    CommandResult,
    EngineCommand,
    WorldObservation,
)


class GameAdapter(ABC):
    @abstractmethod
    def capabilities(self) -> AdapterCapabilities:
        """Return the exact capabilities and runtime/build identity."""

    @abstractmethod
    def observe(self) -> WorldObservation:
        """Return a game-neutral snapshot; never expose native handles."""

    @abstractmethod
    def execute(self, commands: Sequence[EngineCommand]) -> Sequence[CommandResult]:
        """Execute semantic commands and report confirmed outcomes."""

    @abstractmethod
    def close(self) -> None:
        """Release adapter resources and native event subscriptions."""
