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
