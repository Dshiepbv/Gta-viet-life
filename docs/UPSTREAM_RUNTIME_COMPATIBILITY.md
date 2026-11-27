# GTA V runtime compatibility — pinned upstreams and fail-safe policy

**Purpose:** Prevent the Launcher from treating a file-presence check as proof that a GTA V mod runtime supports the installed game build.

## 1. Supported .NET scripting runtime upstream

- Repository: https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced
- Pinned release: v1.1.0.6
- Release commit shown by GitHub: d756d47
- Release asset: ScriptHookVDotNetEnhanced-v1.1.0.6.zip
- GitHub release asset SHA-256 digest: f10da8819fa6814fb0a04553567d2516934eac96b16af06ad8f315f0da00473a
- License: zlib (verify and retain the upstream license/notices with any redistributed copy).

The upstream project states that SHVDN Enhanced supports GTA V Legacy and Enhanced and requires C++ Script Hook V, .NET Framework 4.8 or later, and the Visual C++ Redistributable x64. Its installation guidance requires the ASI plugin and matching API DLLs from the same release. The release package also includes the release-specific INI and MinHook dependency; MinHook.x64.dll is required from v1.1.0.0 onward.

Required file-presence preflight in the GTA game root:
- ScriptHookV.dll
- ScriptHookVDotNet.asi
- ScriptHookVDotNet2.dll
- ScriptHookVDotNet3.dll
- ScriptHookVDotNet.ini
- MinHook.x64.dll
- an ASI loader appropriate to the installed edition (for example dinput8.dll or xinput1_4.dll).

File presence is necessary, not sufficient. Do not mix ASI/DLL files from different SHVDN/SHVDNE releases.

## 2. Script Hook V upstream

Official upstream/download page: https://www.dev-c.com/gtav/scripthookv/

The page currently lists Script Hook V v3889.0 / 1158.13 and supported patches 1.0.3889.0 (Legacy) and 1.0.1158.13 (Enhanced). Script Hook V is a required dependency for the .NET runtime and is separate from the GTA Viet Life Adapter.

Do not download Script Hook V DLLs from third-party mirrors or silently replace a user's native loader. The Launcher detects the required files and reads ScriptHookV.log. An explicit “Unknown game version” result blocks readiness. A success line must match the current executable file version before the Launcher labels the Script Hook V build as confirmed.

## 3. Current game-build caution

SHVDN Enhanced v1.1.0.6 release notes specifically mention an updated memory pattern for GTA V Enhanced 1.0.1158.13. Rockstar's Title Update 1.73 notes include a PC Enhanced update on 27 October 2026; public Steam build tracking identifies a later 1.0.1158.16 build.

The upstream release page does not explicitly list 1.0.1158.16 as supported. Therefore:
- Do not hard-code 1.0.1158.16 as supported merely because older files exist.
- If ScriptHookV.log reports “Unknown game version”, block READY_TO_LAUNCH.
- If there is no current-build success log, show the runtime as unverified; do not claim a successful game connection.
- IN_GAME_CONNECTED is shown only after the Adapter is actually loaded and its authenticated heartbeat reaches the Engine Host.
- Do not auto-update or replace third-party runtime files until the exact game build and upstream compatibility are verified.

## 4. Launcher checks

The Launcher currently:
1. Reads the selected GTA executable's FileVersionInfo.
2. Requires the base Script Hook V, SHVDN Enhanced, release INI, MinHook and ASI-loader files.
3. Checks .NET Framework 4.8+ and Visual C++ Redistributable x64 registry markers.
4. Blocks if ScriptHookV.log contains an unknown-game-version failure.
5. Reports whether the log contains a success line matching the selected executable's exact file version.
6. Requires the in-game Adapter handshake/heartbeat before reporting IN_GAME_CONNECTED.

The handshake reports the actual process executable edition and file version to the Engine Host for diagnostics.

## 5. Sources and provenance

- SHVDN Enhanced source/release: https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced/releases/tag/v1.1.0.6
- SHVDN Enhanced README and requirements: https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced
- Original SHVDN project and its compatibility warning for older stable builds: https://github.com/scripthookvdotnet/scripthookvdotnet
- Official Script Hook V page and supported patches: https://www.dev-c.com/gtav/scripthookv/
- Rockstar GTAV Title Update 1.73 notes: https://support.rockstargames.com/articles/4vRqEDvjUs9h7nqRUgc8YO/gtav-title-update-1-73-notes-ps5-ps4-xbox-series-x-or-s-xbox-one-pc-enhanced-legacy

External version observations must be rechecked before every final release. GitHub repository/release references and exact asset hashes are pinned in this document; moving branches are not accepted as reproducible runtime dependencies.
