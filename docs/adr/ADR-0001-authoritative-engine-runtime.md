# ADR-0001 — Authoritative simulation runtime and domain ownership

- **Status:** Accepted for the first integrated release; scale decision is measurable and revisitable
- **Date:** 2026-11-27
- **Scope:** M0 — independent Life Simulation Engine
- **Related:** LIFE_SIMULATION_ENGINE.md, ENGINE_ADAPTER_PROTOCOL.md, ENGINEERING_GOVERNANCE.md

## Problem and observable outcome

GTA Viet Life needs one authoritative world state that continues to advance when the player is away, persists NPC identity and consequences, and can serve multiple base-game adapters. No game SDK or AI provider may mutate canonical facts directly. The first implementation must be deployable by the existing Windows Launcher and testable without GTA running.

## Constraints and repository facts

- The current Launcher already packages and supervises a Python Engine Host.
- The existing cognition/voice bridge is also Python but remains a separate process and must not own simulation truth.
- GTA V integration is C# / ScriptHookVDotNet and must remain a thin in-process adapter.
- The engine must remain game-neutral and preserve existing GVL.* modules and text saves during staged migration.
- Current target workloads are bounded minute-step life simulation, not high-frequency physics or rendering.
- The repository already has deterministic Python domain tests and SQLite persistence; replacing the runtime now would incur migration risk without measured evidence of a bottleneck.

## Alternatives

### A. C#/.NET authoritative Host

Advantages: strong static typing, natural alignment with the GTA adapter and Windows deployment, good throughput and explicit contracts. Disadvantages: requires migrating the existing Python kernel and its tests, while AI cognition still needs an IPC boundary. It does not remove the need for SQLite migrations, adapter contracts or deterministic simulation tests.

### B. Python authoritative Host (selected for initial release)

Advantages: existing packaged Host, mature deterministic test support, SQLite and data tooling, low migration cost, and clear separation from the C# adapter. Minute-step simulation can be profiled and optimized before considering a language migration. Disadvantages: dynamic typing requires strict runtime validation and schema/contract tests; a naive per-NPC/per-minute loop will not scale indefinitely.

### C. Split authoritative state across C# and Python

Rejected. Two writers create conflict resolution, ordering, replay and recovery complexity. The cognition bridge may propose dialogue or plans, but it must not mutate canonical facts.

## Comparison with established patterns

- Unreal MassEntity / Unity Entities: data-oriented storage and batched processors are useful if profiling demonstrates large populations or high-frequency updates. They do not require moving authority into a game adapter. The initial Python Host will use bounded batches and deterministic processors; ECS-like storage is a future optimization, not a prerequisite.
- Bevy ECS / event-driven simulation: explicit schedules and event queues clarify system ordering and causality. GTA Viet Life adopts deterministic system ordering, stable IDs, an event journal and idempotent commands without depending on Bevy.
- Authoritative server / thin-client pattern: the Host owns facts; adapters observe and execute; AI proposes only. This is the governing boundary across both GTA and Godot.

## Decision

Use the existing Python Engine Host as the sole authoritative simulation owner for M0–M5. Use SQLite transactions and additive schema migrations. Keep cognition in a separately supervised bridge. Keep native game execution in per-game adapters. No second process may independently write the same canonical world facts.

The first scale target is an explicitly bounded resident population and minute-step processing. CI will include deterministic load/replay tests. If measured p95 simulation time, memory use or lock contention exceeds the documented budget, revisit C# or a data-oriented implementation through a new ADR; do not split authority.

## Failure modes and safeguards

- Host unavailable: adapter does not fabricate success; legacy game features remain usable and events queue locally where supported.
- Duplicate request or command: stable IDs and transactional deduplication prevent duplicate consequences.
- Host restart: SQLite state and pending commands survive restart.
- AI provider unavailable or hallucinates: deterministic context/fallback remains; AI output cannot directly change money, ownership, needs or relationships.
- Adapter disconnect: off-screen simulation continues according to the authoritative clock policy; physical commands remain pending until terminal result.
- Performance degradation: bounded catch-up, batch limits, profiling and explicit population caps.

## Pre-mortem / counter-argument

Python may become a bottleneck once city-wide population, traffic and businesses are simulated at fine granularity. Choosing it now could create a future migration. The countermeasure is to keep domain rules independent of Python-specific libraries, define typed JSON contracts, measure representative loads, and migrate only the Host implementation if benchmarks prove the need. This risk is preferable to introducing two authoritative writers before the product has a stable domain model.

## Acceptance gates

1. Engine imports no GTA, SHVDN, Godot, Unity or Unreal SDK.
2. Exactly one Host owns canonical NPC/world/economy state.
3. Replaying the same request/command IDs cannot duplicate effects.
4. Restart restores snapshots, event journal, clock and pending commands.
5. Schema migrations are additive and tested from every supported prior version.
6. Adapter contract tests cover handshake, observation, command, result, disconnect and reconnect.
7. A representative population/load test reports runtime and memory budgets.
8. The Host is packaged and launched by the single Launcher; no manual Python setup is required for the player.

This ADR selects the runtime for the first integrated release; it does not claim all M0 acceptance gates are already passed.