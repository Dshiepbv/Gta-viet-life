# ADR-0002 — Single Launcher with Independent Simulation Host and In-Game Adapter

- **Status:** Accepted target architecture
- **Date:** 2026-11-27
- **Decision owners:** GTA Viet Life project
- **Related:** `LAUNCHER_AND_UPDATE_ARCHITECTURE.md`, `LIFE_SIMULATION_ENGINE.md`, `ENGINE_ADAPTER_PROTOCOL.md`, `ENGINEERING_DELIVERY_POLICY.md`

## Context and required outcome

The player wants one simple workflow: open one application, let it check/update/start the required components, see an explicit ready state, then play GTA V. The player must not manually copy C# scripts, run Python, start a local server or assemble separate modules.

The project also requires a portable life-simulation engine that can eventually support more than one base game. NPCs and world systems must continue to evolve off-screen, while physical actions in GTA V must be executed by the game itself.

## Options considered

### A. All logic inside the GTA V C# plugin

**Advantages**
- Direct access to native game APIs and game-thread execution.
- One process to inspect during a game session.
- No local service protocol for the initial implementation.

**Disadvantages**
- Simulation lifecycle is coupled to GTA and its mod runtime.
- Off-screen simulation stops when the game closes unless a separate persistence scheduler is added.
- AI/provider integration, data processing and long-running services become harder to isolate.
- A future second game requires extracting domain logic from the first adapter.
- Updating the entire system is tied to game/plugin deployment constraints.

**Assessment:** Not selected as the long-term architecture. Keep the Adapter thin and preserve existing modules during migration.

### B. One standalone Launcher containing the entire simulation and game integration

**Advantages**
- Simple player-facing concept.
- A single desktop application can own update and startup UX.

**Disadvantages**
- A desktop UI process is not a reliable authoritative simulation service.
- The Launcher cannot directly manipulate in-game entities without a plugin loaded inside GTA.
- Combining UI, simulation, persistence, update logic and game integration increases blast radius and complicates restart/recovery.
- A crash in the UI can unnecessarily interrupt simulation and orchestration.

**Assessment:** Rejected. The Launcher is an orchestrator, not the simulation engine or game adapter.

### C. Launcher + independent Engine Host + game-specific Adapter

**Advantages**
- One simple entry point for the player while preserving clean process boundaries.
- Engine Host owns authoritative state, persistence and off-screen simulation.
- Adapter owns game-native entity/action execution and reports confirmed outcomes.
- Versioned handshake and capability negotiation make incompatibilities explicit.
- A future adapter can connect to the same game-neutral engine without moving domain rules into another game's SDK.
- Launcher can update, repair, back up and roll back components independently.
- Host can be tested with deterministic offline tests without requiring GTA to run.

**Costs and risks**
- Requires a secure local IPC/HTTP protocol, lifecycle supervision and version compatibility policy.
- More moving parts than an all-in-one plugin.
- Installer/updater must handle interrupted transactions, locked game files and rollback.
- Engine/Adapter protocol and session semantics must be designed before gameplay commands are expanded.

**Assessment:** Selected. It is the smallest architecture that satisfies both the single-entry player workflow and the long-term portable simulation-engine goal.

## Comparison with established engine patterns

This decision borrows architectural properties, not proprietary implementation details:

- **Unreal Engine / Unity:** use a clear separation between engine runtime, game-specific code and editor/tooling/build/deployment workflows. GTA Viet Life applies the same boundary principle: the Launcher manages lifecycle; the Adapter speaks the game's API; the domain engine stays game-neutral.
- **Godot:** its scene/runtime integration is tightly coupled to the engine API. This illustrates why game-native behavior belongs in a per-game adapter rather than being assumed portable.
- **Creation Engine / Script Extender mod ecosystems:** game extensions execute inside a host game process and depend on exact game/runtime APIs. GTA V's Adapter must therefore be version-pinned and compatibility-checked; an external Launcher cannot substitute for an in-process extension.
- **ECS/data-oriented simulation:** useful for large populations and high-frequency state updates, but it does not itself solve desktop lifecycle, game integration or update safety. ECS may be adopted inside the Host if profiling justifies it; it is not mandated at the application boundary.
- **Event-driven/actor-style simulation:** useful for independent NPCs, businesses and world events, but requires deterministic authority, persistence and idempotent delivery. These remain explicit Engine Host responsibilities.

No claim is made that these engines share one identical architecture or that their private internals were inspected. The comparison concerns publicly documented architectural patterns and integration constraints.

## Decision

Adopt Option C:

1. **GTA Viet Life Launcher** — one player-facing desktop application; installation checks, update, rollback, process lifecycle, readiness and diagnostics.
2. **Life Simulation Engine Host** — independent authoritative simulation and persistence process.
3. **GTA V Adapter** — game-specific in-process plugin that translates semantic commands and reports actual outcomes.

The Launcher may start GTA V or wait for the player to start it, depending on verified mod-runtime support. It must not assume it can inject or load a plugin through undocumented mechanisms.

## Critical design constraints

- READY requires a healthy Host and a live, compatible Adapter handshake from the active game session.
- Local communication is loopback-only by default and authenticated.
- Updates are signed/verified, staged and rollback-capable.
- Player saves/configuration are never treated as replaceable application files.
- The Host is authoritative; AI suggestions and Adapter observations are validated before changing persistent domain state.
- Game-native handles remain Adapter-local.
- Existing `GVL.*` contracts and saves remain compatible through explicit migration.
- No second adapter or portable-engine claim is considered proven until an independently implemented adapter passes the same contract tests.
- No Launcher/installer/updater is considered implemented until actual artifacts build and pass the acceptance criteria.

## Consequences

Positive:
- Simple player workflow without sacrificing engine portability.
- Failure isolation between UI, simulation and game runtime.
- Explicit readiness and diagnosable compatibility.

Negative:
- Additional IPC, process supervision and packaging work.
- A staged migration is required from current monolithic scripts and Python bridge.
- Actual game-runtime verification remains necessary for the final acceptance candidate.

## Acceptance gates

Use the 12 measurable gates in `LAUNCHER_AND_UPDATE_ARCHITECTURE.md`. The current repository state is architecture-approved only; it is not a completed Launcher release.
