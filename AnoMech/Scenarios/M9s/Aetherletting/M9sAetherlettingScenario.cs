using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.M9s.M9sConstants;

namespace AnoMech.Scenarios.M9s.Aetherletting;

// M9S Aetherletting, from after the arena comes back to just before the second Hardcore: Crowd
// Kill, Finale Fatale and the death-wall ring, Pulping Pulse, then Aetherletting's rotating cones,
// the puddle spreads and the crosses they leave behind. Scenario time 0 is 140.0s into the clear in
// Network_30301_20260923.log (pull 10); effect times are that pull's action-effect lines.
public sealed class M9sAetherlettingScenario : IScenario
{
    public string Name => "Aetherletting";
    public IPhase Phase => M9sZone.Fight;
    public bool SupportsSolo => true;

    public IReadOnlyList<IScenarioAi> AiStrats => [new M9sAetherlettingAi()];

    public void DrawSettings() => settingsWindow.Draw();
    private readonly M9sAetherlettingSettingsWindow settingsWindow = new();

    private const float CrossDiagonal = MathF.PI / 4f;

    private M9sAetherlettingState state = null!;
    private SimWorld world = null!;
    private DamageSolver damage = null!;

    private SimEnemy? vamp;
    private readonly List<SimEnemy> helpers = [];
    private readonly List<SimEnemy> pulses = [];
    private readonly SimEnemy?[][] cones = new SimEnemy?[4][];
    private readonly SimEnemy?[][] crosses = new SimEnemy?[4][];

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        damage = new DamageSolver(world.Party);
        damage.SetStatuses(DamageType.Magic, StatusId.MagicVulnerabilityUp);
        helpers.Clear();
        pulses.Clear();

        state = new M9sAetherlettingState(settingsWindow.Overrides);
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<M9sAetherlettingState>)AiStrats[idx]).Run(state, world);

        world.Events.Add(0f, SpawnVamp);
        world.Events.Add(0f, () => world.EnforceSquareArenaBoundary(Geometry.ArenaHalfWidth, "Walked off the arena"));
        world.Events.Add(6.75f, () => vamp?.Cast(ActionId.CrowdKillCast, castSeconds: 0.2f));
        world.Events.Add(12.86f, () => M9sUtils.Raidwide(world.Party, damage, ActionId.CrowdKill, 0.51f));
        world.Events.Add(12.86f, () => state.Satisfied.Add(1));
        world.Events.Add(13.13f, () => state.Satisfied.Add(1));
        world.Events.Add(13.44f, () => state.Satisfied.Add(1));
        world.Events.Add(13.75f, () => state.Satisfied.Add(1));
        world.Events.Add(25.51f, () => vamp?.Cast(ActionId.FinaleFataleCast, castSeconds: 4.7f));
        world.Events.Add(31.51f, () => M9sUtils.Raidwide(world.Party, damage, ActionId.FinaleFatale, 0.30f));
        world.Events.Add(31.58f, () => world.Map.AddEffect(0x00020001, 0x10));
        world.Events.Add(31.58f, () => world.EnforceArenaBoundary(Geometry.RingArenaRadius, "Walked into the death wall"));

        world.Events.Add(32.52f, CastPulpingPulses);
        world.Events.Add(36.49f, ResolvePulpingPulses);

        world.Events.Add(37.64f, () => vamp?.Cast(ActionId.AetherlettingCast, castSeconds: 12f));
        world.Events.Add(41.66f, () => CastCones(0));
        world.Events.Add(43.67f, () => CastCones(1));
        world.Events.Add(45.68f, () => CastCones(2));
        world.Events.Add(47.68f, () => CastCones(3));
        world.Events.Add(47.68f, () => MarkSpreads(0));
        world.Events.Add(49.68f, () => MarkSpreads(1));
        world.Events.Add(50.68f, () => ResolveCones(0));
        world.Events.Add(51.68f, () => MarkSpreads(2));
        world.Events.Add(52.68f, () => ResolveCones(1));
        world.Events.Add(52.68f, () => ResolveSpreads(0));
        world.Events.Add(53.15f, () => CastCrosses(0));
        world.Events.Add(53.68f, () => MarkSpreads(3));
        world.Events.Add(54.68f, () => ResolveCones(2));
        world.Events.Add(54.68f, () => ResolveSpreads(1));
        world.Events.Add(55.14f, () => CastCrosses(1));
        world.Events.Add(56.68f, () => ResolveCones(3));
        world.Events.Add(56.68f, () => ResolveSpreads(2));
        world.Events.Add(57.14f, () => CastCrosses(2));
        world.Events.Add(58.68f, () => ResolveSpreads(3));
        world.Events.Add(59.14f, () => CastCrosses(3));

        world.Events.Add(67.16f, () => ResolveCrosses(0));
        world.Events.Add(69.16f, () => ResolveCrosses(1));
        world.Events.Add(71.17f, () => ResolveCrosses(2));
        world.Events.Add(73.13f, () => ResolveCrosses(3));
        world.Events.Add(75f, DespawnAll);
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

    private void CastPulpingPulses()
    {
        foreach (var at in state.PulpingPulses)
            if (SpawnHelper(new Placement(at, 0f)) is { } pulse)
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

    private void CastCones(int pair)
    {
        var bearing = state.ConeBearing(pair);
        cones[pair] =
        [
            SpawnHelper(new Placement(Vector3.Zero, M9sAetherlettingState.RotationFacing(bearing))),
            SpawnHelper(new Placement(Vector3.Zero, M9sAetherlettingState.RotationFacing(bearing + 180f))),
        ];
        foreach (var cone in cones[pair])
            cone?.Cast(ActionId.AetherlettingCone, castSeconds: 8.7f);
    }

    private void ResolveCones(int pair)
    {
        foreach (var cone in cones[pair])
            state.Satisfied.AddFor(damage.Resolve(cone, ActionId.AetherlettingCone, [DamageType.Lethal], [], size: MathF.PI / 8f));
    }

    private void MarkSpreads(int pair)
    {
        foreach (var role in state.SpreadPairs[pair])
        {
            if (world.Party.Get(role) is not { } target || !target.IsAlive()) continue;
            target.AttachLockonVfx(LockonId.Aetherletting, persistent: false);
            SpawnHelper(new Placement(Vector3.Zero, 0f))?.Cast(ActionId.AetherlettingSpread, castSeconds: 4.7f, targetId: target.GameObjectId);
        }
    }

    // Each spread leaves its puddle where its target stands when it lands.
    private void ResolveSpreads(int pair)
    {
        foreach (var role in state.SpreadPairs[pair])
        {
            if (world.Party.Get(role) is not { } target || !target.IsAlive()) continue;
            state.Puddles[(int)role] = target.Position with { Y = 0f };
            var hit = damage.Resolve(target, ActionId.AetherlettingSpread, [DamageType.Magic], [(StatusId.MagicVulnerabilityUp, 1.96f)]);
            state.Satisfied.AddFor(hit, except: target);
        }
    }

    private void CastCrosses(int pair)
    {
        var roles = state.SpreadPairs[pair];
        crosses[pair] = new SimEnemy?[roles.Length];
        for (var i = 0; i < roles.Length; i++)
        {
            if (state.Puddles[(int)roles[i]] is not { } at) continue;
            var rotation = state.IsDiagonalCross(roles[i]) ? CrossDiagonal : 0f;
            crosses[pair][i] = SpawnHelper(new Placement(at, rotation));
            crosses[pair][i]?.Cast(ActionId.AetherlettingCross, castSeconds: 13.7f);
        }
    }

    private void ResolveCrosses(int pair)
    {
        foreach (var cross in crosses[pair] ?? Array.Empty<SimEnemy?>())
            state.Satisfied.AddFor(damage.Resolve(cross, ActionId.AetherlettingCross, [DamageType.Lethal], []));
    }

    private void DespawnAll()
    {
        vamp?.Despawn();
        foreach (var helper in helpers) helper.Despawn();
        helpers.Clear();
        pulses.Clear();
    }
}
