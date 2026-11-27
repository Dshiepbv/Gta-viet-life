# GTA Viet Life — Base-Game-Independent Life Simulation Engine

**Decision status:** Architecture baseline / first implementation boundary  
**First adapter:** GTA V Enhanced  
**Long-term goal:** Reuse the same simulation in other supported base games.

## 1. Product definition

GTA Viet Life is a persistent life-simulation engine with game adapters. It is not a collection of menus or a GTA-only script pack.

The engine owns the rules and persistent truth of the simulated society. A base game owns rendering, physics, animation, navigation, audio, input and the physical execution of actions. An adapter translates between them.

A feature is not complete because a menu or data record exists. It is complete when a player can perform a meaningful in-world action, the world responds visibly, the result persists, and other relevant actors/systems react consistently.

## 2. Hard architectural boundary

`engine/` must not import or reference:
- GTA, ScriptHook, SHVDN, RAGE, FiveM or GTA entity handles;
- Unity, Unreal or another game engine SDK;
- game-specific coordinates, model hashes, animation dictionaries or native function names.

Only an adapter may contain those dependencies.

The engine communicates with adapters through versioned, game-neutral contracts. Contracts describe semantic actions such as `move_actor`, `play_activity`, `use_interaction_point`, `spawn_actor`, `despawn_actor`, `enter_space` and `present_world_prompt`. The adapter maps them to the capabilities of its base game and reports the actual outcome.

## 3. Runtime topology

```text
                 GTA Viet Life Simulation Engine
       ┌────────────────────────────────────────────────┐
       │ World clock / scheduler / event processor      │
       │ Population / identity / memory / relationships │
       │ Needs / activities / jobs / housing            │
       │ Property / business / economy / inventory      │
       │ Traffic / mobility / incidents                 │
       │ Interaction intent / consequence / persistence │
       └───────────────────────┬────────────────────────┘
                               │ versioned contracts
                  ┌────────────┴────────────┐
                  │                         │
          GTA V Enhanced Adapter      Other Game Adapter
          SHVDN / game natives        Unity / Unreal / ...
                  │                         │
          GTA world entities          Native world entities
          GTA interiors               Native interiors
          GTA animation               Native animation
          GTA physics                 Native physics
```

The simulation must continue when actors are outside the loaded game area. Nearby actors and events are materialized into native game entities; distant society is advanced as data. Returning to an area must reveal the consequences of off-screen simulation.

## 4. Five layers

1. **Domain:** identity, households, relationships, jobs, needs, property, business, money, traffic and social rules. No game SDK.
2. **Simulation:** deterministic clock, schedules, event processing, causal rules, background/off-screen advancement.
3. **Interaction:** turns player intent and NPC intent into semantic actions, validates preconditions and records outcomes. AI may propose dialogue or plans, but cannot invent world facts or bypass rules.
4. **Persistence and integration:** versioned records, migrations, event journal, snapshots, idempotency and external AI provider interfaces.
5. **Game adapter:** observes the native world, maps identities/locations, executes actions, streams assets and reports success/failure.

## 5. In-world interaction standard

Menus are not the primary interaction model. Use physical affordances:
- approach a person, look at them, speak, listen, gesture and move away;
- approach a door, operate it, cross the threshold and navigate the interior;
- handle objects, inspect items, pick up, carry, use, exchange and store them;
- visit workplaces, shops and service locations; interact with people and physical objects;
- drive, park, refuel, repair and experience traffic incidents in the world.

A small HUD prompt is acceptable as an accessibility/interaction hint. It must not replace the action with a management screen. A menu may exist for out-of-world configuration or diagnostics, never as proof that gameplay has been implemented.

## 6. NPC cognition and behavior

Every persistent NPC needs a structured, authoritative state:
- identity and stable actor ID;
- traits, skills, occupation, household and home;
- current activity, schedule, needs and goals;
- relationships and shared history;
- episodic memories with source, time, confidence and relevance;
- knowledge boundaries: what this NPC knows, witnessed or was told;
- current location and physical embodiment state.

Dialogue generation receives a bounded context assembled from that state. It must not receive arbitrary unrelated facts and must not create authoritative changes by itself. The engine validates proposed actions against world state, rules, resources and adapter capabilities.

Fallback behavior must be deterministic and context-aware when an AI provider is unavailable. Random filler dialogue is not an acceptable fallback.

## 7. World simulation and scale

Use a multi-resolution simulation:
- **Background:** all registered residents, households, properties, businesses and trips advance in coarse time steps.
- **Regional:** relevant districts and active causal chains advance at finer resolution.
- **Embodied:** actors near the player or involved in an important event are instantiated in the base game and use its navigation, animation and physics.

Do not simulate thousands of actors as live game entities. Do not let optimization change authoritative outcomes. When a background actor is materialized, the adapter must reconcile the native entity with the engine-owned state.

## 8. Adapter contract and capability negotiation

Every adapter declares:
- adapter ID and semantic version;
- base-game build and mod/runtime version;
- supported capabilities (navigation, animation, interaction, interiors, vehicles, crowds, persistence hooks);
- entity and location identity mapping;
- streaming and entity budgets;
- unsupported operations and graceful degradation policy.

The engine must not assume every base game supports every capability. It may choose a different physical representation, but it must not silently pretend an unsupported action succeeded.

## 9. Persistence and compatibility

- The engine owns canonical simulation IDs and state.
- A game adapter owns only native handles and transient mappings.
- Save schemas are versioned and migrated explicitly.
- Existing GTA Viet Life saves remain readable during migration; do not delete or repurpose legacy keys.
- Existing `GVL.*` AppDomain contracts remain as a compatibility façade until each old module has a tested replacement.
- SQLite remains in the Python-side persistence/service layer initially. Do not make the game script the owner of the canonical database.
- Events require stable IDs, timestamps, causation/correlation IDs and idempotent consumers.

## 10. First implementation milestones

### M0 — Engine boundary
Create game-neutral contracts, a simulation host, deterministic clock and a fake adapter. Prove the engine runs without GTA assemblies.

### M1 — GTA adapter
Wrap the existing SHVDN integration behind the contract. Preserve current save data and legacy module behavior. The adapter reports actual capabilities and execution outcomes.

### M2 — First complete vertical slice: one NPC
One persistent NPC has a home, job, schedule, needs, memory and a goal. The player can meet them in the world, speak in context, observe them perform an activity, affect their state and see the consequence persist after leaving and returning.

### M3 — Physical places
Doors, entrances, interiors, object interactions, workplaces and shops become native in-world interactions. Universal building coverage is a content pipeline and measurable validation effort, not a menu feature.

### M4 — Society
Households, labor, property, businesses, economy and traffic become causally connected background systems with embodied local activity.

### M5 — Second adapter
Port a narrow vertical slice to a second base game without copying domain or simulation code. This is the proof that the engine is genuinely portable.

## 11. Acceptance gates

A module passes only when:
1. its rules run without loading any base-game SDK;
2. state survives save/reload and schema migration;
3. in-world actions are executed by the adapter and their outcomes are confirmed;
4. NPC knowledge and dialogue match their identity, memories and current context;
5. off-screen simulation produces consistent consequences;
6. failure, missing capabilities and AI/network outages have defined behavior;
7. tests cover domain rules independently of the game;
8. an in-game acceptance test verifies visible behavior.

## 12. Non-goals and risks

- This does not promise that every base game can expose the same physical capabilities.
- A portable engine cannot manufacture collision meshes, navigation data, animation rigs or interior assets for another game.
- AI does not replace deterministic simulation, authored activities or game-native physical execution.
- The first GTA adapter is a compatibility layer, not the place where new domain rules should accumulate.
- Portability is proven only when a second adapter runs the same domain test suite and vertical slice.

## 13. Migration rule

Do not rewrite all current GTA scripts at once. Freeze the old `GVL.*` contract, add an anti-corruption adapter around it, and migrate one vertical slice at a time. Keep rollback possible until the new path passes the same in-game acceptance tests.
