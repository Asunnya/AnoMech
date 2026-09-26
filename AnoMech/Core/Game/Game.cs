using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Map;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.M9s.Aetherletting;
using AnoMech.Scenarios.M9s.Coffinmaker;
using AnoMech.Scenarios.M9s.Deathmatch;
using AnoMech.Scenarios.M9s.Final;
using AnoMech.Scenarios.M9s.Flails;
using AnoMech.Scenarios.M9s.HellInACell;
using AnoMech.Scenarios.M9s.VampStomp;
using AnoMech.Scenarios.M9s.VampStomp2;
using AnoMech.Scenarios.Top.P2PartySynergy;
using AnoMech.Scenarios.Top.P5Delta;
using AnoMech.Scenarios.Top.P5Omega;
using AnoMech.Scenarios.Top.P5Sigma;
using AnoMech.Scenarios.Top.P6WaveCannon2;
using AnoMech.Scenarios.Umad;
using AnoMech.Scenarios.Umad.P2Forsaken;
using AnoMech.Scenarios.Umad.P3BlackHole;
using AnoMech.Scenarios.Umad.P4KefkaSays;
using AnoMech.Scenarios.Ucob.P5Exaflares;
using AnoMech.Scenarios.Umad.P5Celestriad;
using AnoMech.Scenarios.Umad.P5Exaflares;
using AnoMech.Scenarios.Uwu.P1Garuda;
using AnoMech.Scenarios.Uwu.P2Ifrit;
using AnoMech.Scenarios.Uwu.UltimatePredation;
using AnoMech.Scenarios.Uwu.UltimateSuppression;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace AnoMech.Core.Game;

// High-level orchestrator: owns the World, holds the scenario catalog, drives
// the active scenario's lifecycle, and is the single entry point UI talks to.
public sealed class Game : IDisposable
{
    // EventObj for the duty Exit portal — hidden on every scenario start so the
    // teleport-out interactable doesn't sit inside the simulated arena.
    private const uint ExitObjectBaseId = 2000139;

    public EventScheduler Events { get; } = new();
    public SimWorld World { get; }
    public SimPlayer? Player => World.Party.Player;
    // Flat registry; the zone -> phase -> scenario tree is derived from it in
    // first-appearance order.
    public IReadOnlyList<IScenario> Scenarios { get; }
    public IReadOnlyList<IZone> Zones { get; }
    private readonly Dictionary<IZone, List<IPhase>> phasesByZone = new();
    private readonly Dictionary<IPhase, List<IScenario>> scenariosByPhase = new();
    public Bgm Bgm { get; } = new();

    // Fixed scenario-local player spawn (16y south of centre).
    public static readonly Vector3 PlayerSpawnLocal = new(0f, 0f, 16f);

    // Multiplier applied only to the EventScheduler's delta. Intentionally does not
    // scale enemy/party/tether/status ticks so cast bars, animations, and movement
    // run at real time — only the timeline of scheduled events stretches/compresses.
    public float EventTimeScale { get; set; } = 1f;

    // Set by Game.Kill once the post-first-death freeze timer fires. While true,
    // Tick is a no-op so scenario events, scheduler, and world all stop.
    public bool Paused { get; set; }
    public bool IsScenarioActive => activeScenario is not null;
    public bool HasScenarioMistake => lastMistakeElapsed is not null;
    public bool HasScenarioFailed => deathOccurredThisRun;
    public bool HasScenarioSucceeded => mechanicResultReported
        && !deathOccurredThisRun
        && lastMistakeElapsed is null;

    // When true, Game.Kill still posts the chat line for learning but skips every
    // gameplay side effect (HP=0, KO timeline, stun hooks, freeze timer).
    public bool GodMode { get; set; }

    // When true, a successful run (see UpdateMechanicResult) immediately starts the same
    // scenario again with the same parameters, for hands-free repetition. A real death
    // cancels it (set false directly in Kill) rather than restarting past a failure the
    // freeze/overlay exists to let the user actually see.
    public bool AutoRestart { get; set; }
    private RunScenarioParams? lastRun;

    // Consecutive successful completions of whatever scenario is currently active. Reset by
    // Kill on any real (non-godmode) death and by RunScenarioInternal when a different
    // scenario starts. Incremented automatically once IScenario.IsFinished reports true and
    // stays true for MechanicResultSettleSeconds (see UpdateMechanicResult): no per-scenario
    // reporting needed. In-memory only: does not survive a plugin reload.
    public int MechanicStreak { get; private set; }

    // How long IsFinished has to stay true before it counts as "reached the end cleanly".
    // Covers a death whose failure check trails a few frames behind the scenario's own
    // last scheduled action (rather than firing in the exact same frame, which the Tick
    // call order below already handles on its own).
    private const float MechanicResultSettleSeconds = 1f;
    private float? scenarioFinishedElapsed;
    private bool mechanicResultReported;

    // Bardam's Mettle's Success/Failure marks (Checkmark/X).
    private const string MechanicSuccessVfx = "vfx/monster/gimmick2/eff/e3d2_b2_g04t0x.avfx";
    private const string MechanicFailureVfx = "vfx/monster/gimmick2/eff/e3d2_b2_g05t0x.avfx";

    // When the last mistake was marked, null while the run is still clean. Godmode deaths return
    // before deathOccurredThisRun is set, so this is the only mistake signal spanning both modes.
    private const float MistakeMarkCooldownSeconds = 1f;
    private float? lastMistakeElapsed;

    // Set by Kill on any real death, scoped to the current run (cleared by ResetInternal).
    // IsFinished can go true on a queue that Kill's own freeze-timer event never touches
    // (e.g. a scenario with a private EventScheduler immune to EventTimeScale): there's no
    // structural guarantee that queue and the freeze timer interleave correctly the way two
    // entries on the same Events queue would, so this flag is the actual source of truth for
    // "did this run fail", independent of any queue or timing race.
    private bool deathOccurredThisRun;

    private IScenario? activeScenario;
    private float scenarioElapsed;
    private bool firstDeathScheduled;
    private bool firstFreezeScheduled;
    private readonly OpcodeUpdater opcodeUpdater;

    public Game()
    {
        World = new SimWorld(Events);
        opcodeUpdater = new OpcodeUpdater();
        Scenarios = new IScenario[]
        {
            new UmadP2ForsakenScenario(),
            new UmadP3BlackHoleScenario(),
            new UmadP4KefkaSaysScenario(),
            new UmadP5ExaflaresScenario(),
            new UmadP5CelestriadScenario(),
            new UmadP5ForsakenNull(),
            new TopP2PartySynergyScenario(),
            new TopP5DeltaScenario(),
            new TopP5SigmaScenario(),
            new TopP5OmegaScenario(),
            new TopP6WaveCannon2Scenario(),
            new UwuP1GarudaScenario(),
            new UwuP2IfritScenario(),
            new UltimatePredationScenario(),
            new UltimateSuppressionScenario(),
            new UcobP5ExaflaresScenario(),
            new M9sVampStompScenario(),
            new M9sCoffinmakerScenario(),
            new M9sAetherlettingScenario(),
            new M9sVampStomp2Scenario(),
            new M9sFlailsScenario(),
            new M9sHellInACellScenario(),
            new M9sDeathmatchScenario(),
            new M9sFinalScenario()
        };

        // Derive the zone tree from the flat registry (first-appearance order).
        var zoneOrder = new List<IZone>();
        foreach (var scenario in Scenarios)
        {
            var phase = scenario.Phase;
            var zone = phase.Zone;
            if (!phasesByZone.TryGetValue(zone, out var phases))
            {
                phases = new List<IPhase>();
                phasesByZone[zone] = phases;
                zoneOrder.Add(zone);
            }
            if (!phases.Contains(phase)) phases.Add(phase);
            if (!scenariosByPhase.TryGetValue(phase, out var phaseScenarios))
            {
                phaseScenarios = new List<IScenario>();
                scenariosByPhase[phase] = phaseScenarios;
            }
            phaseScenarios.Add(scenario);
        }
        Zones = zoneOrder;
    }

    // Derived zone-tree accessors, in registry order.
    public IReadOnlyList<IPhase> PhasesOf(IZone zone) => phasesByZone[zone];
    public IReadOnlyList<IScenario> ScenariosOf(IPhase phase) => scenariosByPhase[phase];

    // selectedAi: index into the scenario's AiStrats of the strat to run, or null for
    // solo (no doppels, no AI). Defaults to 0 = run the first strat with a full party.
    // selectedWaymark: index into the scenario's WaymarkPresets; ignored when it has none.
    public void RunScenario(IScenario scenario, PartyRole? roleOverride = null, int? selectedAi = 0, int selectedWaymark = 0)
        => RunScenario(new RunScenarioParams(scenario, roleOverride, selectedAi, selectedWaymark));

    private void RunScenario(RunScenarioParams p)
    {
        lastRun = p;
        Plugin.Framework.Run(() => RunScenarioInternal(p.Scenario, p.RoleOverride, p.SelectedAi, p.SelectedWaymark));
    }

    // The selected preset, or [0] as the default.
    private static IReadOnlyList<Waymark> ResolveWaymarks(IZone zone, int selectedWaymark)
    {
        var presets = zone.WaymarkPresets;
        if (selectedWaymark >= 0 && selectedWaymark < presets.Count)
            return presets[selectedWaymark].Markers;
        return presets[0].Markers;
    }

    private void RunScenarioInternal(IScenario scenario, PartyRole? roleOverride, int? selectedAi, int selectedWaymark)
    {
        var solo = selectedAi is null;
        var phase = scenario.Phase;
        var zone = phase.Zone;
        // Hard gate: scenarios are only ever run from an inn. Everything
        // downstream (CharacterManager registration, zone load, doppel spawn)
        // assumes that invariant.
        if (!ZoneSession.IsInInn())
        {
            Plugin.Log.Warning("Game: scenarios can only run from an inn; aborting.");
            return;
        }

        // Captured before ResetInternal clears activeScenario, so restarting the same
        // scenario (the normal way to extend a streak) doesn't look like a switch.
        var previousScenario = activeScenario;
        ResetInternal();

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
        {
            Plugin.Log.Warning("Game: no local player; aborting scenario start");
            return;
        }

        // Captured before TryLoad: false only on the first start from the inn (a true
        // zone entry), true for any restart/switch within the already-loaded zone.
        var freshLoad = !World.Map.IsZoneLoaded;

        // Snapshot the player's pristine job gauge once per session, before any action mutates
        // it, so Leave can restore it. Only on a true zone entry — a restart must keep the
        // original snapshot, not re-capture the already-simulated gauge.
        if (freshLoad) Plugin.UserActions.OnSessionStart();

        World.HideObject(ExitObjectBaseId);
        World.Map.TryLoad(
            new TargetInstance(zone.TerritoryId, zone.Origin, zone.Origin + PlayerSpawnLocal, phase.Weather),
            zone.Level, zone.ItemLevel);
        World.ScenarioOrigin = zone.Origin;
        World.Map.ArmColliderDrops(zone.ColliderRemovalPoints.Select(World.Coordinates.ToGlobal));
        World.PlaceWaymarks(ResolveWaymarks(zone, selectedWaymark));
        World.CreateParty(player.ClassJob.RowId, roleOverride, solo);
        // zone.Run creates the SimArenaBoundary the out-of-arena check below reads.
        zone.Run(World);
        phase.Run(World);
        scenario.Run(World, selectedAi);
        // Entering the zone always starts at spawn; a restart only recenters the player
        // if they're standing outside the arena ring (otherwise they keep their position).
        if (freshLoad)
            TeleportPlayerToSpawn();
        else
            TeleportPlayerToSpawnIfOutsideArena();
        if (previousScenario != scenario)
            MechanicStreak = 0;
        Plugin.UserActions.OnScenarioStart();
        activeScenario = scenario;
        scenarioElapsed = 0f;

        // Reconcile BGM to the new scenario. Bgm.Play is idempotent, so switching
        // between same-track scenarios (e.g. the P5 phases) keeps playing without
        // restarting the song; a different track swaps; suppressed/no-track reverts.
        if (Plugin.Config.SuppressBgm || phase.Bgm == 0)
            Bgm.Reset();
        else
            Bgm.Play(phase.Bgm);

        Plugin.ChatGui.Print(new XivChatEntry
        {
            Type = XivChatType.SystemMessage,
            Message = new SeStringBuilder().AddText($"[AnoMech] Starting: {FullName(scenario)}{(solo ? " (Solo)" : "")}").Build(),
        });
    }

    public void Tick(float deltaSeconds)
    {
        if (Paused) return;
        Events.Tick(deltaSeconds * EventTimeScale);
        World.Tick(deltaSeconds);
        if (activeScenario != null)
        {
            scenarioElapsed += deltaSeconds;
            activeScenario.Tick(deltaSeconds, scenarioElapsed);
            UpdateMechanicResult(deltaSeconds);
        }
    }

    // Infers a clean run from IScenario.IsFinished going true and staying true, rather than
    // needing each scenario to report its own completion. deathOccurredThisRun (set by Kill,
    // independent of whichever queue IsFinished watches) is the actual gate against a failed
    // run being mistaken for a clean one: a queue running dry is not by itself proof nothing
    // died, since Kill's own freeze-timer event lives on Events specifically and a scenario's
    // IsFinished override may watch a different queue entirely.
    private void UpdateMechanicResult(float deltaSeconds)
    {
        if (mechanicResultReported) return;
        if (activeScenario is null || !activeScenario.IsFinished(World))
        {
            scenarioFinishedElapsed = null;
            return;
        }
        scenarioFinishedElapsed = (scenarioFinishedElapsed ?? 0f) + deltaSeconds;
        if (scenarioFinishedElapsed < MechanicResultSettleSeconds) return;
        mechanicResultReported = true;
        if (deathOccurredThisRun) return;
        if (lastMistakeElapsed is null)
        {
            MechanicStreak++;
            if (Plugin.Config.EnableMechanicResultMarks)
                World.Party.Player?.AddVfx(MechanicSuccessVfx, persistent: false);
        }
        if (AutoRestart && lastRun is { } p)
            RunScenario(p);
    }

    // Godmode preview: how long a swallowed-death HP-bar drop stays down before healing back.
    private const float GodmodeHealSeconds = 1.2f;

    // Single entry point for "this character died". Always posts the cause
    // to chat and, on the first call of a run, fires the on-screen overlay
    // — both happen even in godmode so the user can learn what would have
    // killed them. Gameplay side effects (OnKilled, which flips Dead, plus
    // the 5s freeze) only run outside godmode; the freeze fires once per run
    // on the first non-godmode death.
    //
    // Returns true only when the member actually went down (OnKilled ran):
    // false when it was already dead, invulnerable (GiveInvuln), or godmode
    // swallowed it. Callers that run extra on-death logic should gate on this
    // so an invuln'd/godmode'd "death" doesn't trigger gameplay consequences.
    public bool Kill(ISimPartyMember target, string cause)
    {
        if (target == null) return false;
        if (target.Dead) return false;
        if (target is SimCharacter sc && sc.HasStatus(SimParty.InvulnStatusId))
        {
            Plugin.Log.Info($"[Invuln] {DescribeName(target)} survived: {cause}");
            return false;
        }

        PrintDeath(target, cause);
        if (!firstDeathScheduled)
        {
            firstDeathScheduled = true;
            ShowFirstDeathOverlay(target, cause);
        }
        // Above the godmode return so every swallowed mistake marks.
        // Prevent Mark stacking by enforcing a cooldown.
        if (lastMistakeElapsed is not { } last || scenarioElapsed - last > MistakeMarkCooldownSeconds)
        {
            lastMistakeElapsed = scenarioElapsed;
            if (Plugin.Config.EnableMechanicResultMarks)
                World.Party.Player?.AddVfx(MechanicFailureVfx, persistent: false);
        }

        if (GodMode)
        {
            // Godmode swallows the death but still previews it: drop the player's bar and heal it
            // back a beat later. Done here rather than OnKilled (which godmode skips) so every
            // scenario gets it. Rides Game.Events; the ~1.2s restore is cosmetic, so scaling is moot.
            if (target is SimPlayer player)
            {
                player.DropHpBar();
                Events.Add(GodmodeHealSeconds, player.RestoreHpBar);
            }
            return false;
        }
        target.OnKilled();
        MechanicStreak = 0;
        deathOccurredThisRun = true;
        AutoRestart = false;
        if (!firstFreezeScheduled)
        {
            firstFreezeScheduled = true;
#if DEBUG
            AnoMech.Windows.DamageDebugWindow.Instance?.Freeze();
#endif
            Events.Add(5f, () => Paused = true);
        }
        return true;
    }

    private static void PrintDeath(ISimPartyMember target, string cause)
    {
        Plugin.ChatGui.Print(new XivChatEntry
        {
            Type = XivChatType.SystemMessage,
            Message = new SeStringBuilder().AddText($"[AnoMech] {DescribeName(target)} died: {cause}").Build(),
        });
    }

    private static string DescribeName(ISimPartyMember target) => target switch
    {
        SimPlayer => "You",
        SimPartyNpc pm => pm.DisplayName,
        _ => "Character",
    };

    private static unsafe void ShowFirstDeathOverlay(ISimPartyMember target, string cause)
    {
        var ui = UIModule.Instance();
        if (ui == null) return;
        ui->ShowErrorText($"{DescribeName(target)} died: {cause}", true);
    }

    public void Reset() => Plugin.Framework.Run(() =>
    {
        if (activeScenario is not null)
            TeleportPlayerToSpawnIfOutsideArena();
        ResetInternal();
        Bgm.Reset();
    });

    // Pull the player back to the scenario's spawn point only if they're standing
    // outside the arena ring (e.g. knocked out of bounds, or wandered off). No-op
    // when the scenario enforces no boundary. Reads the live game-object position:
    // at scenario start the SimPlayer was just created and hasn't ticked, so its
    // cached Position is still zero. At reset this must run before ResetInternal
    // clears Party / ScenarioOrigin.
    private void TeleportPlayerToSpawnIfOutsideArena()
    {
        var lp = Plugin.ObjectTable.LocalPlayer;
        if (lp == null) return;
        if (!World.IsOutsideArena(World.Coordinates.ToLocal(lp.Position))) return;
        TeleportPlayerToSpawn();
    }

    // ScenarioOrigin must already be set (SetPosition resolves local -> world through it).
    private void TeleportPlayerToSpawn() => Player?.SetPosition(PlayerSpawnLocal);

    // Menu label, e.g. "P5 Delta".
    public static string DisplayName(IScenario scenario)
    {
        var phase = scenario.Phase;
        return string.IsNullOrEmpty(phase.Name) ? scenario.Name : $"{phase.Name} {scenario.Name}";
    }

    public static string FullName(IScenario scenario)
        => $"{scenario.Phase.Zone.Name} — {DisplayName(scenario)}";

    // Leave returns to the inn. Only meaningful when IsInInstance is true.
    // Resets the encounter first, then reverts the zone — Reset stays in-zone.
    public void Leave()
    {
        Plugin.Framework.Run(() =>
        {
            ResetInternal();
            Plugin.UserActions.OnSessionEnd();   // restore the job gauge captured at session start
            Bgm.Reset();
            World.Map.Unload();
        });
    }

    private void ResetInternal()
    {
        activeScenario = null;
        scenarioElapsed = 0f;
        Events.Clear();
        World.Despawn();
        // BGM is owned by the callers: a scenario start reconciles it to the new
        // track (keeping it playing when unchanged); Reset/Leave stop it. Resetting
        // here would force a same-track restart on every scenario switch.

        Paused = false;
        firstDeathScheduled = false;
        firstFreezeScheduled = false;
        scenarioFinishedElapsed = null;
        mechanicResultReported = false;
        deathOccurredThisRun = false;
        lastMistakeElapsed = null;
#if DEBUG
        AnoMech.Windows.DamageDebugWindow.Instance?.ResetFreeze();
#endif
        // Input-lock flags are owned by SimPlayer (reconciled each tick, cleared on
        // its Despawn during World.Reset above) — nothing to clear here.
    }

    // Plugin.Dispose is invoked on the framework thread during unload — run
    // teardown synchronously here. The previous Framework.Run wrapper queued
    // the lambda for the *next* tick, which never fired during shutdown and
    // leaked all six LocalPlayerInputHooks hooks.
    public void Dispose()
    {
        activeScenario = null;
        Events.Clear();
        Plugin.UserActions.OnSessionEnd();   // restore the gauge if the plugin unloads mid-session (no-op otherwise)
        Bgm.Dispose();
        World.Dispose();
        opcodeUpdater.Dispose();
    }
}

// A single RunScenario call's arguments, bundled so Game can replay the exact same run (see
// AutoRestart) without tracking each argument as its own field.
public sealed record RunScenarioParams(IScenario Scenario, PartyRole? RoleOverride, int? SelectedAi, int SelectedWaymark);
