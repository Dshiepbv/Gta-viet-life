# Launcher implementation status

**Milestone:** Windows bootstrap and authenticated local Adapter session handshake.  
**Status:** Internal CI build only; not a user acceptance release.

## Source implemented
- WPF .NET 8 Launcher with Vietnamese status UI.
- One-time GTA executable selection and per-user settings.
- ScriptHookVDotNet detection before script installation.
- Automatic installation of GTA Viet Life C# and INI files, with timestamped backups of replaced files; save files and unrelated mods are not touched.
- Local random authentication token and loopback Engine Host process startup.
- Standard-library Engine Host health, handshake, heartbeat, close and status endpoints.
- Windows CI workflow for Python tests, self-contained Launcher/Updater publishing, Engine Host packaging, AI bridge packaging and installer/portable package generation.
- Latest stable GitHub release check, HTTPS download, SHA-256 checksum verification and external updater helper with directory-swap backup/rollback.\n- Diagnostic log export.

## Readiness states
- READY TO LAUNCH means Engine Host and legacy AI bridge are healthy, the expected mod runtime is detected and project scripts are installed.
- IN GAME / CONNECTED means the Adapter has completed the authenticated gvl-engine/1 handshake and heartbeat.
- Copying scripts or starting a process alone never counts as an in-game connection.

## Remaining gates before final user acceptance
- Execute Windows CI and correct every build/package failure.\n- Provision publisher signing/certificate infrastructure before public production updates.
- Adapter handshake/heartbeat is integrated into the GTA V Core script; runtime compilation and in-game handshake remain unverified.
- Verify exact ScriptHookVDotNet Enhanced/Legacy compatibility against a supported game build.
- Verify PyInstaller bridge packaging, audio initialization and first-run speech-model behavior.
- Automatic latest-release check, HTTPS download, SHA-256 package verification and helper-based directory-swap rollback are implemented in source; publisher code-signing/manifest signature is not yet provisioned or verified.
- Complete Engine Host domain simulation and real in-world command/result flow.
- Run in-game and graceful-shutdown tests in a supported GTA V runtime.

No internal build is to be sent to the user for alpha testing. The final-only delivery policy remains in force.
