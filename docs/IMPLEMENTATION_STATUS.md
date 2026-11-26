# GTA Viet Life — architecture audit and implementation status

Date: 2026-11-27  
Working branch: `codex/gta-viet-life-architecture-hardening`  
Baseline: `main` (existing GTA V Enhanced single-player mod)

## Compatibility constitution

The repository's existing constitution is `docs/CORE.md`. Its compatibility rules remain authoritative:

1. Keep modules independently loadable as ScriptHookVDotNet scripts.
2. Preserve the `GVL.*` AppDomain data contract.
3. Preserve legacy per-module `key=value` save files; add versioned keys rather than renaming old keys.
4. Keep SQLite in the Python bridge; in-game modules continue to use text saves.
5. A missing optional core must not crash a feature module.
6. All monetary flows must use the shared payment/earning delegates where those delegates are available.

This change does not replace the monolithic game modules or migrate player data.

## Architecture audit — 20 development areas

Status labels: **Existing** means relevant code is present; **Partial** means important integrity, coverage, or verification gaps remain; **Not verified** means source/runtime evidence is insufficient to claim completion.

| # | Area | Existing baseline | Status / next engineering boundary |
|---:|---|---|---|
| 1 | Core engine and modular architecture | Core script, independent feature scripts, `GVL.*` contract | Partial — shared contract exists; dependency ownership and module lifecycle need formalization |
| 2 | Transaction engine and financial integrity | Economy payment delegate, cash/bank ledger, loans | Partial — some modules still have independent money paths; no cross-module transaction journal |
| 3 | Save, backup and recovery | Separate text saves; some modules write directly | Improved in Core — atomic replacement, backup fallback, durable event outbox; other module saves remain to audit |
| 4 | Time and offline simulation | Game clock and per-module catch-up logic | Partial — separate day tracking and caps can diverge |
| 5 | Persistent event bus | AppDomain list + sequence and bridge forwarding | Improved in Core — persisted outbox and retry-after-failure; delivery is at-least-once |
| 6 | Security and synchronization | Localhost AI bridge, optional dashboard PIN, Apps Script key/PIN | Partial — deployment secrets and transport hardening require review |
| 7 | Economic balancing and channel pricing | GT/MT, wholesale/retail, product costs and discounts in World | Partial — values are distributed across source and INI files |
| 8 | Income, purchasing power and living expenses | Jobs, shops, rent, utilities, services and bills | Partial — no central Vietnamese household/economic parameter model |
| 9 | Business and replenishment | Shops, stock, employees, supplier orders and B2B | Partial — automation and stock valuation need unified invariants |
| 10 | Supply, demand and competition | Customer flow and business simulation exist | Not verified — no unified market-clearing or competitor model established |
| 11 | Banking, credit and taxation | Bank balance, loans, repayment, credit score, taxes | Partial — tax and debt policies are not centralized or comprehensively tested |
| 12 | Careers and labor market | Delivery work, employee hiring and wages | Partial — no persistent labor-market matching model established |
| 13 | Personal needs and health | Needs, hygiene, illness, injury, sleep and stress | Existing / partial — features exist, balance and cross-module effects need verification |
| 14 | Housing, vehicles and assets | Property, rentals, personal garage, fuel, wear and impound | Existing / partial — asset ownership and valuation need a common registry |
| 15 | Family and social relationships | NPC affinity, dating, marriage, contacts and pets | Existing / partial — relationship state spans game and bridge persistence |
| 16 | NPC AI, memory and behavior | Python LLM bridge, SQLite memory, schedules and actions | Existing / partial — AI provider reliability, memory lifecycle and deterministic fallback need tests |
| 17 | Persistent society and world simulation | Residents, schedules, traffic rhythm, rumors and random events | Partial — population scale and offline world causality are not verified |
| 18 | Dashboard, Android and cloud | Python dashboard, Android WebView, Apps Script relay | Existing / partial — authentication, conflict handling and command idempotency need hardening |
| 19 | System-wide performance | Tick-based modules, cached model lists, background bridge calls | Not verified — no profiler baseline or repeatable performance suite |
| 20 | Release packaging and versioning | Install scripts and Android Actions build | Partial — no unified game-mod release manifest or compatibility matrix |

## Changes delivered on this branch

### Core persistence

- Kept the original `GtaVietLifeCore.sav` filename and legacy `day=<integer>` field.
- Added `seq` and Base64-encoded `outbox` fields. Older saves without these fields remain readable.
- Writes to a temporary file, preserves the previous save as `GtaVietLifeCore.sav.bak`, then atomically replaces the active save.
- If the active save cannot be read, attempts recovery from the backup.
- Restores the event sequence on startup to avoid reusing sequence IDs after a restart.

### Event delivery

- The core no longer clears pending events before the HTTP request.
- Sends bounded batches and removes the acknowledged prefix only after a successful bridge response.
- Retains events on network failure and persists the pending queue.
- Delivery semantics are **at-least-once**, not exactly-once. A crash after the bridge accepts a batch but before local acknowledgement can replay it. Downstream consumers must deduplicate using the leading sequence number.

## Migration and rollback

- No conversion of existing game, economy, personal, world, resident, or NPC data is performed.
- Existing `day=` core saves are read as before.
- New core saves add optional `seq=` and `outbox=` keys; older builds ignore unknown keys only if they parse selectively. To roll back, keep the generated `.bak` and restore the prior core script/save pair together.
- The new event outbox is local to the game working directory. Back it up with the other `GtaVietLife*.sav` files.
- Do not run this mod in GTA Online. This project remains single-player only.

## Verification and release gates

The repository does not currently provide a game-mod solution/project or an automated GTA V runtime harness. The Android workflow is not evidence that the C# ScriptHookVDotNet scripts compile or behave correctly in GTA V Enhanced.

Before promoting this branch to a release:
- Compile all game scripts against the exact installed ScriptHookVDotNet Enhanced API/runtime.
- Test loading legacy saves, backup recovery, disk-full/permission failures, and abrupt process termination.
- Simulate bridge unavailable, timeout, HTTP error, and bridge-accepted/local-crash cases; verify at-least-once replay and sequence deduplication.
- Run a 24-hour accelerated in-game simulation and compare balances, inventory, payroll, tax, debt and asset totals.
- Verify the Android APK and Apps Script command path with PIN enabled and disabled.
- Confirm no GTA Online entry path is enabled.

## Explicitly unresolved

This branch is a foundational hardening increment, not completion of all 20 development areas and not yet a release candidate. The largest remaining architectural work is a unified transaction journal, one authoritative simulation clock, central economic configuration with Vietnamese regional/household profiles, idempotent dashboard commands, schema/version migration for every save file, and repeatable game-runtime tests.
