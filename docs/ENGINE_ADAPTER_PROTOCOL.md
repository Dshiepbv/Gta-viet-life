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

The Host now exposes authenticated NPC simulation endpoints:
- `POST /engine/v1/simulation/npcs` — register a game-neutral NPC profile and validated schedule.
- `POST /engine/v1/simulation/advance` — advance bounded simulation minutes; request IDs are deduplicated.
- `POST /engine/v1/simulation/interact` — record a supported player conversation, relationship change and memory; request IDs are deduplicated.
- `GET /engine/v1/simulation/snapshot` — inspect canonical NPC state and needs.

**Implementation boundary:** the Host now owns durable NPC simulation state and runs its simulation clock independently, but it is not yet the complete authoritative city/world simulation. The existing GTA World module still executes the physical resident schedule. Semantic actor commands, GTA location coordinate mappings, NPC materialization/reconciliation and physical execution results are still unimplemented.

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
