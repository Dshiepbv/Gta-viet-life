# GTA Viet Life — Launcher, Runtime Orchestration and Update Architecture

**Status:** Approved target architecture  
**Applies to:** Windows player installation, Engine Host, GTA V Adapter and release packaging  
**User experience:** Open Launcher → automatic preflight/update/startup → explicit Ready state → launch/play GTA V.

## 1. Player-facing outcome

The player must not manually copy C# files, run Python commands, start a server, edit source files or assemble a release from separate downloads.

The supported installation and daily workflow is:

1. Install GTA Viet Life once using the supplied Windows installer. Installer code-signing is a pending production gate.
2. Open **GTA Viet Life Launcher**.
3. Launcher checks the installation, game/mod-runtime compatibility, disk space, required files and update manifest.
4. If a newer stable GitHub release is available, Launcher downloads the package over HTTPS, checks its SHA-256 against the release checksum asset, then hands it to the external updater for directory-swap installation with a retained backup. Publisher code-signing is not yet provisioned.
5. Launcher starts or attaches to the local Life Simulation Engine Host.
6. GTA V Adapter is loaded by the supported GTA mod runtime when the game starts.
7. Adapter connects to the Engine Host, negotiates protocol version and capabilities, and reports a healthy game session.
8. Launcher reports **READY / SẴN SÀNG** only after all required readiness gates pass.
9. Player launches GTA V from Launcher or opens GTA V after Launcher reports readiness, according to the supported mod-runtime integration.
10. On exit, the system flushes durable state, records shutdown status and closes managed processes cleanly.

No intermediate/alpha build is offered to the player. This workflow is for the final integrated release only.

## 2. Components and ownership

### 2.1 GTA Viet Life Launcher (Windows desktop application)

Responsibilities:
- Single player-facing entry point and status dashboard.
- Installation discovery and preflight diagnostics.
- Version/update manifest retrieval and validation.
- Transactional update orchestration, backup and rollback.
- Engine Host process lifecycle management.
- GTA/mod-runtime detection and launch orchestration.
- Adapter/Engine health display and actionable error messages.
- Log collection/export, save backup/restore and repair installation.
- Release channel selection only if supported and explicitly configured.

Non-responsibilities:
- No authoritative NPC, economy, world or simulation rules.
- No direct manipulation of GTA entities.
- No arbitrary remote command execution.
- No public network listener.

### 2.2 Life Simulation Engine Host

Responsibilities:
- Authoritative game-neutral world/domain state and simulation.
- NPC, household, business, economy, schedule, relationship and off-screen simulation.
- Persistence, schema migrations, event processing and deterministic validation.
- Versioned local API for adapter communication.
- Health/readiness endpoint and structured diagnostics.

The Host runs locally under the player's account with least privilege. Bind only to loopback unless a separately reviewed feature requires otherwise. Do not expose the simulation API to the public internet.

### 2.3 GTA V Adapter

Responsibilities:
- Load inside the verified supported GTA V mod runtime.
- Translate game-neutral semantic commands into native GTA actions.
- Observe actual game entities, player state, world position and interaction outcomes.
- Report capabilities, game build, adapter version, session identity and execution results.
- Respect game-thread constraints and marshal work to the correct runtime thread.
- Preserve existing GTA Viet Life module compatibility during incremental migration.

The adapter is not optional for in-world gameplay. Launcher cannot replace it or truthfully claim the game is ready when it is absent.

## 3. Readiness state machine

Launcher must expose explicit states, not a single optimistic spinner:

- **NOT_INSTALLED** — required components are missing.
- **PREFLIGHT** — checking OS, game path, mod runtime, dependencies, storage and versions.
- **UPDATE_REQUIRED** — a compatible update is available.
- **DOWNLOADING** — update payload is being downloaded to staging.
- **VERIFYING** — manifest, hashes, signature, compatibility and package contents are checked.
- **INSTALLING** — verified payload is applied transactionally with rollback data retained.
- **STARTING_ENGINE** — Engine Host process is starting.
- **WAITING_FOR_GAME** — Host is healthy; waiting for GTA V and Adapter.
- **HANDSHAKING** — Adapter and Host negotiate protocol, build and capabilities.
- **READY_TO_LAUNCH** — verified local package, detected mod-runtime files, installed Adapter scripts, healthy Engine Host and healthy legacy AI bridge; game process may not yet be running.\n- **IN_GAME_CONNECTED** — active game Adapter has completed authenticated protocol handshake and a fresh heartbeat.
- **DEGRADED** — optional feature unavailable; gameplay may continue only if policy explicitly permits it.
- **BLOCKED** — a mandatory dependency or compatibility check failed.
- **STOPPING** — graceful shutdown and durable flush.
- **RECOVERY_REQUIRED** — interrupted update, invalid installation or failed rollback needs repair.

### Mandatory READY gates

READY is permitted only when all are true:
1. Installed package manifest is valid and all required files match their declared hashes.
2. Game build and mod-runtime version are in the declared compatibility matrix.
3. Engine Host process is alive and its health check succeeds.
4. Host schema/migration check completes without unresolved error.
5. Adapter has loaded in the active GTA process and identified the game build.
6. Adapter/Host protocol versions are compatible.
7. Required adapter capabilities for the release profile are negotiated.
8. Session handshake succeeds and a heartbeat/health response is received within the configured timeout.
9. No fatal persistence, security or startup error is present.

The UI must show which gate failed and the next repair action. Never show READY merely because the Launcher or Host process started.

## 4. Update and rollback protocol

### 4.1 Release manifest

Every release manifest must include:
- product and release version;
- release channel;
- minimum/maximum supported Launcher version;
- Engine Host version;
- Adapter version;
- protocol major/minor compatibility;
- supported GTA V build(s);
- supported mod-runtime name and exact version range;
- package file list, sizes and SHA-256 hashes;
- package digest and checksum provenance; (publisher signing key is a pending production gate);
- schema versions and migrations;
- dependency versions and licenses;
- release notes, known issues and rollback compatibility.

Manifest schema is canonical in this GitHub repository. Production signing keys must not be stored in the repository or distributed in the installer source.

### 4.2 Safe update sequence

1. Retrieve manifest over HTTPS from the configured official release endpoint.
2. Validate manifest schema, release channel and compatibility.
3. Verify publisher signature using a trusted public key embedded in the installed Launcher.
4. Download all required packages into a version-specific staging directory.
5. Verify size and SHA-256 for every file; reject missing, unexpected or modified files.
6. Check free disk space and ensure backups can be written.
7. Quiesce managed processes and flush durable state.
8. Back up the current installation metadata and files that the update will replace. Player saves and user configuration are preserved separately and are never overwritten by application payloads.
9. Apply the update using an atomic directory/version switch where supported; otherwise use a journaled file-by-file transaction.
10. Run post-install integrity and startup checks.
11. Commit the new active version only after checks pass.
12. If any step fails, restore the prior version and report the exact failure. Never delete the last known-good version before the new version is verified.

Updates must be resumable where safe, idempotent, and protected against power loss or process termination. Never execute unverified downloaded scripts or binaries.

### 4.3 Update policy

- Security and critical compatibility updates may be marked mandatory.
- Ordinary feature updates should be announced with version and change summary.
- Do not silently switch release channels.
- Do not auto-update while GTA V or the Adapter is actively using files.
- Preserve user saves, configuration, logs and custom content.
- Support repair/reinstall and rollback to the last known-good compatible package.
- If offline, allow the last verified installed release to run when its compatibility and security policy permit; clearly show that update checks are unavailable.

## 5. Startup and shutdown orchestration

### Startup
1. Acquire a single-instance Launcher lock.
2. Read installation manifest and validate local files.
3. Run compatibility preflight.
4. Apply eligible verified updates.
5. Start Engine Host as a child process under the current user, with a private working directory and redirected structured logs.
6. Wait for Host health and protocol endpoint.
7. Launch GTA V or wait for the user to launch it, depending on verified mod-runtime support.
8. Adapter loads and registers its game session.
9. Complete handshake and capability negotiation.
10. Show READY and enable the Play/Resume action.

### Shutdown
1. Stop accepting new world commands.
2. Drain or durably persist pending events and command results.
3. Commit save transactions and checkpoint persistence.
4. Send Adapter session-close notification where possible.
5. Stop Engine Host gracefully; enforce a timeout and log forced termination if required.
6. Release Launcher lock and record the final session status.

A crash must not silently discard acknowledged or queued events. Use durable outbox/inbox semantics and idempotency keys.

## 6. Diagnostics and support bundle

Every session log must identify:
- Launcher, Host, Adapter, protocol and game versions;
- session ID and startup/shutdown timestamps;
- readiness-state transitions and failed gate;
- process exit codes and dependency checks;
- handshake/capability negotiation result;
- update manifest version, package hashes and rollback result;
- migration/save revision and persistence errors;
- correlation IDs for commands/events;
- exception type, stack trace and subsystem.

Provide **Export diagnostics** to create a redacted support archive. Never include passwords, API keys, signing private keys, access tokens or unnecessary personal data. Save data is excluded by default; the player must explicitly opt in to include it.

## 7. Security and trust boundaries

- Launcher, Host and Adapter communicate over loopback using a versioned protocol.
- Use an installation-scoped random local authentication secret or equivalent OS-protected local IPC authentication; do not rely on obscurity of the port.
- Validate message sizes, schemas, session identity and command authorization.
- The Adapter accepts only commands valid for its negotiated capabilities and current game session.
- The Host validates every state-changing result and never trusts AI output as authoritative state.
- Apply least privilege; do not require administrator rights for normal gameplay after installation.
- Pin dependencies and release artifacts; record provenance and licenses in the repository.
- Never download or execute arbitrary code from untrusted endpoints.

## 8. Compatibility and installation

- Existing GTA Viet Life saves and legacy `GVL.*` contracts remain supported through explicit migrations/adapters.
- Installer detects an existing installation and offers a safe in-place upgrade with backup.
- Do not overwrite unrelated ScriptHookVDotNet scripts or third-party mods.
- Detect the actual installed mod runtime and supported API; do not assume a specific Enhanced/Legacy runtime or loader until verified.
- Keep the GTA Adapter thin and game-specific; keep domain/simulation rules in the independent Host.
- Keep the existing Python bridge/SQLite role compatible until a migration is implemented and verified. Do not silently replace it with a new database or runtime.

## 9. Technology selection gates

The architecture is approved; implementation technology is selected only after repository and target-environment verification:
- Prefer a Windows-native .NET desktop Launcher if supported by the project's target Windows baseline and build environment.
- Select WPF/WinUI/other UI framework based on verified deployment/runtime constraints, not fashion.
- Package the Engine Host as a reproducible self-contained executable or managed runtime bundle; avoid requiring the player to install Python manually.
- Build the GTA Adapter against the exact supported ScriptHookVDotNet API/runtime and target framework.
- Use an installer technology that supports signed packages, upgrade/repair and rollback.
- Pin all SDKs, runtimes and dependencies in the release manifest.

Do not claim a working EXE, installer, updater or game integration until its actual build and runtime checks have succeeded.

## 10. Acceptance criteria

A final integrated release passes this subsystem only when:
1. A clean Windows installation can be completed through one installer.
2. The player can open one Launcher without manually copying scripts or starting services.
3. Launcher detects missing/incompatible dependencies and explains the repair path.
4. A valid update downloads, verifies, installs and restarts successfully.
5. Invalid signature/hash, interrupted download and interrupted install are rejected or safely recovered.
6. A failed update restores the last known-good release without touching saves/configuration.
7. Engine Host starts, reports healthy and shuts down cleanly.
8. GTA V loads the Adapter and completes a versioned capability handshake.
9. READY is shown only after all mandatory gates pass.
10. A real in-world gameplay command is executed by the Adapter and its confirmed result is persisted by the Host.
11. Diagnostics can be exported and identify the exact build/session/failure.
12. Existing saves and legacy modules remain compatible.

Until these gates are implemented and verified, this document defines the approved target architecture, not a claim that a Launcher or installer already exists.
