using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Geometry;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.M9s.M9sConstants;

namespace AnoMech.Scenarios.M9s.VampStomp;

// M9S opener, pull to the first Sadistic Screech: Killer Voice, Hardcore, Vamp Stomp (see
// M9sBatStomp), then Brutal Rain. Timings are the clear in Network_30301_20260923.log (pull 10);
// shapes are the Action sheet's (the same BossMod uses).
public sealed class M9sVampStompScenario : IScenario
{
    public string Name => "Vamp Stomp";
    public IPhase Phase => M9sZone.Fight;
    public bool SupportsSolo => true;

    public IReadOnlyList<IScenarioAi> AiStrats => [new M9sVampStompAi()];

    public void DrawSettings() => settingsWindow.Draw();
    private readonly M9sVampStompSettingsWindow settingsWindow = new();

    private M9sVampStompState state = null!;
    private SimWorld world = null!;
    private SimParty party = null!;
    private DamageSolver damage = null!;
    private M9sBatStomp stomp = null!;
    private M9sHardcore hardcore = null!;
    private int brutalRainHits;

    private SimEnemy? vamp;
    private readonly List<SimEnemy> helpers = [];

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        party = worldParam.Party;
        damage = new DamageSolver(party);
        damage.SetStatuses(DamageType.Magic, StatusId.MagicVulnerabilityUp);
        helpers.Clear();

        state = new M9sVampStompState(world.Rng, settingsWindow.Overrides);
        stomp = new M9sBatStomp(world, damage, state.Satisfied, state.Bats, SpawnHelper);
        hardcore = new M9sHardcore(party, damage, state.Satisfied, SpawnHelper);
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<M9sVampStompState>)AiStrats[idx]).Run(state, world);

        world.Events.Add(0f, SpawnVamp);
        world.Events.Add(0f, () => world.EnforceArenaBoundary(new SquareArena(Geometry.ArenaHalfWidth), "Walked off the arena"));
        world.Events.Add(0.08f, () => vamp?.MoveTo(new Vector3(0f, 0f, -0.5f), 7.5f));
        world.Events.Add(5f, () => vamp?.Cast(ActionId.KillerVoice, castSeconds: 4.7f));
        world.Events.Add(9.96f, () => M9sUtils.Raidwide(party, damage, ActionId.KillerVoice, 0.45f));

        world.Events.Add(15.14f, () => vamp?.Cast(ActionId.HardcoreCast, castSeconds: 2.7f));
        world.Events.Add(15.14f, () => hardcore.Cast(vamp?.Position ?? Vector3.Zero));
        world.Events.Add(20.10f, hardcore.Resolve);

        world.Events.Add(25.24f, stomp.ApplyCurses);
        world.Events.Add(25.24f, stomp.SpawnBats);
        world.Events.Add(25.33f, () => vamp?.Cast(ActionId.VampStompCast, targetLocation: Vector3.Zero, castSeconds: 3.8f));
        world.Events.Add(25.33f, stomp.CastVampStomp);
        world.Events.Add(30.29f, stomp.ResolveVampStomp);
        world.Events.Add(30.47f, () => vamp?.SetPosition(new Placement(Vector3.Zero, MathF.PI)));
        world.Events.Add(30.47f, stomp.CastBatRing);
        world.Events.Add(32.57f, () => vamp?.MoveTo(new Vector3(0.2f, 0f, -7.9f)));

        stomp.ScheduleBatFlight();
        world.Events.Add(33.51f, () => stomp.CastBlastBeat(0));
        world.Events.Add(34.49f, () => stomp.ResolveBlastBeat(0));
        world.Events.Add(36.99f, () => stomp.CastBlastBeat(1));
        world.Events.Add(37.85f, () => stomp.DespawnBats(0));
        world.Events.Add(37.97f, () => stomp.ResolveBlastBeat(1));
        world.Events.Add(40.48f, () => stomp.CastBlastBeat(2));
        world.Events.Add(41.38f, () => stomp.DespawnBats(1));
        world.Events.Add(41.46f, () => stomp.ResolveBlastBeat(2));
        world.Events.Add(45.06f, () => stomp.DespawnBats(2));

        stomp.ScheduleCurseRing();

        world.Events.Add(41.37f, MarkBrutalRain);
        world.Events.Add(41.46f, () => vamp?.Cast(ActionId.BrutalRainCast, castSeconds: 3.5f));
        world.Events.Add(46.47f, () => ResolveBrutalRain(0));
        world.Events.Add(47.53f, () => ResolveBrutalRain(1));
        world.Events.Add(48.61f, () => ResolveBrutalRain(2));
        world.Events.Add(49.68f, () => ResolveBrutalRain(3));
        world.Events.Add(50.75f, () => ResolveBrutalRain(4));
        world.Events.Add(51.82f, () => ResolveBrutalRain(5));

        world.Events.Add(53f, DespawnAll);
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
            Placement: new Placement(new Vector3(0f, 0f, -10f), 0f)));
        state.Satisfied.Attach(vamp);
    }

    private SimEnemy? SpawnHelper(Vector3 position)
    {
        var helper = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: BNpcBaseId.Helper,
            Level: 100,
            Targetable: false,
            EnemyList: EnemyListMode.Never,
            // Drawn so its action timelines play; the helper's ModelChara has no mesh.
            IsVisible: true,
            Placement: new Placement(position, 0f)));
        if (helper != null) helpers.Add(helper);
        return helper;
    }

    private SimCharacter? BrutalRainTarget() => party.Get(state.BrutalRainTarget) is { } t && t.IsAlive() ? t : null;

    // The hit count is fixed when the marker goes up, from the stacks the boss has then.
    private void MarkBrutalRain()
    {
        brutalRainHits = state.Satisfied.BrutalRainHits;
        BrutalRainTarget()?.AttachLockonVfx(LockonId.ShareMulti, persistent: false);
    }

    // A fresh helper per hit: one helper can't start a second cast while the first is still releasing.
    private void ResolveBrutalRain(int hit)
    {
        if (hit >= brutalRainHits || BrutalRainTarget() is not { } target) return;
        SpawnHelper(Vector3.Zero)?.Cast(ActionId.BrutalRainHit, castSeconds: 0f, targetId: target.GameObjectId, animationLock: 0f);
        damage.Resolve(target, ActionId.BrutalRainHit, [], [], stackMinTargets: 4);
    }

    private void DespawnAll()
    {
        vamp?.Despawn();
        foreach (var helper in helpers) helper.Despawn();
        helpers.Clear();
        stomp.DespawnBats();
    }
}
