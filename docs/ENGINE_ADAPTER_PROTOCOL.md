# GTA Viet Life Engine — Game Adapter Protocol

**Protocol:** `gvl-engine/1`  
**Purpose:** Connect a base game to the independent Python simulation host.

## 1. Separation

The Python engine process is authoritative for simulation state. A native game adapter is responsible for observing and executing physical game behavior. The adapter may be C#, C++, C++/CLI or another language; it does not need to load Python or implement Python classes.

The in-process `engine.adapters.GameAdapter` abstraction is for the engine host and test/fake adapters. A real GTA V SHVDN adapter communicates through this wire protocol.

## 2. Transport

Initial GTA implementation: localhost HTTP on the existing bridge service, bound only to `127.0.0.1`. The transport is replaceable; message semantics are not tied to HTTP. A future adapter may use named pipes or another local IPC transport.

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

## 4. Observation submission

`POST /engine/v1/observe`

A request contains:
- adapter and game session IDs;
- monotonic observation timestamp;
- player actor ID and current location ID;
- world time, if available;
- actors and relevant physical observations in a bounded radius;
- interaction affordances and active physical events.

Native entity handles, model hashes and native pointers must never become canonical engine IDs. The adapter maps them to stable session-scoped IDs and retains the native mapping privately.

## 5. Command polling and results

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

## 6. Identity and location mapping

- Engine actor IDs are stable across sessions.
- Native entity handles are transient and must be remapped after game reload.
- Location IDs identify semantic places (building, room, road segment, workplace), not raw coordinates alone.
- Adapter-owned mapping tables are disposable and rebuildable.
- Coordinates must include a declared coordinate-space ID and unit. No cross-game coordinate is comparable without an explicit mapping.

## 7. Failure semantics

- Duplicate request or command IDs must not duplicate state changes.
- Expired commands are rejected with a reason.
- Missing capabilities return `unsupported`; never silently fake success.
- If the game or adapter disconnects, the simulation may continue in background.
- On reconnect, the adapter sends a fresh observation and mapping revision; the engine reconciles canonical state before issuing physical commands.
- The engine never assumes a spawn, animation, door transition or movement completed until the adapter reports the result.

## 8. Security

- Bind local transport to loopback only.
- Do not expose the engine control endpoint to LAN or public internet by default.
- Validate payload size, IDs, enums, numeric ranges and command allowlists.
- Do not accept arbitrary script, native function names or executable code from AI output.
- Keep AI-generated plans separate from validated engine commands.

## 9. Compatibility policy

Minor protocol additions are backward-compatible and capability-negotiated. Breaking semantic changes require a new major protocol version. Each adapter must publish the exact base-game build and adapter version used for acceptance tests.
