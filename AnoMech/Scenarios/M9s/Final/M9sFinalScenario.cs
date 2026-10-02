using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.M9s.M9sConstants;

namespace AnoMech.Scenarios.M9s.Final;

// M9S final stretch, from the third Vamp Stomp to the kill (or the enrage): Vamp Stomp (see
// M9sBatStomp) inside the death-wall ring, the larger Half Moon and Hardcore, Pulping Pulse, a last
// Sanguine Scratch, Insatiable Thirst bringing the square back, and Crowd Kill. Scenario time 0 is
// 500.0s into the clear in Network_30301_20260923.log (pull 10), which killed the boss at 81.0s here;
// the enrage timing is the other pull that reached it.
public sealed class M9sFinalScenario : IScenario
{
    public string Name => "Final";
    public IPhase Phase => M9sZone.Fight;
    public bool SupportsSolo => true;

    public IReadOnlyList<IScenarioAi> AiStrats => [new M9sFinalAi()];

    public void DrawSettings() => settingsWindow.Draw();
    private readonly M9sFinalSettingsWindow settingsWindow = new();

    // The kill ends the run; the enrage events left on the queue are no-ops without it.
    public bool IsFinished(SimWorld w) => bossKilled || w.Events.IsEmpty;

    private M9sFinalState state = null!;
    private SimWorld world = null!;
    private DamageSolver damage = null!;
    private M9sBatStomp stomp = null!;
    private M9sHardcore hardcore = null!;
    private M9sSanguineScratch scratch = null!;

    private SimEnemy? vamp;
    private SimEnemy? shortCleave;
    private SimEnemy? longCleave;
    private bool halfMoonIsMore;
    private bool bossKilled;
    private readonly List<SimEnemy> helpers = [];
    private readonly List<SimEnemy> pulses = [];

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        damage = new DamageSolver(world.Party);
        damage.SetStatuses(DamageType.Magic, StatusId.MagicVulnerabilityUp);
        helpers.Clear();
        pulses.Clear();
        bossKilled = false;

        state = new M9sFinalState(world.Rng, settingsWindow.Overrides);
        stomp = new M9sBatStomp(world, damage, state.Satisfied, state.Bats, SpawnHelper);
        hardcore = new M9sHardcore(world.Party, damage, state.Satisfied, SpawnHelper);
        scratch = new M9sSanguineScratch(damage, state.Satisfied, SpawnHelper);
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<M9sFinalState>)AiStrats[idx]).Run(state, world);

        world.Events.Add(0f, SpawnVamp);
        world.Events.Add(0.1f, () => world.Map.AddEffect(0x00020001, 0x10));
        world.Events.Add(0.1f, () => world.EnforceArenaBoundary(Geometry.RingArenaRadius, "Walked into the death wall"));

        world.Events.Add(6.66f, stomp.ApplyCurses);
        world.Events.Add(6.66f, stomp.SpawnBats);
        world.Events.Add(6.75f, () => vamp?.Cast(ActionId.VampStompCast, targetLocation: Vector3.Zero, castSeconds: 3.8f));
        world.Events.Add(6.75f, stomp.CastVampStomp);
        world.Events.Add(11.71f, stomp.ResolveVampStomp);
        world.Events.Add(11.89f, () => vamp?.SetPosition(new Placement(Vector3.Zero, MathF.PI)));
        world.Events.Add(11.89f, stomp.CastBatRing);
        world.Events.Add(13.99f, () => vamp?.MoveTo(M9sFinalState.BossTanked.Position, 6f, M9sFinalState.BossTanked.Rotation));

        stomp.ScheduleBatFlight();
        world.Events.Add(14.93f, () => stomp.CastBlastBeat(0));
        world.Events.Add(15.91f, () => stomp.ResolveBlastBeat(0));
        world.Events.Add(18.41f, () => stomp.CastBlastBeat(1));
        world.Events.Add(19.27f, () => stomp.DespawnBats(0));
        world.Events.Add(19.39f, () => stomp.ResolveBlastBeat(1));
        world.Events.Add(21.90f, () => stomp.CastBlastBeat(2));
        world.Events.Add(22.80f, () => stomp.DespawnBats(1));
        world.Events.Add(22.88f, () => stomp.ResolveBlastBeat(2));
        world.Events.Add(26.48f, () => stomp.DespawnBats(2));
        stomp.ScheduleCurseRing();

        world.Events.Add(22.92f, CastHalfMoon);
        world.Events.Add(27.94f, () => ResolveCleave(shortCleave, M9sHalfMoon.ShortId(state.HalfMoon, halfMoonIsMore)));
        world.Events.Add(30.92f, () => ResolveCleave(longCleave, M9sHalfMoon.LongId(state.HalfMoon, halfMoonIsMore)));

        world.Events.Add(33.05f, () => vamp?.Cast(ActionId.HardcoreCast, castSeconds: 2.7f));
        world.Events.Add(33.05f, () => hardcore.Cast(vamp?.Position ?? Vector3.Zero));
        world.Events.Add(38.01f, hardcore.Resolve);

        world.Events.Add(39.09f, CastPulpingPulses);
        world.Events.Add(43.06f, ResolvePulpingPulses);

        world.Events.Add(45.00f, () => vamp?.MoveTo(Vector3.Zero, 6f, MathF.PI));
        world.Events.Add(47.37f, () => vamp?.Cast(ActionId.SanguineScratchCast, castSeconds: 2f));
        world.Events.Add(47.37f, () => scratch.CastFirstWave(Vector3.Zero, state.ScratchFirstOffset));
        world.Events.Add(50.36f, () => scratch.ResolveWave(Vector3.Zero, state.ScratchFirstOffset, 0));
        world.Events.Add(52.78f, () => scratch.ResolveWave(Vector3.Zero, state.ScratchFirstOffset, 1));
        world.Events.Add(55.18f, () => scratch.ResolveWave(Vector3.Zero, state.ScratchFirstOffset, 2));
        world.Events.Add(57.56f, () => scratch.ResolveWave(Vector3.Zero, state.ScratchFirstOffset, 3));
        world.Events.Add(59.98f, () => scratch.ResolveWave(Vector3.Zero, state.ScratchFirstOffset, 4));

        world.Events.Add(64.70f, () => vamp?.Cast(ActionId.InsatiableThirstCast, castSeconds: 2.5f));
        world.Events.Add(70.49f, () => M9sUtils.Raidwide(world.Party, damage, ActionId.InsatiableThirst, 0.35f));
        world.Events.Add(70.72f, () => world.Map.AddEffect(0x00080004, 0x10));

        world.Events.Add(79.10f, () => vamp?.Cast(ActionId.CrowdKillCast, castSeconds: 0.2f));
        world.Events.Add(81.00f, KillBossUnlessEnrage);
        world.Events.Add(85.20f, CrowdKillIfEnrage);
        world.Events.Add(93.75f, () => { if (state.Enrage) vamp?.Cast(ActionId.FinaleFataleEnrageCast, castSeconds: 9.7f); });
        world.Events.Add(104.55f, () => { if (state.Enrage) world.Party.WipeAllPlayers("Final Finale Fatale (enrage)"); });
        world.Events.Add(106f, DespawnAll);
    }

    private void SpawnVamp()
    {
        vamp = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: BNpcBaseId.VampFatale,
            NameId: BNpcNameId.VampFatale,
            Level: 100,
            Targetable: true,
            EnemyList: EnemyListMode.Always,
            IsVisible: true,
            Placement: new Placement(Vector3.Zero, MathF.PI)));
        state.Satisfied.Attach(vamp);
    }

    private SimEnemy? SpawnHelper(Vector3 position) => SpawnHelper(new Placement(position, 0f));

    private SimEnemy? SpawnHelper(Placement placement)
    {
        var helper = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: BNpcBaseId.Helper,
            Level: 100,
            Targetable: false,
            EnemyList: EnemyListMode.Never,
            // Drawn so its action timelines play; the helper's ModelChara has no mesh.
            IsVisible: true,
            Placement: placement));
        if (helper != null) helpers.Add(helper);
        return helper;
    }

    private void CastHalfMoon()
    {
        var boss = M9sFinalState.BossTanked;
        // The boss can't walk while casting, so a MoveTo can land after the omen; pin her to the
        // cleave's origin as it goes up.
        vamp?.StopMoving();
        vamp?.SetPosition(boss);
        var more = halfMoonIsMore = state.Satisfied.IsMore;
        vamp?.Cast(M9sHalfMoon.BossCastId(state.HalfMoon, more), castSeconds: 4f);
        shortCleave = SpawnHelper(M9sHalfMoon.CleaveOrigin(boss, state.HalfMoonShortRotation, more));
        shortCleave?.Cast(M9sHalfMoon.ShortId(state.HalfMoon, more), castSeconds: 4.7f);
        longCleave = SpawnHelper(M9sHalfMoon.CleaveOrigin(boss, state.HalfMoonLongRotation, more));
        longCleave?.Cast(M9sHalfMoon.LongId(state.HalfMoon, more), castSeconds: 7.7f, omenDelay: 4.7f);
    }

    private void ResolveCleave(SimEnemy? cleave, uint actionId) =>
        state.Satisfied.AddFor(damage.Resolve(cleave, actionId, [DamageType.Lethal], [], size: MathF.PI / 2f));

    private void CastPulpingPulses()
    {
        foreach (var at in M9sFinalState.PulpingPulses)
            if (SpawnHelper(at) is { } pulse)
            {
                pulse.Cast(ActionId.PulpingPulse, castSeconds: 3.7f);
                pulses.Add(pulse);
            }
    }

    private void ResolvePulpingPulses()
    {
        foreach (var pulse in pulses)
            state.Satisfied.AddFor(damage.Resolve(pulse, ActionId.PulpingPulse, [DamageType.Lethal], []));
    }

    private void KillBossUnlessEnrage()
    {
        if (state.Enrage) return;
        vamp?.Despawn();
        vamp = null;
        bossKilled = true;
    }

    private void CrowdKillIfEnrage()
    {
        if (!state.Enrage) return;
        M9sUtils.Raidwide(world.Party, damage, ActionId.CrowdKill, 0.51f);
        state.Satisfied.Add(4);
    }

    private void DespawnAll()
    {
        vamp?.Despawn();
        foreach (var helper in helpers) helper.Despawn();
        helpers.Clear();
        pulses.Clear();
        stomp.DespawnBats();
    }
}
