# GTA Viet Life — Engineering, Validation and Delivery Policy

**Applies to:** GTA Viet Life Engine, all game adapters, and all gameplay modules  
**Delivery model:** Build internally to an integrated release candidate; user tests only the final handoff.

## 1. Non-negotiable delivery rule

The user is not the alpha/beta test team. Do not ask the user to install, play, validate, or provide logs for intermediate alpha builds, prototypes, partial modules, or unintegrated versions.

The user receives one integrated final candidate after the implementation and internal verification gates below have been completed. The user then performs the acceptance playthrough and supplies logs. Fixes are handled in up to three post-handoff correction cycles, with each cycle producing a consolidated updated build and a clear change log.

This is a delivery policy, not a promise that software can be made defect-free without runtime evidence. If a blocker cannot be verified without the user's machine or game session, disclose it explicitly; do not disguise an unverified build as final.

## 2. Required pre-design challenge

Before presenting an architecture or implementation plan, perform a written internal design review:

1. State the actual user outcome and observable gameplay acceptance criteria.
2. Identify assumptions, engine constraints, compatibility risks, performance limits and failure modes.
3. Compare at least two relevant established approaches where applicable, such as:
   - ECS/data-oriented simulation;
   - event-driven/domain simulation;
   - actor/agent models and hierarchical AI;
   - authoritative simulation with thin presentation adapters;
   - native engine gameplay systems and mod/plugin architectures.
4. Compare the proposed design with relevant established engines/frameworks and their documented architectural patterns. Examples may include Unreal Engine, Unity, Godot, Creation Engine/Skyrim mod ecosystems, simulation architectures and mod/plugin SDKs. Do not claim these systems are identical; compare only the relevant design properties.
5. Challenge the proposal against OpenAI model knowledge and reasoning: expose contradictions, missing mechanisms, hidden costs and unsupported assumptions. This means using available model knowledge and verifiable public technical references where needed; never claim access to private OpenAI training data, hidden source code or internal engineering systems.
6. Select the simplest feasible architecture that satisfies the acceptance criteria. Explain what is reused, what is custom, what is deferred and why.
7. Define measurable acceptance gates before implementation begins.

Do not present an unchallenged first idea as the recommended solution.

## 3. Zero-to-integrated delivery

After the design is accepted or the direction is otherwise authorized, implement the complete vertical slice end-to-end:

- domain model and authoritative state;
- simulation rules and scheduling;
- persistence, migrations and recovery;
- game-neutral contracts;
- adapter implementation and capability negotiation;
- in-world interaction, physical execution and feedback;
- UI/HUD only where it supports, rather than replaces, gameplay;
- configuration, diagnostics, logging and failure handling;
- packaging, install/update instructions and rollback path;
- documentation and release notes.

Do not hand off isolated scaffolding as if it were a completed feature. Do not substitute menus, static records, dialogue stubs or mock actions for physical gameplay.

## 4. Internal verification is mandatory; user alpha testing is prohibited

The prohibition on user testing intermediate builds does not prohibit engineering verification. Before final handoff, run all verification that is possible in the available environment:

- static inspection, formatting and type/schema checks;
- unit tests for deterministic domain rules;
- integration tests across persistence, bridge and adapter contracts;
- build/package verification for every deliverable;
- compatibility and save migration checks;
- replay/idempotency, failure recovery and offline behavior checks;
- performance/load tests with declared budgets;
- automated or instrumented game-runtime checks where a suitable game environment exists;
- regression checks against existing modules and saves.

Record the exact commands, environment, versions, results and limitations. Never claim a test passed unless it was actually run and its result observed.

If the development environment lacks GTA V Enhanced, the required mod runtime, hardware or assets, do not invent a runtime pass. Complete all available offline verification and mark the remaining game-runtime gate as requiring the user's final acceptance session. The user should not be asked to test an alpha to compensate for missing engineering work.

## 5. Final handoff criteria

A build may be labelled **Final Candidate for User Acceptance** only when:

- all planned files and modules are integrated;
- the intended end-to-end gameplay path exists;
- no known critical/high-severity defect is left undisclosed;
- save compatibility and migration/rollback are addressed;
- internal verification results and limitations are documented;
- logs identify build, adapter, game version, session and relevant event/correlation IDs;
- install/update and log-export instructions are complete.

A build that fails these gates remains internal work and must not be sent to the user as a test version.

## 6. Three-cycle post-handoff correction protocol

After the user tests the final candidate:

1. **Cycle 1 — Diagnose and correct:** collect logs, reproduce where possible, identify root cause, fix the highest-impact defects, run regression checks and issue a consolidated build.
2. **Cycle 2 — Close regressions and edge cases:** analyze new logs, fix remaining defects and regressions, run the full affected verification set and issue a consolidated build.
3. **Cycle 3 — Stabilize:** address remaining reproducible defects, verify compatibility and performance, freeze scope and issue the stabilized build with a final change log.

Each cycle must be based on evidence: logs, reproduction steps, build identifiers and observed behavior. Do not patch symptoms blindly.

The three cycles are the planned correction budget, not a license to conceal unresolved defects. If a fundamental engine limitation or external dependency prevents completion, report it plainly with the smallest technically sound next action.

## 7. Logging and observability requirements

Every final candidate must provide actionable logs, not just generic error text. Where relevant, include:

- timestamp and simulation tick;
- build/version, adapter ID/version and base-game build;
- session ID and stable actor/location IDs;
- command/event ID plus causation/correlation IDs;
- requested semantic action and confirmed adapter outcome;
- subsystem and severity;
- exception type, stack trace and error code;
- persistence transaction/save revision;
- AI provider/model identifier where applicable, latency and fallback reason, without logging secrets or unnecessary personal data.

Logs must support filtering and export. Sensitive credentials, tokens and private user data must be redacted.

## 8. Compatibility and release discipline

- Work on a dedicated feature branch and keep changes reviewable.
- Preserve existing GTA V Enhanced support and user saves unless a documented migration explicitly changes them.
- Do not silently change legacy keys, event semantics or module contracts.
- Keep the final handoff consolidated; do not ask the user to cherry-pick commits or assemble partial packages.
- Never describe a draft PR, unbuilt source tree or unverified artifact as a final release.
- Maintain a known-issues list and a truthful verification report.

## 9. Acceptance principle for gameplay

A feature is complete only when the player can perform it through in-world actions, the base game physically executes the interaction, the simulation validates and persists the consequence, and relevant actors/systems respond consistently.

Menus and HUD prompts may support usability, but they cannot substitute for physical interaction or functioning gameplay.
