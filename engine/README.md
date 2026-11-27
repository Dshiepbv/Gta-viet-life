# GTA Viet Life Engine

This package is the game-neutral simulation kernel. It must run without GTA, SHVDN, Unity or Unreal dependencies.

## Boundary

- `contracts.py`: semantic, versioned messages shared with game adapters.
- `world.py`: deterministic simulation clock and authoritative world state.
- `adapters/`: base-game integration contracts; native SDK references belong in separate adapter packages.
- Future domain packages: population, NPC cognition, activities, property, economy, business, traffic and persistence.

The current code is only M0 foundation. It is not yet a complete life simulator and does not claim that GTA gameplay features are implemented.

## Run the kernel tests

From the repository root:

```bash
python -m unittest discover -s engine/tests -v
```

No external package is required.


## Local host bootstrap

The Windows Launcher starts the standard-library service in `host.py` on loopback port 5580. It authenticates the GTA V Core adapter using a random per-user token at `%LOCALAPPDATA%\\GtaVietLife\\host.token`.

Run manually for development:

```powershell
python engine/host.py --host 127.0.0.1 --port 5580 --token-file .\\host.token --data-dir .\\data
```

The host currently provides health, adapter handshake, heartbeat, session status and shutdown. It is a connectivity/runtime bootstrap, not yet the full NPC, economy or world simulation engine.


## First NPC simulation kernel

`life_simulation.py` provides a deterministic, game-neutral NPC model and SQLite persistence. It simulates schedules, hunger, energy, social need, stress and career/health/social life-goal progress; records activity transitions; persists relationship affinity and player-conversation memories; and exposes snapshots for adapter integration.

The Engine Host advances one simulated minute every two seconds while running and performs bounded offline catch-up on restart. The current GTA Core imports resident identity/schedule profiles, while the Engine Host now creates durable `actor.set_activity` commands on activity/location transitions. Commands are available through authenticated polling and success acknowledgement endpoints, backed by SQLite schema v3. The GTA World script does not yet consume or execute this command stream; physical resident movement remains legacy-controlled. This is an engine-to-adapter protocol increment, not an end-to-end in-world control release or user acceptance build.

Run all engine tests:

```bash
python -m unittest discover -s engine/tests -v
```

## Second-game adapter — M5 portability proof

A Godot 4 reference adapter now lives in `adapters/godot/`. It uses the same Engine Host API and canonical NPC simulation as GTA V; it owns only native scene rendering, player input, movement and semantic-location mapping. The Host integration suite includes a Godot adapter identity path covering registration, command execution acknowledgement and persisted conversation memory. Run the unchanged engine tests with `python -m unittest discover -s engine/tests -v`. The actual Godot 4 runtime still requires a separate launch/acceptance run.
