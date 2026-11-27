# Launcher implementation status

**Milestone:** Windows single-entry bootstrap, automatic release check and authenticated GTA Adapter session handshake.  
**Status:** Internal CI package only; not a user acceptance release.

## Source implemented

- WPF .NET 8 Launcher with Vietnamese status UI, one-instance guard and saved GTA executable path.
- Latest stable GitHub release check on startup. A newer package is downloaded over HTTPS, checked against the SHA-256 checksum asset and applied by an external updater helper.
- Updater stages the archive, checks for the Launcher executable, swaps the install directory and retains the previous version. Failed extraction or failed launch restores the previous directory.
- Preflight requires Script Hook V, the pinned SHVDN Enhanced file set (ASI, API v2/v3 DLLs, INI and MinHook), .NET Framework 4.8+ and Visual C++ Redistributable x64. It reads the selected game executable version and the latest ScriptHookV.log result; exact runtime/game compatibility is not inferred from file presence.
- Automatic installation of GTA Viet Life C# modules and UI icon assets into the GTA scripts folder. Changed project code and icon assets are backed up. Existing INI settings are preserved; only the required Engine Port key is merged into the Core INI. Existing saves and unrelated mods are not overwritten.
- Script replacement and application updates are deferred while GTA V is running.
- Random per-user authentication token, loopback Engine Host process management, authenticated protocol request IDs, handshake, heartbeat, close and status.
- Legacy AI bridge starts as a separately packaged process, uses a per-user data/config directory and exposes an identity health endpoint. The legacy /ping route remains compatible with existing game scripts.
- Launcher supervises local services, attempts recovery after health-check failures and exports diagnostics.
- Windows CI builds the self-contained Launcher and updater, packages Engine Host and AI bridge, creates an Inno Setup installer and a portable ZIP with SHA-256 checksum.

## Readiness semantics

- **READY_TO_LAUNCH:** selected GTA executable exists; expected ScriptHookVDotNet files are detected; GTA Viet Life scripts are installed; authenticated Engine Host identity/health and AI bridge identity/health checks pass. This does not claim that a live game session is connected or that the detected runtime version is supported.
- **IN_GAME_CONNECTED:** GTA V Core has loaded and completed the authenticated gvl-engine/1 handshake and heartbeat. The current Adapter reports only adapter.session; it does not claim unimplemented gameplay capabilities.

## Verification

- Python Engine Host unit tests cover health, token authentication, handshake, heartbeat, protocol rejection and stale-session detection.
- Python source compilation is part of Windows CI.\n- The modified GTA V Core compiles against the pinned ScriptHookVDotNet Enhanced v1.1.0.6 API v3 assembly; the compile reported one pre-existing obsolete API warning for World.CurrentTimeOfDay and no errors.\n- The WPF Launcher and self-contained updater compile successfully after the latest UI fix.
- Windows CI has successfully published the .NET 8 Launcher and updater, packaged Engine Host and AI bridge, and generated an installer/portable ZIP in prior internal runs.
- Additional CI gates now verify required portable-package contents and updater rollback behavior; the latest run is tracked in the repository Actions page.
- The latest full Windows packaging run is rebuilding the installer after the compatibility-preflight changes.\n- No GTA V or ScriptHookVDotNet runtime test has been run in this environment.

## Remaining gates before final user acceptance

1. Complete the latest Windows CI run, including package-content and updater rollback tests, and resolve any failures.
2. Provision publisher code-signing/manifest-signature infrastructure. Current updater verifies HTTPS transport and SHA-256 against the official GitHub release checksum; this is not a publisher signature.
3. Verify the exact GTA V Enhanced/Legacy and ScriptHookVDotNet versions against a real supported installation.
4. Verify the in-game Core script loads under that runtime and the authenticated handshake/heartbeat stays healthy through game reloads and shutdown.
5. Verify bridge audio initialization, microphone selection, first-use Whisper model download and AI provider configuration on Windows.
6. Implement the full authoritative Engine Host domain simulation, persistence and semantic command/result path. The current Host is a lifecycle/session bootstrap, not the completed life simulator.
7. Run end-to-end in-game regression and graceful-shutdown checks before producing the final user acceptance package.

No internal build is to be sent to the user for alpha testing. The final-only delivery policy remains in force.
