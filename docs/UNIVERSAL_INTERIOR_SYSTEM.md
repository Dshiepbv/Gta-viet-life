# GTA Viet Life — Universal Interior System (UIS)

**Status:** Architecture proposal  
**Target:** GTA V Enhanced single-player, SHVDN-based runtime  
**Branch:** codex/gta-viet-life-architecture-hardening

## 1. Product requirement

The selected direction is **Option A: every eligible city building must be enterable**.

Every residential building and every relevant occupied or commercial building in the supported Los Santos/San Andreas world catalog must resolve to a usable interior. A building must not remain a decorative, inaccessible shell merely because the base game did not provide an interior.

“Enterable” is a content and runtime contract, not a promise that every building receives a unique hand-authored interior. The system may reuse authored interior templates, but each building must have a valid entrance, destination, collision, exit route, identity, and persistent property state.

The initial scope should inventory the full GTA V base-game world. DLC, map expansions, and third-party map mods are separate catalog packs and must not silently be assumed covered.

## 2. Repository baseline and constraints

The current repository is a collection of SHVDN C# scripts plus a Python bridge and Android dashboard. The existing game/GtaVietLifeWorld.cs has a small hard-coded Homes array (four entries) and a player home state (hasHome, home, homeIdx, homeName, GVL.Home). It does not currently provide:

- a world-wide building/entrance catalog;
- a general entrance interaction system;
- an interior resolver or interior instance manager;
- interior templates, MLO/YMAP assets, or a map-asset build pipeline;
- a test catalog proving city-wide building coverage.

The current SHVDN scripts are not a replacement for CodeWalker/OpenIV or a 3D content pipeline. SHVDN can coordinate runtime behavior, but it cannot turn arbitrary exterior meshes into production-quality interiors with correct room geometry and collision solely through ordinary gameplay scripts.

Compatibility constraints:
- Keep the current GTA V Enhanced installation and user save data intact.
- Keep existing SHVDN modules independently loadable.
- Preserve the existing GVL.* AppDomain contract.
- Do not migrate module saves destructively.
- Keep SQLite in the Python bridge unless a separately reviewed architecture change is approved.
- The UIS must fail independently: a missing or invalid interior pack must not crash Core, Economy, Personal, or Life.

## 3. High-level architecture

```text
GTA V Enhanced world
       |
       v
Building Census / Content Catalog (offline generated)
       |
       v
Entrance Registry ---- Building & Property Registry
       |                         |
       v                         v
Entrance Interaction       Persistent Property State
       |
       v
Interior Resolver
       |
       +--> Native GTA interior / IPL
       +--> Authored MLO
       +--> Reusable template instance
       +--> Modular room assembly
       |
       v
Interior Runtime / Streaming / Exit & Recovery
       |
       v
Life, Economy, NPC, Inventory and Social adapters
```

The building catalog and property simulation are separate. A building can be entered even if it has no owner, rent, business, or NPC resident. Economy and AI consume building events; they do not own the interior lifecycle.

## 4. Core data model

### 4.1 Building identity

Each catalog entry has a stable building_id, independent of runtime entity handles. Suggested identity inputs:

- world/map pack identifier;
- exterior archetype/model hash;
- quantized world anchor coordinates;
- optional authored stable ID for hand-authored exceptions.

Do not use a transient entity handle as a persistent key. Handles can change across sessions and streaming cycles.

### 4.2 Building record

| Field | Purpose |
|---|---|
| building_id | Stable unique key |
| map_pack | Base game or named expansion pack |
| building_type | House, apartment, shop, office, warehouse, public, etc. |
| exterior_model | Exterior archetype/model identifier |
| zone / street | Human-readable location metadata |
| entrance_ids | One or more catalogued entrances |
| interior_profile_id | Native, MLO, template, or modular profile |
| coverage_status | Discovered, reviewed, authored, validated, blocked |
| access_policy | Public, owned, rented, business-hours, restricted |
| content_version | Catalog compatibility/version |

### 4.3 Entrance record

Each entrance must store:
- stable entrance_id and owning building_id;
- exterior position and heading;
- interaction radius and facing tolerance;
- entry transform and exit transform;
- interior profile / destination identifier;
- door animation or door-state policy, if supported;
- accessibility and lock policy;
- safe fallback transform.

Buildings with multiple doors must have multiple entrance records. The resolver must not guess the nearest arbitrary point on a facade.

### 4.4 Interior profile

An interior profile describes reusable content:
- content type: native, MLO, template, modular;
- asset pack and asset version;
- interior or IPL identifiers where applicable;
- room graph and valid navigation/interaction anchors;
- spawn and exit transforms;
- collision validation metadata;
- furniture slots and optional room variants;
- capacity and streaming budget class.

### 4.5 Persistent property state

Keep static world geometry out of player save files. Persist only changing state:
- owner, tenant, lease, and access rights;
- furniture and purchased upgrades;
- storage/inventory references;
- utilities, maintenance, rent and business state;
- resident NPC identities and schedules;
- damage, cleanliness and other simulation variables.

For the current repository, use a versioned local key=value save compatible with the existing game-side save policy, and synchronize events/state through the Python bridge. Do not put SQLite access into SHVDN scripts.

## 5. Building Census and content pipeline

A complete city cannot be catalogued reliably by scanning nearby entities every frame. The world census must be an offline content pipeline.

### Stage A — discover exterior building candidates

1. Enumerate base-game map archetypes and placed building instances using map inspection tools.
2. Exclude decorative props, bridges, tunnels, fences, and non-occupiable infrastructure by explicit classification.
3. Group repeated building instances and detect likely entrances using model metadata, door entities, authored map markers, and spatial review.
4. Export a stable building/entrance manifest.

Automated discovery is a candidate generator, not the final authority. A mesh does not always expose a semantically correct front door, and some buildings share facades or have inaccessible geometry.

### Stage B — coverage review

Build a coverage dashboard/report grouped by map zone and building type. Every candidate must end in one of these states:

- validated_native
- validated_mlo
- validated_template
- validated_modular
- blocked_documented (only for genuine technical exclusions, with reason)
- needs_authoring

The product acceptance target is zero unreviewed eligible buildings in the supported map catalog. A “100% coverage” claim is prohibited until the census, entrance, collision, and interior checks pass.

### Stage C — interior asset authoring

Use:
- existing GTA V interiors/IPLs where appropriate;
- CodeWalker/OpenIV-compatible map asset workflows for custom MLO/YMAP/YTYP/YDR/YBN resources;
- modular room kits for common residential archetypes;
- custom authored interiors for landmarks, premium properties, complex commercial spaces.

Every asset pack must have a manifest, version, dependency list, collision checks, streaming checks, and a known-good fallback.

### Stage D — package and validate

Package assets separately from the C# scripts. The installer must:
- back up any files it replaces;
- never overwrite the user's saves;
- verify expected files and versions;
- support clean uninstall/rollback;
- report missing dependencies before game launch where possible.

## 6. Runtime components

### 6.1 BuildingCatalog

Loads a validated, versioned manifest once at startup. Provides spatial queries by zone/grid. No full-world scan in the per-frame tick.

### 6.2 EntranceInteraction

Uses a spatial index to query only entrances near the player. Shows a contextual prompt and validates player position, heading, movement state, and access rules. Avoids conflicting with existing menu keys and interaction scripts.

### 6.3 InteriorResolver

Resolves a building to one of the four interior realization strategies. Resolution order should be explicit and content-driven, not hard-coded by business logic:

1. valid authored native interior;
2. valid custom MLO;
3. assigned reusable template;
4. modular fallback profile;
5. safe unavailable/error response only if content validation has already marked the building blocked.

### 6.4 InteriorRuntime

Owns transition state:
- capture exterior return transform and dimension/interior context;
- request/load the required IPL or asset pack;
- wait for streaming readiness and collision readiness;
- move player to the interior spawn;
- apply interior context where required;
- restore the exact exterior transform on exit;
- recover safely if loading fails, the script aborts, or the player is teleported/dead.

Transitions must be asynchronous/state-machine driven. Never freeze the game while waiting for streaming.

### 6.5 InteriorInstanceManager

A reusable template at one fixed set of world coordinates is not automatically a separate physical copy for every household. The manager must enforce occupancy and isolation.

For the first release, use a single-player interior lease:
- one active player interior instance at a time;
- NPCs/property state not currently instantiated remain simulated in data;
- do not spawn two households into the same physical template simultaneously;
- if concurrent NPC occupancy is required later, author separate physical instances or a validated instancing/room-cell strategy.

### 6.6 BuildingSimulationAdapter

Publishes events such as:
- building.entered
- building.exited
- property.purchased
- property.rented
- property.furniture_changed
- property.utility_due

Economy, Life, Personal, Social and NPC systems subscribe to these events. UIS must not directly mutate bank balances or relationship values.

## 7. Performance and memory policy

- Do not stream all interiors at startup.
- Load only the active interior and nearby exterior interaction metadata.
- Unload or deactivate interior assets after a safe exit and reference release.
- Use spatial grids/quadtree-style lookup for entrance candidates.
- Cache catalog records, not live game entities.
- Limit dynamic furniture and NPC spawn counts per interior profile.
- Use lower-cost simulation for residents outside the active interior; do not keep every NPC entity alive.
- Instrument transition time, memory/asset count, entity count, and frame-time impact.

A reusable template reduces authoring cost but does not guarantee unique simultaneous occupancy. A custom MLO improves spatial fidelity but increases asset authoring and streaming costs.

## 8. Failure handling and save safety

- If an asset is missing, do not teleport the player into void geometry. Keep the player outside and show a diagnostic.
- If streaming times out, cancel the transition and restore the exterior state.
- If the script aborts while inside, restore the player to the last safe exterior transform on next initialization where possible.
- Persist the last building/entrance and return transform only after validation.
- Never overwrite the existing four-home save fields during migration. Map old home locations to new building_id records through a one-time, reversible compatibility layer.
- Unknown catalog versions must fail closed for interior loading, not corrupt property saves.

## 9. Proposed code boundaries

New game-side files (names provisional):
- game/GtaVietLifeBuildings.cs — catalog, spatial lookup and entrance interaction.
- game/GtaVietLifeInteriors.cs — resolver, transition state machine, instance manager.
- game/GtaVietLifePropertyAdapter.cs — property events and integration with existing modules.

Content:
- content/buildings/ — versioned building and entrance manifests.
- content/interiors/ — profile manifests and asset pack metadata.
- tools/interiors/ — offline census/export/validation tooling (separate from SHVDN runtime).

Do not move or rewrite the current GtaVietLifeWorld.cs wholesale. First introduce adapters and compatibility reads for GVL.Home, then migrate existing home selection to the registry in a separate, reversible step.

## 10. Delivery roadmap

### Phase 0 — feasibility spike
- Verify GTA V Enhanced + current SHVDN compatibility for the selected interior natives and asset loading path.
- Build one test house with one known entrance and a known-good exit.
- Test native interior and one custom MLO.
- Verify collision, camera, player recovery, save/load, and no conflict with existing modules.

**Exit gate:** repeated entry/exit works without soft-lock, missing collision, or save damage.

### Phase 1 — catalog foundation
- Define building/entrance/profile schemas.
- Create the offline building census and spatial index.
- Inventory one representative district and report coverage.
- Add runtime prompt and resolver behind a feature flag.

**Exit gate:** every eligible building in the pilot district has a reviewed entrance and valid interior assignment.

### Phase 2 — reusable interior library
- Author residential templates for major building archetypes.
- Add apartment, shop, office and warehouse profiles.
- Validate asset dependencies and fallback behavior.
- Add furniture anchors and interaction points.

**Exit gate:** all pilot buildings are enterable and interiors are not visibly/physically broken.

### Phase 3 — city-wide coverage
- Expand the census district by district.
- Author exceptions and special MLOs.
- Track all eligible building IDs and entrance IDs.
- Resolve duplicates, inaccessible facades, multi-door buildings and multi-floor access.

**Exit gate:** zero unreviewed eligible buildings in the supported base-game catalog; all remaining exclusions documented and approved.

### Phase 4 — living properties
- Integrate ownership, rent, residents, furniture, storage, maintenance and business use.
- Connect NPC schedules and off-screen simulation.
- Add dashboard visibility and diagnostics.

**Exit gate:** property state survives save/reload and is consistent with Economy/Life/NPC systems.

## 11. Acceptance criteria

A building counts as complete only if:
1. it has a stable catalog identity;
2. at least one valid entrance is reachable from the exterior;
3. entry loads a valid interior with collision and a usable navigation space;
4. the interior is appropriate to its building category;
5. exit returns the player to the correct exterior entrance;
6. failed streaming or script interruption has a safe recovery path;
7. the building's property state is independent of the visual asset;
8. it is included in coverage reporting and regression checks.

Global release criteria:
- all eligible buildings in the supported base-game catalog are accounted for;
- no unreviewed entrances remain;
- no known soft-lock or save-corruption path;
- interior transitions and asset streaming meet agreed performance budgets;
- existing GTA V saves and current GVL module saves remain compatible.

## 12. Explicit risks and non-goals

- “Every building is enterable” is a content production program, not a small SHVDN feature.
- Reusing a small number of templates can satisfy access coverage but will not make every home visually unique.
- Script-only procedural generation cannot replace authored collision-ready map assets.
- GTA V's streaming, memory, and interior systems impose hard constraints; these must be measured on the target Enhanced build.
- Third-party map packs are not included until their asset licenses, coordinates, dependencies, and compatibility are verified.
- Do not promise full NPC household simulation, multiplayer occupancy, or unlimited simultaneous interior instances in the first UIS release.

**Architectural decision:** Build UIS as a separate world subsystem, keep the property simulation data-driven, and treat the base-game city as a catalog that must pass measurable coverage validation. GTA V remains the rendering and movement layer; GTA Viet Life owns the building identity, access rules, property state, and life simulation.
