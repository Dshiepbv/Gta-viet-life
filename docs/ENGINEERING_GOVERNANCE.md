# Engineering Governance — Research, Build, Release and Final Acceptance

**Applies to:** GTA Viet Life Engine and every game adapter  
**User-facing policy:** No alpha, preview, prototype or intermediate build is submitted to the user for gameplay testing.  
**Final acceptance:** One complete release candidate, followed by at most three user-feedback correction cycles.

## 1. Non-negotiable delivery rule

The user is not the development test team. The development process must not ask the user to install or test alpha builds, incomplete modules, temporary menus or experimental protocol changes.

The user receives a build only after the development team has completed the applicable research, design review, implementation, internal verification, integration and release-readiness gates.

This does not mean "skip testing". It means internal engineering verification is the developer's responsibility. The user's first test is the final integrated candidate.

## 2. Mandatory design review before implementation

For every major subsystem, create a short Architecture Decision Record (ADR) before coding. It must include:

1. Problem statement and user-visible outcome.
2. Constraints from the current repository, GTA V Enhanced, runtime, save compatibility and target hardware.
3. At least two credible architecture alternatives.
4. Comparison against established engine patterns and primary technical documentation.
5. Failure modes, performance risks, migration cost and portability impact.
6. A counter-argument against the preferred option (pre-mortem).
7. Selected option, why it wins, and conditions that would invalidate it.
8. Explicit acceptance criteria stated as observable in-game behavior.

Do not present a proposal as validated merely because it sounds architecturally elegant. Distinguish documented facts, repository facts, assumptions and unknowns.

## 3. Engine architecture decision

The project must keep the simulation domain independent of any one game engine. The reviewed reference patterns include:

- Unreal MassEntity: data-only fragments, archetypes/chunks and processors; MassGameplay adds representation and LOD concepts.
- Unity Entities/DOTS: data-oriented entities, components and systems, with optional jobs/Burst for high-throughput work.
- Bevy ECS: entities/components/systems, explicit schedules and dependency ordering; the ECS crate can be used independently of Bevy.

These are reference patterns, not dependencies to embed in the portable core. The project must not make Unreal, Unity or Bevy a prerequisite for running GTA Viet Life.

### Required separation

- **Simulation kernel:** canonical world state, time, scheduling, deterministic rules, causal events and persistence contracts.
- **Domain systems:** population, identity, memory, relationships, needs, activities, property, business, economy and mobility.
- **Cognition service:** LLM/provider integration, context assembly, bounded planning and dialogue. It proposes; the simulation validates.
- **Game adapter:** observes native entities and executes semantic physical commands. It owns native handles and asset mappings only.
- **Content pipeline:** maps, buildings, interiors, animation/action definitions, navigation affordances and per-game assets.

### Runtime technology decision is not frozen by the first scaffold

The initial Python kernel files are a minimal boundary demonstration, not evidence that Python is the final high-volume simulation runtime. Before expanding domain systems, compare:
- a .NET/C# simulation host, aligned with the existing C# GTA integration and repository;
- a Python simulation host, aligned with the current Python bridge and AI/data tooling;
- a split architecture, only if ownership, IPC failure semantics and persistence are explicit.

The ADR must select one canonical owner for simulation state. Never let two processes independently mutate the same world facts. A likely separation to evaluate is a strongly typed .NET simulation host, Python cognition/integration services, and versioned local IPC; this is a candidate, not a pre-approved conclusion.

The selection must be based on measured workload estimates, deployment complexity, runtime compatibility, testability, adapter portability and migration cost—not language preference.

## 4. Internal verification is mandatory; user alpha testing is forbidden

Internal gates, in order:

1. **Static review:** type/syntax checks, dependency-boundary checks, schema validation, security review.
2. **Automated tests:** domain rules, state transitions, idempotency, deterministic replay, save migration and failure cases.
3. **Contract tests:** fake adapter verifies handshake, observation, command, result and disconnect/reconnect behavior.
4. **Integration tests:** engine host, bridge, persistence and GTA adapter run together in a controlled environment.
5. **Runtime acceptance:** verify the target GTA V Enhanced build, mod loader/runtime compatibility, actual physical actions, NPC schedules, interior transitions and save/reload.
6. **Release audit:** no known blocker, documented limitations, reproducible build and rollback path.

A test harness or fake adapter is not a user-facing alpha. It is internal verification infrastructure.

If the team lacks the target game/runtime or a required asset, mark that gate BLOCKED. Never claim a physical feature works based only on unit tests or a successful compile.

## 5. No-menu gameplay acceptance standard

A system is not complete because it has a database record, dialogue panel or management menu. It must satisfy all applicable conditions:

- Player initiates the activity through a physical in-world affordance.
- The game adapter executes an actual native action/animation/transition.
- Other actors and systems respond according to authoritative world state.
- The result changes persistent state and survives leaving/reloading.
- The outcome can be observed in the world without opening a management menu.
- Failures and unsupported game capabilities are visible and handled honestly.

HUD prompts and accessibility options are allowed. They may guide interaction but must not replace gameplay.

## 6. Final delivery package

The first user-facing build must be a complete integrated release candidate, not a feature demo labelled as finished. It must include:

- versioned installer/package and exact compatibility matrix;
- complete source commit and build instructions;
- migration and backup/rollback procedure for existing saves;
- release notes and known limitations;
- reproducible internal verification report with pass/fail/blocked status;
- a concise in-game acceptance route and log collection instructions.

The user should not need to configure developer tools, edit JSON, run scripts or inspect databases to make core gameplay work.

## 7. Three-cycle correction protocol

After the user receives the final candidate:

- **Cycle 1 — Reproduction and diagnosis:** user supplies one consolidated gameplay log and observed issues. Developer classifies each issue, reproduces internally, identifies root cause and prepares a complete corrective build.
- **Cycle 2 — Regression and correction:** user reports remaining issues from the corrected build. Developer fixes root causes and reruns the full relevant regression suite.
- **Cycle 3 — Closure:** final user verification. Only remaining release-blocking defects are addressed; deliver closure report and final build.

Each cycle must produce a versioned build, change log, issue-to-fix mapping and regression evidence. Do not reset the cycle count for cosmetic renaming or split one defect into multiple tickets. If the build is not internally reproducible, request the specific log or save needed; do not guess.

Three cycles are a target for efficient closure, not a promise that arbitrary defects can always be fixed in three attempts. If a critical blocker remains after cycle 3, report it honestly and continue engineering rather than declaring success.

## 8. User log design

The final build must have a one-action "Export diagnostic log" capability, or automatically produce a clearly located log bundle. The bundle should contain:
- game build, adapter version, engine version and protocol version;
- timestamped engine events and command/result IDs;
- NPC/interaction state transition summaries;
- adapter capability and failure reports;
- save/migration status and exception traces;
- performance counters and relevant entity counts.

Do not collect passwords, account tokens, unrelated personal files or unnecessary private chat content. Provide a clear way to inspect the log before sharing.

## 9. Release decision

A release is blocked if any of the following is true:
- engine/game protocol versions are incompatible;
- existing saves can be corrupted or silently reset;
- a core physical action is only represented by a menu;
- NPC dialogue can invent authoritative facts or ignore known context;
- a queued physical action is reported as successful without adapter confirmation;
- critical runtime or asset compatibility is unverified;
- no usable diagnostic log or rollback path exists.

## 10. Current branch status

The current `engine/` Python code is a small M0 scaffold and its test files are not proof of a completed runtime. Do not expand it into the full simulation until the runtime technology ADR is completed. The existing GTA modules and legacy save format remain protected during the architecture decision.

The current pull request remains Draft. It must not be represented as a finished or user-testable release.
