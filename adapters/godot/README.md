# Godot 4 second-game adapter — M5 portability proof

This is a separate base-game adapter, not a second simulation implementation. It consumes the same authenticated gvl-engine/1 Engine Host API used by the GTA V adapter. Canonical NPC profiles, schedules, needs, relationships, memories, simulation clock and SQLite persistence remain in engine/.

## Requirements

- Godot 4.2 or newer.
- A running Engine Host listening on 127.0.0.1:5580.
- The per-user Host token supplied to the Godot process as GVL_ENGINE_TOKEN (at least 32 characters). Never commit or hard-code a token.

## Run

1. Start Engine Host using the same local data directory and token configured by the Launcher.
2. Set GVL_ENGINE_TOKEN in the process environment.
3. Open this folder in Godot 4 and run the project.
4. The adapter authenticates, registers both npc.godot.minh-an and player.godot in the shared actor model, observes the player's body needs, polls semantic activity commands, moves the native Godot resident and player to mapped locations, and acknowledges physical actions.
5. Approach the green social area and press Enter to talk. The interaction is sent to the shared Engine Host; relationship affinity and episodic memory are committed by the shared domain logic.

## Scope and truthful boundary

- Narrow slice: one persistent NPC and one first-class player actor, schedule-driven location/activity transition, player hunger/thirst/energy/hygiene observation, bed/fridge/counter/toilet/shower interactions, durable relationship/memory and restart persistence through the shared Host.
- Godot owns only presentation, input, native Node2D movement and semantic-location-to-scene-marker mapping.
- The adapter contains no duplicate NPC schedule or relationship rules. It owns the native player-body meter presentation and reports observations; canonical actor state, relationships and physical interaction consequences remain in Engine Host.
- The Host API is unchanged: handshake, NPC registration, heartbeat, command polling/acknowledgement, interaction and session close.
- This is a portability reference adapter. It is not packaged into the GTA Windows installer and has no claimed Godot runtime pass until run with Godot 4 and a live Host.
- It is a narrow M5 proof, not a complete Godot game or GTA replacement.

## Acceptance checklist

- [ ] Godot 4 parses and runs the project.
- [ ] Handshake identifies godot4.reference and negotiates the shared protocol.
- [ ] NPC profile registration succeeds without changing Engine source.
- [ ] A schedule transition creates the same actor.set_activity command used by GTA.
- [ ] The Godot resident moves to the mapped marker; acknowledgement occurs after arrival.
- [ ] In-world talk increments affinity and stores memory in the shared SQLite store.
- [ ] Restarting Host and Godot client preserves NPC identity, relationship and memory.
- [ ] Engine domain tests pass unchanged with either adapter connected.


### NPC physical routine in the Godot reference world

The sample resident's daily schedule now routes sleep into the home interior and bed anchor, meals to the cafe counter, work to the workplace counter, and leisure to the public park. The adapter acknowledges only after the resident's movement tween reaches the selected physical anchor. These mappings are local presentation; the shared Engine Host still owns the schedule, activity state, needs and relationship rules.
