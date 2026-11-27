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
- Windows CI workflow for Python tests, self-contained Launcher publishing and runtime packaging.
- Diagnostic log export.

## Readiness states
- READY TO LAUNCH means Engine Host and legacy AI bridge are healthy, the expected mod runtime is detected and project scripts are installed.
- IN GAME / CONNECTED means the Adapter has completed the authenticated gvl-engine/1 handshake and heartbeat.
- Copying scripts or starting a process alone never counts as an in-game connection.

## Remaining gates before final user acceptance
- Execute Windows CI and correct every build/package failure.
- Integrate the Adapter handshake into the actual GTA V Core script.
- Verify exact ScriptHookVDotNet Enhanced/Legacy compatibility against a supported game build.
- Verify PyInstaller bridge packaging, audio initialization and first-run speech-model behavior.
- Implement and verify signed automatic updates, transactional apply and rollback.
- Complete Engine Host domain simulation and real in-world command/result flow.
- Run in-game and graceful-shutdown tests in a supported GTA V runtime.

No internal build is to be sent to the user for alpha testing. The final-only delivery policy remains in force.
