# GTA Viet Life Engine — Game Adapter Protocol

**Protocol:** `gvl-engine/1`  
**Purpose:** Connect a base game to the independent Python simulation host.

## 1. Separation

The Python engine process is authoritative for simulation state. A native game adapter is responsible for observing and executing physical game behavior. The adapter may be C#, C++, C++/CLI or another language; it does not need to load Python or implement Python classes.

The in-process `engine.adapters.GameAdapter` abstraction is for the engine host and test/fake adapters. A real GTA V SHVDN adapter communicates through this wire protocol.

## 2. Transport

Initial GTA implementation: a dedicated Engine Host HTTP service on `127.0.0.1:5580`. The legacy AI/voice bridge remains a separate service on `127.0.0.1:5577` and is not the authority for this protocol. The transport is replaceable; message semantics are not tied to HTTP. A future adapter may use named pipes or another local IPC transport.

All requests use JSON UTF-8, include `protocol: "gvl-engine/1"`, and carry a unique `request_id`. The server rejects unknown major protocol versions. Commands are idempotent by `command_id`.

## 3. Required handshake

`POST /engine/v1/handshake`

Request:

```json
{
  "protocol": "gvl-engine/1",
  "request_id": "uuid",
  "adapter": {
    "id": "gta5-enhanced.shvdn",
    "version": "0.1.0",
    "base_game": "gta5-enhanced",
    "base_game_build": "reported-build-string"
  },
  "capabilities": [
    "actor.observe",
    "actor.spawn",
    "actor.despawn",
    "actor.navigate",
    "actor.animate",
    "interaction.world",
    "vehicle.control",
    "interior.transition"
  ],
  "limits": {
    "max_embodied_actors": 32,
    "max_active_interactions": 8
  }
}
```

The engine responds with the accepted protocol, required capabilities, negotiated limits and any unsupported capability list. Capability claims must reflect tested implementation, not planned features.

## 4. Bootstrap and health endpoints

The current Launcher/Host/Adapter bootstrap implements these endpoints:

- `GET /health` — unauthenticated process health only; loopback binding is mandatory.
- `GET /engine/v1/status` — authenticated runtime and active adapter-session status.
- `POST /engine/v1/handshake` — authenticated session registration.
- `POST /engine/v1/heartbeat` — authenticated liveness renewal.
- `POST /engine/v1/close` — authenticated session close.
- `POST /engine/v1/shutdown` — authenticated graceful Host shutdown.

Every POST body must contain a non-empty unique `request_id`. Authenticated requests use the `X-GVL-Token` header. The Launcher creates a random per-user token at `%LOCALAPPDATA%/GtaVietLife/host.token`; the game Adapter reads the same token. The Host rejects non-loopback bind addresses.

The current GTA Core reports only the capability `adapter.session`. It also imports the existing resident roster into the Engine Host through the authenticated simulation profile endpoint. This imports stable identity and schedule metadata only; it does not claim actor observation, spawning, navigation, interiors or vehicle control. Those capabilities remain unavailable until the corresponding in-world command/result path is implemented and verified.

The GTA Core schedules its heartbeat using a monotonic process clock rather than `Game.GameTime`, so the heartbeat cadence can continue while the game clock is paused. It includes `world_minute` (absolute in-game day/minute) in the handshake and heartbeat. Once a clock-aware adapter session is live, the Host pauses its wall-clock simulation worker and advances NPC life only by positive deltas observed from the GTA world clock. Repeated heartbeat values do not advance time; a backward clock correction resets the observation baseline without rewinding durable NPC history. Each heartbeat refreshes the offline checkpoint so a paused game is not later mistaken for offline elapsed time. Adapters that omit `world_minute` retain the legacy wall-clock simulation behavior.

The Host now exposes authenticated NPC simulation endpoints:
- `POST /engine/v1/simulation/npcs` — register a game-neutral NPC profile and validated schedule.
- `POST /engine/v1/simulation/advance` — advance bounded simulation minutes; request IDs are deduplicated.
- `POST /engine/v1/simulation/interact` — record a supported player conversation, relationship change and memory; request IDs are deduplicated.
- `GET /engine/v1/simulation/snapshot` — inspect canonical NPC state and needs.
- `GET /engine/v1/adapter/commands` — retrieve pending semantic commands (optional `X-GVL-Limit`, 1–200; default 50).
- `POST /engine/v1/adapter/commands/ack` — report a terminal command result using `command_id` and `result`. `success=true` marks `completed`; `success=false` requires `status` to be one of `failed`, `unsupported`, `rejected` or `cancelled`.

Activity transitions currently create durable `actor.set_activity` commands in a SQLite outbox. For each NPC and command type, a newer pending activity state supersedes the older pending state so reconnecting adapters do not replay a long backlog of obsolete destinations. Polling is at-least-once: commands remain pending until a terminal result is recorded, so adapters must deduplicate by `command_id`. Completed, failed, unsupported, rejected and cancelled outcomes are persisted separately and removed from the pending queue. Temporary conditions such as an off-screen resident, an NPC in conversation or an NPC following the player are deferred locally and are not reported as terminal failures.

**Implementation boundary:** the Host now owns durable NPC simulation state and produces a durable semantic command stream, but the GTA game adapter does not yet poll or execute that stream. The existing GTA World module still executes the physical resident schedule. Real GTA location mapping, physical actor execution, reconciliation and result reporting remain outstanding.

## 5. Observation submission

`POST /engine/v1/observe`

A request contains:
- adapter and game session IDs;
- monotonic observation timestamp;
- player actor ID and current location ID;
- world time, if available;
- actors and relevant physical observations in a bounded radius;
- interaction affordances and active physical events.

Native entity handles, model hashes and native pointers must never become canonical engine IDs. The adapter maps them to stable session-scoped IDs and retains the native mapping privately.

## 6. Command polling and results

`GET /engine/v1/commands?adapter_id=...`

The engine returns pending semantic commands. Each command has:
- stable `command_id`;
- semantic `kind`;
- optional target canonical ID;
- typed parameters;
- correlation ID and expiry policy.

Examples of semantic kinds:
- `actor.navigate`
- `actor.play_activity`
- `actor.face_target`
- `actor.speak`
- `interaction.open`
- `interaction.use_object`
- `space.enter`
- `space.exit`
- `vehicle.drive_to`
- `world.present_prompt`

The adapter must return a result for every accepted command:

`POST /engine/v1/command-results`

A result distinguishes `completed`, `rejected`, `failed`, `cancelled` and `unsupported`. A command is not successful merely because it was queued. The adapter reports the observed physical outcome.

## 7. Identity and location mapping

- Engine actor IDs are stable across sessions.
- Native entity handles are transient and must be remapped after game reload.
- Location IDs identify semantic places (building, room, road segment, workplace), not raw coordinates alone.
- Adapter-owned mapping tables are disposable and rebuildable.
- Coordinates must include a declared coordinate-space ID and unit. No cross-game coordinate is comparable without an explicit mapping.

## 8. Failure semantics

- Duplicate request or command IDs must not duplicate state changes.
- Expired commands are rejected with a reason.
- Missing capabilities return `unsupported`; never silently fake success.
- If the game or adapter disconnects, the simulation may continue in background.
- On reconnect, the adapter sends a fresh observation and mapping revision; the engine reconciles canonical state before issuing physical commands.
- The engine never assumes a spawn, animation, door transition or movement completed until the adapter reports the result.

## 9. Security

- Bind local transport to loopback only.
- Do not expose the engine control endpoint to LAN or public internet by default.
- Validate payload size, IDs, enums, numeric ranges and command allowlists.
- Do not accept arbitrary script, native function names or executable code from AI output.
- Keep AI-generated plans separate from validated engine commands.

## 10. Compatibility policy

Minor protocol additions are backward-compatible and capability-negotiated. Breaking semantic changes require a new major protocol version. Each adapter must publish the exact base-game build and adapter version used for acceptance tests.


## 7. Current GTA V adapter capability declaration

After the command executor increment, the GTA V Core handshake declares:

- `adapter.session` — authenticated adapter session and heartbeat;
- `adapter.command_poll` — authenticated polling of the durable Engine command outbox;
- `adapter.actor_activity` — execution of the first `actor.set_activity` command for registered resident Peds.

The World script maps only the existing resident's verified home, work and lunch coordinates. It defers commands while the resident Ped is absent, frozen in conversation, or following the player. Acknowledgement currently means the GTA task was issued, not that the destination was reached. The capability declaration must not be interpreted as support for arbitrary ambient NPCs, interiors, vehicle tasks, or activity-specific animations.

## Second adapter implementation — Godot 4 (M5)

The `adapters/godot/` reference project consumes the same `gvl-engine/1` Host protocol as the GTA V adapter. It demonstrates adapter identity/capability declaration, NPC registration, durable semantic command polling, physical movement in a native scene, completion acknowledgement after arrival, and a player-to-NPC interaction routed back to the canonical Engine domain. Godot owns only its native scene coordinates and presentation. It does not own canonical NPC state or duplicate domain rules.

The current Godot scene maps `home.godot`, `work.godot` and `park.godot` to local 2D positions. This mapping is adapter-local and is not shared with GTA. The project is a narrow portability reference, not a full second game. Runtime validation with Godot 4 remains an explicit acceptance gate; source presence and Host-side integration tests are not a substitute for launching the Godot project.


## World interaction and society protocol (M3–M4)

The same authenticated Host now exposes these game-neutral endpoints:

- POST /engine/v1/world/buildings — register a building with adapter-local semantic entrance/interior mappings and allowed actions.
- POST /engine/v1/world/objects — register a persistent object with per-adapter interaction anchors.
- POST /engine/v1/world/interact — validate an actor/action/target and enqueue a durable space.transition or interaction.use_object command. A repeated request ID returns the original command result.
- GET /engine/v1/world/commands — poll pending world commands. Each payload carries the intended adapter ID; adapters must ignore commands mapped for another game.
- POST /engine/v1/world/commands/ack — report completed, failed, unsupported, rejected or cancelled execution. A successful object-use acknowledgement updates the canonical object's use state.
- GET /engine/v1/world/snapshot — inspect the canonical building, object, property, business and traffic catalog.
- POST /engine/v1/world/businesses/sync — reconcile a native adapter's authoritative business cash/inventory snapshot. Request IDs are idempotent; cash differences post balanced ledger entries against a system reconciliation account; inventory/open state update atomically. The adapter must have a mapping on the property's building.

The GTA adapter maps its known residential entrances/interiors, furniture anchors, workplace and dynamic shop coordinates. It only reports a transition completed after the player is observed in the requested space. The Godot adapter maps its own scene coordinates and executes the same semantic command types. Semantic IDs are not coordinates; a missing adapter mapping fails closed.

M4 endpoints provide double-entry transactions, accounts, property purchase/lease, business inventory/sales, traffic segment registration and incident reporting. The GTA Core sends legacy events through an independently persisted outbox; source + sequence IDs make retries idempotent. Cash and bank flows remain separate accounts. Opening wallet baselines are created once and never overwrite an active ledger balance.

## Cognition bridge boundary (M2)

POST /engine/v1/simulation/context returns bounded identity, current needs/activity, relationship, provenance-aware known facts and recent episodic memories. POST /engine/v1/simulation/memory records actual player speech and NPC reply. The Host accepts only an affinity delta in [-2, 2], scales and clamps it, and applies it to canonical relationship state. The bridge cannot write needs, money, property, traffic or arbitrary NPC state.

### Authoritative clock and restart behavior

- gta5.shvdn sends the GTA world minute in handshake and heartbeat. The Host persists both the clock mode and last accepted GTA minute in SQLite.
- While GTA is the configured clock source, the Host background worker does not advance simulation from wall time, including while GTA is closed. This prevents the game world from drifting forward merely because the launcher or Host remained open.
- On reconnect, the Host advances only by the positive difference between the newly observed GTA minute and the last persisted GTA minute. A backward in-game time adjustment never rewinds NPC history; the new observation becomes the baseline for subsequent forward progress.
- Adapters without a game-clock feed, including the Godot reference adapter, continue to use the Host clock.


## Shared human-life actor model

The canonical simulation treats the player character and NPCs as human-life actors in the same domain model. Register either through `POST /engine/v1/simulation/actors` (or the backward-compatible NPC route) with `actor_kind: "player" | "npc"`. Both actor kinds share needs, health, hygiene, bladder, fitness, mood, aging and life-stage progression. The engine never steers the player character with autonomous NPC navigation commands; the game adapter remains responsible for player input and physical presentation. NPC activity commands remain adapter-executed.

The current life-needs slice includes hunger, energy, social connection, stress, hygiene and bladder pressure, plus health, fitness, mood and age progression. Supported semantic activities include eating, sleeping, work, commute, socializing, leisure, showering, bathroom use, exercise and study. This is a foundation, not a claim that all human systems (medical treatment, family formation, education, employment contracts, full body animation or every life stage) are complete.


### Physical interactions update the shared human-life model

A completed `interaction.use_object` command is not merely a visual acknowledgement. When the registered object's canonical kind has a human-life mapping, the Host applies the consequence to the actor after the adapter reports `success=true`. Current mappings include food/meal/fridge, bed, toilet, shower, gym equipment, computer/book, hobby and social seating. The result is persisted in the same canonical actor snapshot for both `actor_kind=player` and `actor_kind=npc`.

The engine deliberately does not apply a need change when a command is only queued, rejected, unsupported or failed. This preserves the rule that physical actions must be observed by the game adapter before their life consequences become canonical. Unknown object kinds still record the physical object use but return no life-domain effect until an explicit mapping is implemented.


## Employment and earned income

The shared life clock now tracks completed canonical work hours. An active employment contract links an actor to a business, the employee's deposit account and an hourly wage through `POST /engine/v1/world/employment/contracts`. Every 60 simulated minutes in the actor's `work` activity emits a durable `work_hour_completed` event. Engine Host converts that event into a balanced business-to-employee ledger transaction. Payroll transaction IDs are deterministic per contract and simulation minute, so request replay cannot pay twice. This first payroll slice covers hourly wages; hiring/firing workflows, attendance exceptions, taxes, benefits, promotions and richer labor-market simulation remain future scope.


## Persistent social and family graph

The Society domain now stores explicit actor-to-actor ties with relation types (parent, child, sibling, spouse, romantic interest, friend, coworker, neighbor, acquaintance and rival), relationship strength and status (active, estranged, separated or ended). The relationship endpoint validates that both participants exist in the shared actor registry, persists the social edge and synchronizes symmetric affinity into both life records. This provides a canonical basis for family and social consequences; family formation, custody, household finances, births and relationship-specific autonomous behavior are not yet complete.


## Durable life-event delivery

The SQLite life-event journal is now consumed through a separate processed-event ledger. The Host recovers unprocessed events on startup and after every simulation advance. Each event is marked processed in the same database transaction as its linked economic consequence, and payroll ledger transaction IDs are independently idempotent. This closes the failure window where simulation state could be saved but an associated society/economy consequence lost during a Host interruption.


## GTA V player-character registration

The GTA Core periodically registers `player.main` through the shared actor endpoint as `actor_kind: "player"`, then synchronizes the legacy cash/bank opening balances. Name, occupation, home semantic ID and age can be configured in the `[Player]` settings section; defaults are neutral placeholders. The player actor participates in the same needs, aging, relationship and economic domains as residents, while autonomous NPC navigation commands are never issued to the player. Player movement and direct controls remain owned by GTA V.


### Player body-state observation

The GTA World adapter posts player body meters every two seconds through `POST /engine/v1/simulation/actors/observe`. The native GTA physiology remains authoritative for hunger/fullness, hydration, energy, hygiene and health; the adapter converts fullness and hydration bars into canonical unmet-need values before submission. The Engine does not independently decay those same player meters, avoiding double simulation. NPC physiology remains simulated by the portable Life Engine. The player's bladder pressure, social relationships, stress, mood, fitness and aging remain part of the shared simulation state.


### Player-controlled work sessions

GTA V work actions can register a bounded player activity through `POST /engine/v1/simulation/actors/activity`. The activity is time-boxed in simulation minutes, so work time and hourly work events accrue only during the actual game-clock interval. The GTA World adapter reports the player's mechanic shift as a 240-minute work session before the native time-skip action; the native game remains responsible for its existing cash reward and physical presentation.
