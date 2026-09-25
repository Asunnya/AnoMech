using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.M9s.M9sConstants;

namespace AnoMech.Scenarios.M9s.Deathmatch;

// M9S Undead Deathmatch, from the shared towers to the Brutal Rain before the third Vamp Stomp: two
// 6y towers of exactly four, a bat leashed to each tower's group, and two Sanguine Scratch cycles in
// which both bats sweep half the rim and go off as a 7y circle or a 4-15y donut where they stop.
// Scenario time 0 is 446.0s into the clear in Network_30301_20260923.log (pull 10); the arena is
// still inside the death-wall ring from Hell in a Cell.
public sealed class M9sDeathmatchScenario : IScenario
{
    public string Name => "Undead Deathmatch";
    public IPhase Phase => M9sZone.Fight;
    public bool SupportsSolo => true;

    public IReadOnlyList<IScenarioAi> AiStrats => [new M9sDeathmatchAi()];

    public void DrawSettings() => settingsWindow.Draw();
    private readonly M9sDeathmatchSettingsWindow settingsWindow = new();

    private const float BatStep = 0.05f;
    private const float LeashCheckStep = 0.1f;
    private const float LeashExplosionInterval = 3f;
    private const float LeashDamageAtSlack = 0.45f;
    private const float DamageDownSeconds = 30f;
    private const int ShapeCircle = 0x426;
    private const int ShapeDonut = 0x427;

    private M9sDeathmatchState state = null!;
    private SimWorld world = null!;
    private DamageSolver damage = null!;
    private M9sSanguineScratch scratch = null!;

    private SimEnemy? vamp;
    private int brutalRainHits;
    private readonly List<SimEnemy> helpers = [];
    private readonly List<SimEnemy> towers = [];
    private readonly SimEnemy?[] bats = new SimEnemy?[2];
    private readonly Dictionary<PartyRole, Leash> leashes = [];

    private sealed class Leash(SimCharacter member, int group)
    {
        public SimCharacter Member { get; } = member;
        public int Group { get; } = group;
        public SimTether? Tether { get; set; }
        public bool Stretched { get; set; }
        public float NextExplosionAt { get; set; }
    }

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        damage = new DamageSolver(world.Party);
        damage.SetStatuses(DamageType.Magic, StatusId.MagicVulnerabilityUp);
        helpers.Clear();
        towers.Clear();
        leashes.Clear();
        Array.Clear(bats);

        state = new M9sDeathmatchState(settingsWindow.Overrides);
        scratch = new M9sSanguineScratch(damage, state.Satisfied, SpawnHelper);
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<M9sDeathmatchState>)AiStrats[idx]).Run(state, world);

        world.Events.Add(0f, SpawnVamp);
        world.Events.Add(0.1f, () => world.Map.AddEffect(0x00020001, 0x10));
        world.Events.Add(0.1f, () => world.EnforceArenaBoundary(Geometry.RingArenaRadius, "Walked into the death wall"));

        world.Events.Add(2.71f, CastTowers);
        world.Events.Add(7.71f, ResolveTowers);
        world.Events.Add(8.13f, SpawnLeashedBats);
        ScheduleLeashChecks();
        ScheduleBatSweeps();

        world.Events.Add(11.86f, () => CastScratch(0));
        world.Events.Add(14.85f, () => scratch.ResolveWave(Vector3.Zero, state.Cycles[0].FirstConeOffset, 0));
        world.Events.Add(17.27f, () => scratch.ResolveWave(Vector3.Zero, state.Cycles[0].FirstConeOffset, 1));
        world.Events.Add(19.67f, () => scratch.ResolveWave(Vector3.Zero, state.Cycles[0].FirstConeOffset, 2));
        world.Events.Add(22.05f, () => scratch.ResolveWave(Vector3.Zero, state.Cycles[0].FirstConeOffset, 3));
        world.Events.Add(22.33f, () => RevealBatShapes(0));
        world.Events.Add(24.47f, () => scratch.ResolveWave(Vector3.Zero, state.Cycles[0].FirstConeOffset, 4));
        world.Events.Add(26.42f, () => CastBatShapes(0));
        world.Events.Add(27.42f, () => ResolveBatShapes(0));

        world.Events.Add(30.19f, () => CastScratch(1));
        world.Events.Add(33.19f, () => scratch.ResolveWave(Vector3.Zero, state.Cycles[1].FirstConeOffset, 0));
        world.Events.Add(35.59f, () => scratch.ResolveWave(Vector3.Zero, state.Cycles[1].FirstConeOffset, 1));
        world.Events.Add(37.99f, () => scratch.ResolveWave(Vector3.Zero, state.Cycles[1].FirstConeOffset, 2));
        world.Events.Add(40.38f, () => scratch.ResolveWave(Vector3.Zero, state.Cycles[1].FirstConeOffset, 3));
        world.Events.Add(40.70f, () => RevealBatShapes(1));
        world.Events.Add(42.78f, () => scratch.ResolveWave(Vector3.Zero, state.Cycles[1].FirstConeOffset, 4));
        world.Events.Add(44.76f, () => CastBatShapes(1));
        world.Events.Add(45.76f, () => ResolveBatShapes(1));
        world.Events.Add(46.50f, DespawnBats);

        world.Events.Add(47.42f, MarkBrutalRain);
        world.Events.Add(47.51f, () => vamp?.Cast(ActionId.BrutalRainCast, castSeconds: 3.5f));
        world.Events.Add(52.55f, () => ResolveBrutalRain(0));
        world.Events.Add(53.62f, () => ResolveBrutalRain(1));
        world.Events.Add(54.69f, () => ResolveBrutalRain(2));
        world.Events.Add(55.76f, () => ResolveBrutalRain(3));
        world.Events.Add(56.82f, () => ResolveBrutalRain(4));
        world.Events.Add(57.89f, () => ResolveBrutalRain(5));
        world.Events.Add(59f, DespawnAll);
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

    private void CastTowers()
    {
        vamp?.Cast(ActionId.UndeadDeathmatch, castSeconds: 3.5f);
        for (var group = 0; group < 2; group++)
            if (SpawnHelper(new Placement(state.TowerPosition(group), 0f)) is { } tower)
            {
                tower.Cast(ActionId.BloodyBondageParty, castSeconds: 4.7f);
                towers.Add(tower);
            }
    }

    // Each tower wants exactly four; anything else sets off the tower on the party.
    private void ResolveTowers()
    {
        foreach (var tower in towers)
        {
            var soakers = world.Party.Find.InsideCircle(tower.Position, M9sDeathmatchState.TowerRadius).Count;
            if (soakers == 4) continue;
            world.Party.WipeAllPlayers($"Bloody Bondage tower took {soakers} instead of four");
            return;
        }
    }

    private void SpawnLeashedBats()
    {
        for (var group = 0; group < 2; group++)
        {
            var bearing = state.TowerBearing(group);
            bats[group] = world.SpawnEnemy(new EnemySpawnConfig(
                BNpcBaseId: BNpcBaseId.VampetteFatale,
                NameId: BNpcNameId.VampetteFatale,
                Level: 100,
                Targetable: false,
                EnemyList: EnemyListMode.Never,
                IsVisible: true,
                Placement: new Placement(state.TowerPosition(group), M9sDeathmatchState.FacingAlongRim(bearing, state.Cycles[0].Spin))));
            if (bats[group] is not { } bat) continue;
            helpers.Add(bat);
            foreach (var role in M9sDeathmatchState.Groups[group])
                if (world.Party.Get(role) is { } member && member.IsAlive())
                    leashes[role] = new Leash(member, group) { Tether = world.Tether(member, bat, TetherId.BatLeash) };
        }
    }

    // The bats sweep by position steps, like the Vamp Stomp bats, so they stay on the rim.
    private void ScheduleBatSweeps()
    {
        for (var cycle = 0; cycle < 2; cycle++)
            for (var t = M9sDeathmatchState.SweepStartAt[cycle]; t <= M9sDeathmatchState.SweepEndAt[cycle] + BatStep; t += BatStep)
            {
                var at = t;
                var spin = state.Cycles[cycle].Spin;
                world.Events.Add(at, () => MoveBats(at, spin));
            }
    }

    private void MoveBats(float time, int spin)
    {
        for (var group = 0; group < 2; group++)
        {
            var bearing = state.BatBearing(group, time);
            bats[group]?.SetPosition(new Placement(M9sDeathmatchState.AtBearing(bearing, M9sDeathmatchState.TowerDistance), M9sDeathmatchState.FacingAlongRim(bearing, spin)));
        }
    }

    private void CastScratch(int cycle)
    {
        vamp?.Cast(ActionId.SanguineScratchCast, castSeconds: 2f);
        scratch.CastFirstWave(Vector3.Zero, state.Cycles[cycle].FirstConeOffset);
    }

    private void RevealBatShapes(int cycle)
    {
        for (var group = 0; group < 2; group++)
            bats[group]?.AddStatusParam(StatusId.BatShape, state.BatIsCircle(group, cycle) ? ShapeCircle : ShapeDonut);
    }

    private void CastBatShapes(int cycle)
    {
        for (var group = 0; group < 2; group++)
            bats[group]?.Cast(BatActionId(group, cycle), castSeconds: 0.7f);
    }

    private void ResolveBatShapes(int cycle)
    {
        for (var group = 0; group < 2; group++)
        {
            var circle = state.BatIsCircle(group, cycle);
            state.Satisfied.AddFor(damage.Resolve(bats[group], BatActionId(group, cycle), [DamageType.Lethal], [],
                size: circle ? null : M9sDeathmatchState.DonutInnerRadius));
        }
    }

    private uint BatActionId(int group, int cycle) => (state.BatIsCircle(group, cycle), cycle) switch
    {
        (true, 0) => ActionId.BreakdownDrop1,
        (false, 0) => ActionId.BreakwingBeat1,
        (true, _) => ActionId.BreakdownDrop2,
        _ => ActionId.BreakwingBeat2,
    };

    private void ScheduleLeashChecks()
    {
        for (var t = 8.2f; t <= 46.4f; t += LeashCheckStep)
        {
            var at = t;
            world.Events.Add(at, () => CheckLeashes(at));
        }
    }

    // A leash past its slack turns long and blows up on its player, again every 3s while it stays long.
    private void CheckLeashes(float time)
    {
        foreach (var leash in leashes.Values)
        {
            if (!leash.Member.IsAlive() || bats[leash.Group] is not { } bat) continue;
            var length = M9sDeathmatchState.FlatDistance(leash.Member.Position, bat.Position);
            var stretched = length > M9sDeathmatchState.LeashSlack;
            if (stretched != leash.Stretched)
            {
                leash.Stretched = stretched;
                leash.Tether?.Despawn();
                leash.Tether = world.Tether(leash.Member, bat, stretched ? TetherId.BatLeashStretched : TetherId.BatLeash);
                leash.NextExplosionAt = time;
            }
            if (!stretched || time < leash.NextExplosionAt) continue;
            leash.NextExplosionAt = time + LeashExplosionInterval;
            ExplodeLeash(leash.Member, length);
        }
    }

    private void ExplodeLeash(SimCharacter member, float length)
    {
        SpawnHelper(new Placement(Vector3.Zero, 0f))?.Cast(ActionId.LeashExplosion, castSeconds: 0f, targetId: member.GameObjectId, animationLock: 0f);
        var fraction = LeashDamageAtSlack * length / M9sDeathmatchState.LeashSlack;
        damage.ApplyDamage(member, MathF.Min(fraction, 1f), ActionId.LeashExplosion, $"leash stretched to {length:0}y", lethal: fraction >= 1f);
        member.AddStatus(StatusId.DamageDown, DamageDownSeconds);
        state.Satisfied.Add(1);
    }

    private void DespawnBats()
    {
        foreach (var leash in leashes.Values) leash.Tether?.Despawn();
        leashes.Clear();
        foreach (var bat in bats) bat?.Despawn();
        Array.Clear(bats);
    }

    private SimCharacter? BrutalRainTarget() => world.Party.Get(state.BrutalRainTarget) is { } t && t.IsAlive() ? t : null;

    private void MarkBrutalRain()
    {
        brutalRainHits = state.Satisfied.BrutalRainHits;
        BrutalRainTarget()?.AttachLockonVfx(LockonId.ShareMulti, persistent: false);
    }

    // A fresh helper per hit: one helper can't start a second cast while the first is still releasing.
    private void ResolveBrutalRain(int hit)
    {
        if (hit >= brutalRainHits || BrutalRainTarget() is not { } target) return;
        SpawnHelper(new Placement(Vector3.Zero, 0f))?.Cast(ActionId.BrutalRainHit, castSeconds: 0f, targetId: target.GameObjectId, animationLock: 0f);
        damage.Resolve(target, ActionId.BrutalRainHit, [], [], stackMinTargets: 4);
    }

    private void DespawnAll()
    {
        vamp?.Despawn();
        DespawnBats();
        foreach (var helper in helpers) helper.Despawn();
        helpers.Clear();
        towers.Clear();
    }
}
