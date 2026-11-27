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

The Engine Host advances one simulated minute every two seconds while running and performs bounded offline catch-up on restart. The current GTA Adapter is not yet connected to NPC registration, location mapping or physical command execution. This is an engine implementation increment, not a user acceptance release.

Run all engine tests:

```bash
python -m unittest discover -s engine/tests -v
```
