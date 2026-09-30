using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Geometry;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.M9s.M9sConstants;

namespace AnoMech.Scenarios.M9s.Coffinmaker;

// M9S saws phase; timings from Network_30301_20260923.log pull 10 (t0 = 54s).
public sealed class M9sCoffinmakerScenario : IScenario
{
    public string Name => "Coffinmaker";
    public IPhase Phase => M9sZone.Fight;
    public bool SupportsSolo => true;

    public IReadOnlyList<IScenarioAi> AiStrats => [new M9sCoffinmakerAi()];

    public void DrawSettings() => settingsWindow.Draw();
    private readonly M9sCoffinmakerSettingsWindow settingsWindow = new();

    private const float WallCheckStep = 0.1f;
    private const float CorridorHalfWidth = 10f;
    private const float CorridorSouthEdge = 20f;

    private M9sCoffinmakerState state = null!;
    private SimWorld world = null!;
    private DamageSolver damage = null!;

    private SimEnemy? vamp;
    private SimEnemy? saw;
    private SimEnemy? deadWake;
    private SimEnemy? shortCleave;
    private SimEnemy? longCleave;
    private readonly List<SimEnemy> firstWave = [];
    private readonly List<SimEnemy> secondWave = [];
    private readonly List<SimEnemy> helpers = [];
    private bool corridorActive;
    private bool sawAlive;

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        damage = new DamageSolver(world.Party);
        helpers.Clear();
        corridorActive = false;
        sawAlive = true;

        state = new M9sCoffinmakerState(settingsWindow.Overrides);
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<M9sCoffinmakerState>)AiStrats[idx]).Run(state, world);

        world.Events.Add(0f, SpawnVamp);
        world.Events.Add(0f, () => world.EnforceArenaBoundary(new SquareArena(Geometry.ArenaHalfWidth), "Walked off the arena"));
        world.Events.Add(0.82f, () => vamp?.Cast(ActionId.SadisticScreechCast, castSeconds: 4.7f));
        world.Events.Add(0.82f, () => MapEffect(0x00020001, 0x0F));
        world.Events.Add(1.11f, SpawnSaw);
        world.Events.Add(6.63f, () => MapEffect(0x00020001, 0x00, 0x09, 0x0A, 0x0B, 0x0C));
        world.Events.Add(6.63f, () => MapEffect(0x00080004, 0x0F));
        world.Events.Add(6.63f, () => corridorActive = true);
        world.Events.Add(6.63f, () => world.Map.DirectorUpdate(M9sUtils.CorridorDirectorCommand, 0x07));
        world.Events.Add(6.32f, () => M9sUtils.Raidwide(world.Party, damage, ActionId.SadisticScreech, 0.40f));
        world.Events.Add(6.63f, () => vamp?.SetTargetable(false));
        ScheduleWallChecks();

        world.Events.Add(8.86f, () => MapEffect(0x00200010, 0x09, 0x0A, 0x0B, 0x0C));
        world.Events.Add(8.95f, () => CastDeadWake(-20f));
        world.Events.Add(13.58f, () => AdvanceSaw(-15f));
        world.Events.Add(13.58f, () => vamp?.MoveTo(state.Cycles[0].Boss.Position, 10f, state.Cycles[0].Boss.Rotation));
        world.Events.Add(13.94f, ResolveDeadWake);
        world.Events.Add(14.41f, () => MapEffect(0x00020001, 0x11));

        world.Events.Add(16.19f, () => CastCleavesAndFirstWave(0));
        world.Events.Add(19.23f, () => CastSecondWave(0));
        world.Events.Add(21.15f, () => ResolveFirstHalf(0));
        world.Events.Add(24.19f, () => ResolveSecondHalf(0));

        world.Events.Add(26.29f, () => MapEffect(0x00800040, 0x09, 0x0A, 0x0B, 0x0C));
        world.Events.Add(26.37f, () => CastDeadWake(-10f));
        world.Events.Add(31.00f, () => AdvanceSaw(-5f));
        world.Events.Add(31.00f, () => vamp?.MoveTo(state.Cycles[1].Boss.Position, 10f, state.Cycles[1].Boss.Rotation));
        world.Events.Add(31.36f, ResolveDeadWake);
        world.Events.Add(31.86f, () => MapEffect(0x00200010, 0x11));

        world.Events.Add(33.60f, () => CastCleavesAndFirstWave(1));
        world.Events.Add(36.64f, () => CastSecondWave(1));
        world.Events.Add(38.56f, () => ResolveFirstHalf(1));
        world.Events.Add(41.60f, () => ResolveSecondHalf(1));

        world.Events.Add(43.68f, () => MapEffect(0x02000100, 0x09, 0x0A, 0x0B, 0x0C));
        world.Events.Add(43.77f, () => CastDeadWake(0f));
        world.Events.Add(48.40f, () => AdvanceSaw(5f));
        world.Events.Add(48.40f, () => vamp?.MoveTo(state.Cycles[2].Boss.Position, 10f, state.Cycles[2].Boss.Rotation));
        world.Events.Add(48.76f, ResolveDeadWake);
        world.Events.Add(49.22f, () => MapEffect(0x00800040, 0x11));

        world.Events.Add(51.00f, () => CastCleavesAndFirstWave(2));
        world.Events.Add(54.04f, () => CastSecondWave(2));
        world.Events.Add(55.96f, () => ResolveFirstHalf(2));
        world.Events.Add(59.00f, () => ResolveSecondHalf(2));

        world.Events.Add(61.19f, () => CastCleavesAndFirstWave(3));
        world.Events.Add(64.23f, () => CastSecondWave(3));
        world.Events.Add(66.15f, () => ResolveFirstHalf(3));
        world.Events.Add(69.19f, () => ResolveSecondHalf(3));

        world.Events.Add(60.00f, () => KillSawIf(SawKill.Fast));
        world.Events.Add(62.30f, () => KillSawIf(SawKill.Average));
        world.Events.Add(70.80f, () => KillSawIf(SawKill.Slow));
        world.Events.Add(78.37f, () => vamp?.Cast(ActionId.SadisticScreechCast, castSeconds: 4.7f));
        world.Events.Add(83.87f, () => M9sUtils.Raidwide(world.Party, damage, ActionId.SadisticScreech, 0.40f));
        world.Events.Add(84.21f, () => MapEffect(0x00080004, 0x00, 0x11));
        world.Events.Add(84.21f, () => corridorActive = false);
        world.Events.Add(84.21f, () => world.Map.DirectorUpdate(M9sUtils.CorridorDirectorCommand, 0x01));
        world.Events.Add(86f, DespawnAll);
    }

    // The wall saws put away with 0x80000004, the corridor with 0x00080004.
    private void MapEffect(uint flags, params byte[] indices)
    {
        foreach (var index in indices)
        {
            world.Map.AddEffect(flags, index);
            world.ResetMapEffectOnDespawn(index, index is >= 0x09 and <= 0x0C ? 0x80000004u : 0x00080004u);
        }
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

    private void SpawnSaw()
    {
        saw = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: BNpcBaseId.Coffinmaker,
            NameId: BNpcNameId.Coffinmaker,
            Level: 100,
            Targetable: true,
            EnemyList: EnemyListMode.Always,
            IsVisible: true,
            Placement: new Placement(new Vector3(0f, 0f, -25f), 0f)));
    }

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

    private void CastDeadWake(float northEdge)
    {
        saw?.Cast(ActionId.DeadWakeCast, castSeconds: 4.2f);
        deadWake = SpawnHelper(new Placement(new Vector3(0f, 0f, northEdge), 0f));
        deadWake?.Cast(ActionId.DeadWake, castSeconds: 4.7f);
    }

    private void ResolveDeadWake() => state.Satisfied.AddFor(damage.Resolve(deadWake, ActionId.DeadWake, [DamageType.Lethal], []));

    // The saw's death retracts the wall saws and frees Vamp to be targeted.
    private void KillSawIf(SawKill when)
    {
        if (state.SawKill != when) return;
        sawAlive = false;
        saw?.Despawn();
        MapEffect(0x80000004, 0x09, 0x0A, 0x0B, 0x0C);
        vamp?.SetTargetable(true);
    }

    // The saw lurches 10y south in about 0.6s once each Dead Wake lands.
    private void AdvanceSaw(float z) => saw?.MoveTo(new Vector3(0f, 0f, z), 16f, 0f);

    private void CastCleavesAndFirstWave(int index)
    {
        var cycle = state.Cycles[index];
        var more = state.CycleIsMore[index] = state.Satisfied.IsMore;
        // The run to the wall can still be finishing; pin the boss to the cleave's origin as it goes up.
        vamp?.StopMoving();
        vamp?.SetPosition(cycle.Boss);
        vamp?.Cast(M9sHalfMoon.BossCastId(cycle.Order, more), castSeconds: 4f);
        shortCleave = SpawnHelper(M9sHalfMoon.CleaveOrigin(cycle.Boss, cycle.ShortRotation, more));
        shortCleave?.Cast(M9sHalfMoon.ShortId(cycle.Order, more), castSeconds: 4.7f);
        longCleave = SpawnHelper(M9sHalfMoon.CleaveOrigin(cycle.Boss, cycle.LongRotation, more));
        // The second cleave's telegraph appears as the first resolves, as it does in game.
        longCleave?.Cast(M9sHalfMoon.LongId(cycle.Order, more), castSeconds: 7.7f, omenDelay: 4.7f);

        if (!sawAlive) return;
        saw?.Cast(ActionId.CoffinfillerCast, castSeconds: 4.7f);
        CastWave(cycle, cycle.FirstWave, firstWave);
    }

    private void CastSecondWave(int index)
    {
        if (sawAlive) CastWave(state.Cycles[index], state.Cycles[index].SecondWave, secondWave);
    }

    // Director 0x80000026 lights each firing wall saw; the omen is drawn directly.
    private void CastWave(SawCycle cycle, IReadOnlyList<float> columns, List<SimEnemy> wave)
    {
        wave.Clear();
        foreach (var x in columns)
        {
            world.Map.DirectorUpdate(SawGlowCommand, SawIndex(x), SawGlowLength(cycle.FillerActionId), 0x01);
            var origin = new Vector3(x, 0f, cycle.FillerStartZ);
            if (SpawnHelper(new Placement(origin, 0f)) is not { } filler) continue;
            filler.Cast(cycle.FillerActionId, castSeconds: 4.7f);
            world.SpawnActionOmen(cycle.FillerActionId, origin, 0f, 4.7f);
            wave.Add(filler);
        }
    }

    private const uint SawGlowCommand = 0x80000026;

    private static uint SawIndex(float x) => 0x09u + (uint)MathF.Round((x + 7.5f) / 5f);

    private static uint SawGlowLength(uint fillerActionId) => fillerActionId switch
    {
        ActionId.CoffinfillerLong => 0x0A,
        ActionId.CoffinfillerMedium => 0x0B,
        _ => 0x0C,
    };

    private void ResolveFirstHalf(int index)
    {
        var cycle = state.Cycles[index];
        ResolveCleaveAndWave(shortCleave, M9sHalfMoon.ShortId(cycle.Order, state.CycleIsMore[index]), cycle, firstWave);
    }

    private void ResolveSecondHalf(int index)
    {
        var cycle = state.Cycles[index];
        ResolveCleaveAndWave(longCleave, M9sHalfMoon.LongId(cycle.Order, state.CycleIsMore[index]), cycle, secondWave);
    }

    private void ResolveCleaveAndWave(SimEnemy? cleave, uint cleaveId, SawCycle cycle, List<SimEnemy> wave)
    {
        state.Satisfied.AddFor(damage.Resolve(cleave, cleaveId, [DamageType.Lethal], [], size: MathF.PI / 2f));
        foreach (var filler in wave)
            state.Satisfied.AddFor(damage.Resolve(filler, cycle.FillerActionId, [DamageType.Lethal], []));
        wave.Clear();
    }

    private void ScheduleWallChecks()
    {
        for (var t = 6.7f; t <= 84.2f; t += WallCheckStep)
        {
            var northEdge = M9sCoffinmakerState.NorthEdgeAt(t);
            world.Events.Add(t, () => KillOutsideCorridor(northEdge));
        }
    }

    private void KillOutsideCorridor(float northEdge)
    {
        if (!corridorActive) return;
        for (var slot = 0; slot < 8; slot++)
        {
            if (world.Party.Get(slot) is not { } member || !member.IsAlive()) continue;
            var p = member.Position;
            if (MathF.Abs(p.X) > CorridorHalfWidth || p.Z > CorridorSouthEdge)
                member.Die("Walked into the saw wall");
            else if (p.Z < northEdge)
                member.Die("Walked into Dead Wake's fire");
        }
    }

    private void DespawnAll()
    {
        vamp?.Despawn();
        saw?.Despawn();
        foreach (var helper in helpers) helper.Despawn();
        helpers.Clear();
    }
}
