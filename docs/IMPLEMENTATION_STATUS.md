# GTA Viet Life — architecture audit and implementation status

Date: 2026-11-27  
Working branch: `main`  
Baseline: `main` (existing GTA V Enhanced single-player mod)

## Compatibility constitution

The repository's existing constitution is `docs/CORE.md`. Its compatibility rules remain authoritative:

1. Keep modules independently loadable as ScriptHookVDotNet scripts.
2. Preserve the `GVL.*` AppDomain data contract.
3. Preserve legacy per-module `key=value` save files; add versioned keys rather than renaming old keys.
4. Keep SQLite in the Python bridge; in-game modules continue to use text saves.
5. A missing optional core must not crash a feature module.
6. All monetary flows must use the shared payment/earning delegates where those delegates are available.

This change does not replace the monolithic game modules or migrate player data.

## Architecture audit — 20 development areas

Status labels: **Existing** means relevant code is present; **Partial** means important integrity, coverage, or verification gaps remain; **Not verified** means source/runtime evidence is insufficient to claim completion.

| # | Area | Existing baseline | Status / next engineering boundary |
|---:|---|---|---|
| 1 | Core engine and modular architecture | Core script, independent feature scripts, `GVL.*` contract | Partial — shared contract exists; dependency ownership and module lifecycle need formalization |
| 2 | Transaction engine and financial integrity | Economy payment delegate, cash/bank ledger, loans | Partial — some modules still have independent money paths; no cross-module transaction journal |
| 3 | Save, backup and recovery | Separate text saves; some modules write directly | Improved in Core — atomic replacement, backup fallback, durable event outbox; other module saves remain to audit |
| 4 | Time and offline simulation | Game clock and per-module catch-up logic | Partial — separate day tracking and caps can diverge |
| 5 | Persistent event bus | AppDomain list + sequence and bridge forwarding | Improved in Core — persisted outbox and retry-after-failure; delivery is at-least-once |
| 6 | Security and synchronization | Localhost AI bridge, optional dashboard PIN, Apps Script key/PIN | Partial — deployment secrets and transport hardening require review |
| 7 | Economic balancing and channel pricing | GT/MT, wholesale/retail, product costs and discounts in World | Partial — values are distributed across source and INI files |
| 8 | Income, purchasing power and living expenses | Jobs, shops, rent, utilities, services and bills | Partial — no central Vietnamese household/economic parameter model |
| 9 | Business and replenishment | Shops, stock, employees, supplier orders and B2B | Partial — automation and stock valuation need unified invariants |
| 10 | Supply, demand and competition | Customer flow and business simulation exist | Not verified — no unified market-clearing or competitor model established |
| 11 | Banking, credit and taxation | Bank balance, loans, repayment, credit score, taxes | Partial — tax and debt policies are not centralized or comprehensively tested |
| 12 | Careers and labor market | Delivery work, employee hiring and wages | Partial — no persistent labor-market matching model established |
| 13 | Personal needs and health | Needs, hygiene, illness, injury, sleep and stress | Existing / partial — features exist, balance and cross-module effects need verification |
| 14 | Housing, vehicles and assets | Property, rentals, personal garage, fuel, wear and impound | Existing / partial — asset ownership and valuation need a common registry |
| 15 | Family and social relationships | NPC affinity, dating, marriage, contacts and pets | Existing / partial — relationship state spans game and bridge persistence |
| 16 | NPC AI, memory and behavior | Python LLM bridge, SQLite memory, schedules and actions | Existing / partial — AI provider reliability, memory lifecycle and deterministic fallback need tests |
| 17 | Persistent society and world simulation | Residents, schedules, traffic rhythm, rumors and random events | Partial — population scale and offline world causality are not verified |
| 18 | Dashboard, Android and cloud | Python dashboard, Android WebView, Apps Script relay | Existing / partial — authentication, conflict handling and command idempotency need hardening |
| 19 | System-wide performance | Tick-based modules, cached model lists, background bridge calls | Not verified — no profiler baseline or repeatable performance suite |
| 20 | Release packaging and versioning | Install scripts and Android Actions build | Partial — target is now a single Windows Launcher + managed Engine Host + in-game Adapter; implementation, updater, installer and compatibility matrix are not yet present |

## Changes delivered on this branch

### Core persistence

- Kept the original `GtaVietLifeCore.sav` filename and legacy `day=<integer>` field.
- Added `seq` and Base64-encoded `outbox` fields. Older saves without these fields remain readable.
- Writes to a temporary file, preserves the previous save as `GtaVietLifeCore.sav.bak`, then atomically replaces the active save.
- If the active save cannot be read, attempts recovery from the backup.
- Restores the event sequence on startup to avoid reusing sequence IDs after a restart.

### Event delivery

- The core no longer clears pending events before the HTTP request.
- Sends bounded batches and removes the acknowledged prefix only after a successful bridge response.
- Retains events on network failure and persists the pending queue.
- Delivery semantics are **at-least-once**, not exactly-once. A crash after the bridge accepts a batch but before local acknowledgement can replay it. Downstream consumers must deduplicate using the leading sequence number.

## Migration and rollback

- No conversion of existing game, economy, personal, world, resident, or NPC data is performed.
- Existing `day=` core saves are read as before.
- New core saves add optional `seq=` and `outbox=` keys; older builds ignore unknown keys only if they parse selectively. To roll back, keep the generated `.bak` and restore the prior core script/save pair together.
- The new event outbox is local to the game working directory. Back it up with the other `GtaVietLife*.sav` files.
- Do not run this mod in GTA Online. This project remains single-player only.

## Verification and release gates

The repository does not currently provide a game-mod solution/project or an automated GTA V runtime harness. The Android workflow is not evidence that the C# ScriptHookVDotNet scripts compile or behave correctly in GTA V Enhanced.

Before promoting this branch to a release:
- Compile all game scripts against the exact installed ScriptHookVDotNet Enhanced API/runtime.
- Test loading legacy saves, backup recovery, disk-full/permission failures, and abrupt process termination.
- Simulate bridge unavailable, timeout, HTTP error, and bridge-accepted/local-crash cases; verify at-least-once replay and sequence deduplication.
- Run a 24-hour accelerated in-game simulation and compare balances, inventory, payroll, tax, debt and asset totals.
- Verify the Android APK and Apps Script command path with PIN enabled and disabled.
- Confirm no GTA Online entry path is enabled.

## Explicitly unresolved

This branch is a foundational hardening increment, not completion of all 20 development areas and not yet a release candidate. The largest remaining architectural work is a unified transaction journal, one authoritative simulation clock, central economic configuration with Vietnamese regional/household profiles, idempotent dashboard commands, schema/version migration for every save file, and repeatable game-runtime tests.


## Approved player workflow — single Launcher

The target player experience is now formally specified in `docs/LAUNCHER_AND_UPDATE_ARCHITECTURE.md`:

**Open GTA Viet Life Launcher → automatic preflight and safe update → start Engine Host → wait for GTA V Adapter handshake → show READY → play.**

The Launcher is the single player-facing orchestrator. It manages installation, update/rollback, Engine Host lifecycle, game/mod-runtime detection, diagnostics and backups. The Engine Host owns simulation and persistence. The GTA V Adapter remains a required in-game plugin and is responsible for executing physical game actions.

### Readiness and update guarantees

- READY is gated on verified package integrity, compatible game/mod-runtime versions, healthy Engine Host, successful schema check, loaded Adapter, compatible protocol, required capabilities and a successful session handshake/heartbeat.
- Updates are staged, signature/hash verified, applied transactionally and rolled back to the last known-good version on failure.
- Saves, user configuration and custom content are preserved separately from application binaries.
- Offline startup may use the last verified compatible release according to security policy; Launcher must disclose that update checks are unavailable.
- The Launcher does not replace the in-game Adapter and must not claim readiness based only on its own process or the Host process.

### Implementation status

**Architecture approved; product implementation not yet delivered.** No working Launcher EXE, installer, automatic updater, packaged Engine Host or complete GTA Adapter handshake is claimed by this documentation change. Technology choices remain subject to verification against the actual Windows baseline, GTA V build and mod-runtime API. User-facing alpha testing remains prohibited under the delivery policy.


## M2 implementation increment — deterministic NPC life kernel

Date: 2026-11-27

The branch now contains the first persistent NPC life-simulation implementation in `engine/life_simulation.py`:

- Game-neutral NPC profiles with stable IDs, occupation, home location, traits and validated non-overlapping daily schedule blocks.
- Deterministic minute-step needs for hunger, energy, social connection and stress, plus career/health/social life goals with bounded progress.
- Schedule-driven activities and need-driven overrides (eating, sleeping and socializing).
- Durable relationship affinity and bounded episodic memory for player conversations.
- SQLite schema version 1, transactional NPC snapshots, simulation clock and idempotent event IDs.
- Authenticated Host endpoints to register NPC profiles, advance simulation time, interact with an NPC and inspect a canonical snapshot; mutating simulation requests are deduplicated by request ID and persisted with the state transaction.
- Host background clock advances one simulated minute per two wall-clock seconds while the Host is running. On restart it recovers at most 10,080 simulated minutes (seven in-game days) from the persisted wall-clock checkpoint.

### GTA legacy resident profile bridge

The GTA Core now reads the existing `GtaVietLifeResidents.txt` roster and imports stable resident IDs, names and a normalized daily schedule into the independent Engine Host. This is profile synchronization only. The legacy World module remains responsible for current physical Ped scheduling until semantic commands, location mapping and execution results are connected.

### Honest boundary

This is the first engine-side NPC vertical-slice foundation, not a completed GTA gameplay slice. No production resident catalog is seeded by default. The GTA Core/Adapter does not yet submit NPC profiles, map semantic home/work locations to verified GTA locations, poll semantic actor commands or report physical execution outcomes. NPCs therefore exist in the independent simulation store and are not yet guaranteed to appear or act as physical GTA entities. Dialogue generation is also not wired; conversation currently records a deterministic interaction/memory and returns bounded context only.

### Verification required

- Engine unit tests cover schedule validation, needs, activity selection, relationship changes, persistence/reload and Host API flow.
- Windows CI must compile Python modules and execute the full engine test suite before this increment is considered internally verified.
- Live GTA acceptance remains blocked until the Adapter's observation/command/result path and verified location mappings are implemented.


## NPC simulation increment — durable adapter command outbox

The Engine Host now emits a durable semantic command whenever an NPC changes activity or semantic location:

- SQLite schema v3 adds `command_outbox`, with stable command IDs, target NPC IDs, typed payloads, creation minute and pending/completed state.
- The first command type is `actor.set_activity`; its payload includes the new activity, semantic location ID and transition minute.
- `GET /engine/v1/adapter/commands` returns pending commands (bounded batch size).
- `POST /engine/v1/adapter/commands/ack` records successful execution only.
- Commands remain pending until acknowledged, giving at-least-once delivery across Host restart. The eventual GTA adapter must deduplicate by command ID before performing non-idempotent physical effects.
- Migration from schema v1/v2 to v3 is automatic and additive; existing NPC, event and request-dedup data is retained.

**Important current boundary:** this is a working engine-to-adapter command queue, not yet an end-to-end in-world control path. The GTA V script has not yet polled these commands or executed them against resident Peds. Existing physical resident movement remains controlled by `GtaVietLifeWorld.cs`. Current resident locations are generated/derived by legacy gameplay code (home and lunch positions are randomized street locations); they are not yet a verified city-wide location registry. Do not describe NPCs as Engine-controlled in GTA until the adapter executor, location mapping and observed execution acknowledgement are implemented and runtime-verified.

Automated verification is being added for command creation, persistence across restart, acknowledgement and duplicate acknowledgement. Live GTA V runtime acceptance remains unavailable in the current build environment.


## GTA Adapter command execution increment

The adapter path now includes an initial physical execution loop for resident activity commands:

- GTA Core polls the authenticated Engine Host command outbox every two seconds and places unique command IDs into a shared in-process queue.
- GTA World consumes commands on its main ScriptHookVDotNet tick, resolves the resident by canonical `res_<id>`, and maps only the known `.home`, `.work`, `.lunch` and `.leisure` semantic location IDs to the resident's actual saved GTA coordinates.
- The adapter issues a nav-mesh movement task to the mapped destination, or a bounded wander task when already nearby. It acknowledges only after the task is issued. Commands for NPCs that are not currently materialized, are frozen in conversation, or are following the player are deferred and rotated behind other work so one off-screen resident cannot block all other residents.
- The Engine coalesces pending `actor.set_activity` commands per NPC to the latest desired state; this prevents a large stale movement backlog from accumulating while the adapter is disconnected.
- Once the first Engine command is applied, the resident's old time-of-day movement controller is bypassed; Engine activity commands become the schedule authority for that resident. If the Ped is not materialized, the command remains pending until it is available.
- On resident Ped respawn, the last Engine activity/location is reapplied.

**Scope boundary:** this is the first physical command loop for the existing resident feature, not a complete NPC behavior system. Activity-specific animations/scenarios, path completion observation, command leases/expiry/failure taxonomy, robust handling for unknown IDs, city-wide location registry, interiors, vehicles, arbitrary ambient NPCs and off-screen population materialization remain future work. The adapter currently acknowledges successful task issuance, not proof that the Ped reached its destination. No live GTA V runtime test has been performed in this environment.

Release CI now includes a separate compile-check project for `GtaVietLifeWorld.cs` against the same pinned SHVDN Enhanced API, rather than compiling only GTA Core. This closes a previous verification gap where the World script was staged in packages but not independently compiled by CI.


## Main integration update — 2026-11-27

- PR #11 was squash-merged into `main` as `4819d89`; subsequent integration work is performed directly on `main`.
- Windows verification is now triggered by pushes and pull requests targeting `main`, as well as manual dispatch and version tags.
- The GTA Core reports an absolute in-game world minute in the adapter handshake and heartbeat. While a clock-aware adapter is live, the Engine Host pauses its wall-clock worker and advances NPC simulation only by observed positive GTA clock deltas. A paused GTA clock therefore does not advance NPC needs; a backward clock correction never rewinds persisted NPC history. Heartbeats refresh the offline checkpoint. Legacy adapters without the clock field retain wall-clock behavior.
- This clock policy is internally covered by Host tests; actual GTA pause/sleep/time-jump behavior still requires live-game acceptance and is not claimed verified.


## Adapter clock and command-result integration — 2026-11-27

- The GTA Core now schedules Engine Host heartbeats with `Stopwatch.GetTimestamp()` (monotonic process time), not `Game.GameTime`, so the heartbeat cadence is independent of a paused game clock.
- Clock-aware adapter sessions pause the Host wall-clock worker; heartbeat deltas drive NPC simulation. The first live observation aligns an empty Engine database to the GTA world minute. Persisted NPC history is never rewound by a backward clock correction.
- Adapter command reports now persist terminal outcomes (`completed`, `failed`, `unsupported`, `rejected`, `cancelled`). Unknown command types and unmapped resident activity/location combinations are reported as `unsupported` instead of being silently discarded or retried forever. Temporarily unavailable NPCs remain deferred.
- These paths are covered by Engine Host tests and compile checks. Actual pause behavior and in-world command outcomes remain unverified until live GTA V acceptance.


## Shared cash transaction integration — 2026-11-27

- Added `GVL.PayCash` and `GVL.EarnCash` delegates alongside the existing bank-aware `GVL.Pay` / `GVL.Earn` contract. Cash-only flows retain their original cash semantics and do not silently debit or credit bank accounts.
- The Economy module registers cash-only delegates that write to its local ledger and emit the shared `ledger.expense` / `ledger.income` events. Core supplies compatible defaults when Economy is absent.
- The World module's direct cash income/expense paths (business revenue, wages, taxes, rents, fines, property, inventory, delivery, taxi, work and lifestyle spending) now route through the shared cash delegates. Personal shopping, vehicle delivery and backpack purchases do the same. Remaining direct assignments in those modules are only the guarded fallback implementations or wallet restoration.
- The bank-aware delegate remains available for services that intentionally support card/bank payment. The old Economy module's own bank transfers and loan accounting continue to write their own ledger entries.
- This is a shared in-game transaction/ledger integration, not yet a single SQLite transaction journal across every game save. Durable cross-module reconciliation and exhaustive economic balance testing remain outstanding.

## M5 — second-game adapter portability proof (2026-11-27)

A Godot 4 reference adapter has been added under `adapters/godot/`. It uses the existing authenticated `gvl-engine/1` Host endpoints without importing GTA/SHVDN or copying NPC schedule, needs, relationship, memory or persistence rules. It registers one stable NPC, polls the same durable `actor.set_activity` outbox, maps semantic home/work/park IDs to Godot scene positions, moves a native Node2D actor and acknowledges only after the movement tween reaches its destination. The player can approach the resident and invoke the shared talk interaction; the Engine Host persists affinity and episodic memory in the same SQLite store.

A Host integration test now exercises the second adapter identity through handshake, profile registration, schedule transition, command poll/ack and interaction. This proves the Host contract is game-neutral at the HTTP/domain boundary and that the same domain path is reusable. Windows CI now downloads the pinned Godot 4.4.1 release, verifies its SHA-512 against the official release manifest and runs a headless editor import to parse the project/scripts. This validates project import and GDScript parsing, but it does not prove rendered gameplay, interactive input, HTTPRequest behavior against a live Host or an actual Godot-to-Host session; those remain runtime acceptance gates. M5 is therefore **implementation-complete as a second-adapter source/integration proof, but runtime acceptance remains pending**. Do not label the overall product release-ready until the Godot runtime gate and the separate GTA V live-game gate are completed.

The Godot adapter is intentionally excluded from the GTA Windows installer; it is a developer portability reference and does not alter GTA save files or legacy `GVL.*` modules.


## Consolidated M0–M5 implementation baseline (main, 2026-11-27)

This section supersedes milestone shorthand in earlier incremental notes. All changes below are committed directly to main as requested. A source-level or CI pass is not a substitute for launching the game.

| Milestone | Integrated implementation now present | Remaining acceptance boundary |
|---|---|---|
| **M0 — Engine foundation** | ADR-0001 selects one authoritative Python Host; SQLite shared state; additive world tables; deterministic and game-SDK import-boundary tests; 250-NPC × 360-minute load baseline. | The CI benchmark must run on Windows; scale budget must be recorded from the completed run. |
| **M1 — GTA adapter and Host protocol** | Authenticated handshake/heartbeat; game-clock alignment; durable NPC command outbox; separately persisted GTA event outbox; cash/bank opening balance sync; command and result reporting. | No live GTA V session has been executed in this environment. |
| **M2 — NPC life and cognition** | Validated resident identity, household, age, biography, skills, provenance-aware knowledge, needs, goals, schedules, off-screen NPC-to-NPC social interactions, bounded episodic memories, actual voice transcript/reply memory, Engine-validated affinity delta, dialogue context delivered to the Vietnamese cognition bridge, and GTA activity-specific scenario issuance after physical arrival. | Actual game dialogue, animation/scenario availability and long-session memory behavior require live-game acceptance. |
| **M3 — Physical world interactions** | Versioned building/object catalog; adapter-local semantic mappings; durable world command outbox; GTA home entry/exit, bed/fridge/sofa interaction, mapped workplace shift start, and dynamic physical storefront registration/selection; Godot doors/interiors/objects also use the same Host world-command contract. | GTA interior collision, each home IPL, storefront prop/door geometry and physical interaction must be checked in the target game build. |
| **M4 — Society, property, economy and traffic** | Double-entry idempotent ledger; separate cash/bank accounts; legacy GVL ledger event ingestion; property ownership/lease and expiry; business accounts/inventory/sales; traffic congestion, incidents, time-based recovery and stress consequences for NPCs on affected road segments. | Existing legacy business/property cash flows are not yet fully reconciled into every Engine business account; full live economy parity and traffic-to-GTA world feedback remain acceptance items. |
| **M5 — Second-game portability** | Godot 4 reference adapter uses the same Host for NPC state, buildings, doors, objects, interaction commands, acknowledgements and conversation memory. Pinned Godot headless import/parse is in CI. | Actual Godot graphical session and Godot-to-Host runtime acceptance have not been executed by the current environment. |

### Important implementation boundaries

- The Engine Host is the sole owner of canonical NPC state, Engine-side relationship values, world catalog, Engine ledger, property/business records, traffic state and durable commands.
- The cognition bridge may propose dialogue and a bounded affinity delta; the Host validates and applies it. It does not own canonical relationship state.
- GTA V and Godot own native coordinates, collision, rendering, input and execution. They report physical outcomes; they do not duplicate simulation rules.
- Legacy GTA modules remain active for compatibility. Their emitted income/expense events are durably forwarded into the Engine journal and ledger. Some legacy shop balances and property actions still need full bidirectional reconciliation before the Engine can be declared the sole owner of every in-game economy field.
- No city-wide resident catalog, complete interior asset library, arbitrary ambient-Ped materialization or live GTA acceptance is claimed complete.

### Current release gate

M0–M5 source implementation is integrated on main. The integrated Windows CI run must pass all Host tests, Bridge context tests, Godot parser, pinned SHVDN C# compile targets, packaging and updater rollback before a final candidate can be considered. Even after CI passes, the target GTA V build and a Godot graphical session remain separate runtime acceptance gates. Do not hand out intermediate builds for user testing.

### Clock correctness hardening

The GTA V adapter is the authoritative simulation clock when connected. The selected clock mode and last accepted GTA world minute are now durable in SQLite. GTA sessions no longer use wall-clock offline catch-up, and a Host restart reconciles only positive in-game minute deltas. Godot and adapters without a world-clock feed continue to use the Host clock. A Windows CI regression test covers restart and clock reconciliation.
