# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

AnoMech ("Another FFXIV mechanics simulator") is a Dalamud plugin that **simulates FFXIV ultimate-raid mechanics client-side**. From an inn, it loads the raid territory client-side (Hyperborea-style, with a packet firewall — see `Core/Map/ZoneSession.cs`), spawns fake `BattleChara` instances (party doppels and bosses), drives their casts/movement/tethers/VFX, and resolves damage so players can practice solo or with bot party members. Scenarios cover TOP, UMAD (Dancing Mad), UWU and UCOB phases.

The reference scenario is **TOP P5 Delta** (`AnoMech/Scenarios/Top/P5Delta/`). Treat it as the canonical example of how a scenario consumes the engine — when designing new APIs, look at how it would read there.

## Build / run

- Build: `dotnet build` from the repo root (solution `AnoMech.sln`, plugin project `AnoMech/AnoMech.csproj`). Release: `dotnet build --configuration Release` → `AnoMech/bin/Release/AnoMech/`.
- The csproj uses `Dalamud.NET.Sdk/15.0.0`; the SDK resolves Dalamud from `%AppData%/XIVLauncher/addon/Hooks/dev/` (on Linux, point `DALAMUD_HOME` at the dev hooks dir). CI (`.github/workflows/pr-build.yml`) builds on Windows after downloading `dalamud-distrib/latest.zip` into that path.
- Projects: `AnoMech/` (the plugin), `AnoMech.Relay/` (multiplayer relay library: wire format in `Network/`, server in `Server/`; the plugin references it), `AnoMech.Relay.Host/` (the standalone relay exe + Dockerfile). Each has an NUnit suite under `tests/<Project>.Tests`; tests of one project's code go in that project's suite. Inside a suite, test files mirror the production tree, one `<Type>Tests.cs` per production file (`AnoMech.Relay/Server/RelayRooms.cs` → `tests/AnoMech.Relay.Tests/Server/RelayRoomsTests.cs`); fakes of a production interface go in a `Fakes/` folder beside that interface's package (`AnoMech/Core/Native/Interfaces/` → `tests/AnoMech.Tests/Core/Native/Fakes/`); other shared helpers and fixtures go in `Support/`, never loose at the suite root. Test namespaces stay flat (`AnoMech.Tests`, `AnoMech.Relay.Tests`) so the `DalamudServices` `[SetUpFixture]` covers the whole suite.
- Tests: `dotnet test` from the root. They cover the multiplayer transport only (wire format, relay, `RelayClient`, protocol, config); `tests/AnoMech.Tests` needs Dalamud installed, the relay suite doesn't. Its `IGameData` (`DataminingGameData`) reads real sheets from xivapi/ffxiv-datamining CSVs, which the build downloads into `obj/datamining/<commit>/`; bump `DataminingCommit` in `AnoMech.Tests.csproj` to move to a newer patch, and add a sheet's name to `DataminingSheet` when `IGameData` starts reading it. Scenario behavior is covered only by `ScenarioCatalogTests`' headless runs (every strat, random seeds, no deaths); anything visual still needs "build clean → load DLL via Dalamud Dev Plugins → run a scenario in-game and watch." For UI / behavior changes, ask the user to run the plugin; you cannot.
- Debugging a failing scenario run: follow the `debug-scenario-test` skill. Each failing seed leaves `tests/AnoMech.Tests/TestResults/scenario-runs/<Scenario>-s<strat>-<seed>/` (trace, per-death world snapshots, scenario state). To answer a question the trace doesn't, write a probe (`ScenarioRunOptions.Probe`) in a scratch test, never temporary log lines in production code.
- In-game: `/anomech` (alias `/ano`) opens the main window. Subcommands: `config`, `start`, `start solo`, `reset`, `leave`.
- Releases are cut via the manual `Build` workflow (reusable workflow in `anomek/MyDalamudPlugins`), which bumps `<Version>` in the csproj.

### Tools (`tools/`, Python)
- `parser.py <log> [territory_id]` — parses an ACT/IINACT network log into pulls and generates a baseline scenario timeline. Output still needs randomization, failure logic and bot AI added by hand. Packet drop policy lives in `filter.py`.
- `replay_export_rsv_rsf.py` — extracts server-only RSV (text) / RSF (file path) tables from an ARealmRecorded replay into a C# data file (e.g. `Scenarios/Umad/UmadReplayData.cs`), since they never arrive when running from the inn.

## Comments

Write a comment only for what the code cannot say itself — rationale, a non-obvious constraint, why the obvious approach was avoided. Delete comments that:
- restate a symbol's name in prose (`// P5 arena state` above `InitP5Arena`);
- restate literals or contract visible at a glance (`// TerritoryId 1363` beside `TerritoryId => 1363`);
- narrate what an edit changed (git records that);
- re-explain what an interface member's own doc already says;
- pile on examples for something trivial;
- cite where a value came from — log filenames, pull numbers, capture dates, sample sizes, tool invocations (`// Confirmed against 3 real pulls (Network_30208_*.log, pulls 2/3/5)`). State what the game does, not how it was measured. Provenance belongs in the session notes, not the source. The exception is a caveat that changes how the code should be trusted: mark a value that was never observed as UNVERIFIED, and keep a note that an approach was tried and found inert so nobody retries it.

When unsure, cut it — sparse and load-bearing beats thorough. Scenario AI strats (`*Ai.cs`) go stricter: no comments, intent carried entirely by descriptive method names.

## Architecture

### Frame loop
`Plugin.OnFrameworkUpdate` → `Game.Tick(dt)` → `Events.Tick(dt * EventTimeScale)`, `SimWorld.Tick(dt)` (map tick, children tick + reap, then `EnmityHud` / `PartyHud` refresh), then the active scenario's `Tick` and mechanic-result tracking. Everything runs on the Framework thread.

### Zone → Phase → Scenario
- `IZone` (`Scenarios/IZone.cs`): a raid territory — TerritoryId, origin, level/ilvl sync, waymark presets, collider removal points, zone-wide `Run`. One per family, e.g. `Scenarios/Top/TopZone.cs`.
- `IPhase`: weather + BGM bundle grouping sibling scenarios (e.g. `TopZone.P5` for Delta/Sigma/Omega); optional phase-wide `Run`.
- `IScenario`: `Name`, `Phase`, `AiStrats`, `Run(SimWorld, int? selectedAi)` (`null` = solo), optional `Tick` / `DrawSettings` / `IsFinished`.
- Scenarios are registered in the flat array in the `Game` constructor (`Core/Game/Game.cs`); the zone/phase menu tree is derived from it.
- `Game.RunScenarioInternal` is the start sequence: inn check → reset → `World.Map.TryLoad` (client-side zone load) → waymarks → `World.CreateParty` → `zone.Run` → `phase.Run` → `scenario.Run`.

Scenarios are nested by family (`Scenarios/Top/`, `Umad/`, `Uwu/`, `Ucob/`). IDs that recur across phases of a family live in `<Family>Constants.cs` (e.g. `TopConstants`), shared helpers in `<Family>Utils.cs`. A phase scenario folder is typically split into `*Scenario` (event timeline + spawns + casts), `*Ai` (party-member movement strat, `IScenarioAi<TState>`), `*State` (per-run randomization), `*SettingsWindow` + `*StateOverrides` (debug overrides). A scenario can offer several strats; `IScenarioAi.Group` buckets them under region buttons (see `Umad/P2Forsaken/Ai/`).

### Scenario timeline conventions
- **Use absolute time literals in `world.Events.Add`.** Every entry in a scenario's `Run` should be `world.Events.Add(<absolute t>, ...)` from scenario start (e.g. `55f`, `56.5f`) — not `<base> + offset` arithmetic, named time constants, or chained `Events.Add` calls inside event handlers. The whole timeline should read top-to-bottom as one list of absolute timestamps. State that flows between events lives on the scenario's `*State` object.
- Scenario-facing positions are **scenario-local** (origin = `IZone.Origin`); `SimWorld.Coordinates` converts to/from global. Don't write global coordinates in scenarios.
- `IsFinished` defaults to `world.Events.IsEmpty`; a scenario that runs its timeline on a private `EventScheduler` must override it, or mechanic-streak tracking breaks.

### Core layout (`AnoMech/Core/`)
- `SimObjects/` — in-world entities implementing `ISimObject` (`Tick`, `IsActive`, `Despawn`). **Read the header of `ISimObject.cs` before adding a type**: parents own and reap children, creation only via parent spawn APIs (`SimWorld.SpawnEnemy`, `SpawnEventObject`, `SpawnTower`, `Tether`, `CreateParty`, …), `Tick`/`Despawn` idempotent. `IsActive` (presence) is distinct from a character's `IsAlive`. Key types: `SimWorld` (root), `SimCharacter` → `SimNpc` / `SimEnemy` / `SimPartyNpc` / `SimPlayer` (wraps the real local player), `SimParty`, `SimCast`, `SimOmen`, `SimTether`, `SimStatus`, `SimTower`, `SimEventObject`.
- `Game/` — `Game` (orchestrator: scenario catalog, run/reset/leave, kill/godmode, mechanic streak), `EventScheduler`, `Movement`, `Coordinates`, `Party/` (`PartyCreator`, `PartyHud`, presets, roles), `Ai/`, `Geometry/`, `Waymarks`, `Bgm`.
- `Map/` — `ZoneSession` (client-side zone load + packet firewall, ported from Hyperborea), `MapController` (`world.Map`), map effects, director functions, arena boundary, `OpcodeUpdater`.
- `Native/` — everything that touches the game client. `Interfaces/` is the game boundary, the only part of Native that sim code (`Core/SimObjects`, `Core/Game`, `Core/Map`, `Scenarios`) may name; `Implementations/` holds the production code, with `Interop/` (native plumbing with no interface) and `Pointers/` (sig-scanned function pointers, packet structs) beneath it. Sim code reaches the game through the static `Natives` locator (`Natives.Vfx`, `Natives.Data`, …; Plugin installs the implementations, tests install fakes) and per-object proxies (`IBattleCharaProxy` on every `SimCharacter`, `IEventObjectProxy`, `IStaticVfxProxy`). Anything acting on one BattleChara is a proxy member; world-level things are facades. Implementations hold no sim logic. Windows, Multiplayer and the UserActions handlers may use `Implementations/` directly. Where Dalamud already has a service interface (`IFramework`, `IChatGui`, …) sim code calls `Plugin.<Service>` directly; `IGameData` is the exception, since Lumina rows can't be built without game files. Scenarios therefore never touch raw pointers.
- `UserActions/` — optional module that resolves the local player's own actions (gauges, combos, cast-time enablers, per-job state handlers) client-side, filling in server responses the firewall blocks. Nothing in the engine depends on it. Per-job notes in `UserActions/docs/*.md`.
- `Scenarios/DamageSolver.cs` — shared damage resolution used by scenarios.

These helpers (status writers, VFX bridges, native wrappers, HUD mirrors) do NOT belong in `SimObjects/`.

### Heavy native interop
`Core/Native/Implementations/` is `unsafe` and walks `FFXIVClientStructs` types directly (`BattleChara*`, `StatusManager`, `CastInfo`, `Timeline`, `CharacterManager`, `GroupManager.MainGroup`, `AtkArrayData`, `PacketDispatcher`, …). When you need to know what a field does, prefer local FFXIVClientStructs / Dalamud checkouts over web search.

## Non-obvious things to know before changing engine code

- **Scenarios are inn-only.** Hard-gated at `Game.RunScenarioInternal` (`ZoneSession.IsInInn()`); UI gates in `Plugin.OnCommand` / `MainWindow` are UX over the same invariant. `ZoneSession.Leave()` reloads the inn. Everything downstream assumes it.
- **While simulating, server traffic is firewalled.** Anything the server would normally send (action effects, statuses, RSV/RSF text/paths, party changes) must be synthesized client-side.
- **CharacterManager registration is unconditional for party doppels.** `PartyCreator.Populate` inserts every doppel into `CharacterManager._battleCharas` (unregistered on despawn), so `LookupBattleCharaByEntityId` resolves them and the `_PartyList` agent drives status icons/timers/targeting natively; `PartyHud` only writes `MainGroup.PartyMembers` (the addon's render path is gated on `MemberCount > 0`) and restores the real MainGroup on clear. This is safe only because of the inn invariant — `Core/Native/Implementations/EnmityHud.cs` documents the render-cache teardown crash that makes it unsafe elsewhere, and why `EnmityHud` writes `_EnemyList` arrays directly instead. A `Hook<>` on `LookupBattleCharaByEntityId` was tried and removed (crashed via Reloaded.Hooks trampoline interaction with upstream `ActorControl` detours).
- **Status countdowns are managed by us, not the engine.** Direct-slot writes via `IBattleCharaProxy.ApplyStatus` / `RemoveStatus` (implemented by `Statuses`) keep `StatusManager.Status[]` packed; sim-side status objects re-stamp remaining time each tick. Don't trust the engine's decrement.
- **Cast bars are entirely simulated.** `SimCast` writes `CastInfo` and ticks it itself; on completion it fires a synthetic `ActionEffectHandler.Receive` (mimicking the server packet) so release animation/VFX play. Bypassing `Character::StartCast` means omens aren't auto-spawned — `SimOmen` resolves `Action.Omen` paths and spawns them manually.
- **Bad VFX paths crash on the file thread.** Always validate via `Natives.Data.FileExists` before spawning VFX (`Natives.Vfx.SpawnStatic` and `SimOmen` already do; Lumina's `FileExists` throws on extensionless paths).
- **Movement needs both `SetPosition` and a timeline override.** Direct field writes only move the hitbox/nameplate; the model needs `SetPosition` (propagates to DrawObject) plus a run-loop timeline. See `Core/Game/Movement.cs`.
- **`EventTimeScale` only scales `Game.Events`.** Cast bars, animations, movement and status ticks run at real time, so the Speed buttons speed up a timeline without breaking animation timing.
