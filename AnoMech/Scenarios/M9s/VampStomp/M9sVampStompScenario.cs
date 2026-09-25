using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.M9s.M9sConstants;

namespace AnoMech.Scenarios.M9s.VampStomp;

// M9S opener, pull to the first Sadistic Screech: Killer Voice, Hardcore, Vamp Stomp with its
// three bat rings and the Curse of the Bombpyre ring, then Brutal Rain. Timings are the clear in
// Network_30301_20260923.log (pull 10); shapes are the Action sheet's (the same BossMod uses).
//
// Every player gets Curse of the Bombpyre with Vamp Stomp. An invisible ring then grows from the
// centre at 2y/s starting with BatRing; whoever it reaches loses the curse and explodes for 8y,
// giving everyone caught Magic Vulnerability Up. That is what makes the stomp a spread: two
// explosions landing on one player, or a bat blast on top of one, kill.
public sealed class M9sVampStompScenario : IScenario
{
    public string Name => "Vamp Stomp";
    public IPhase Phase => M9sZone.Fight;
    public bool SupportsSolo => true;

    public IReadOnlyList<IScenarioAi> AiStrats => [new M9sVampStompAi()];

    public void DrawSettings() => settingsWindow.Draw();
    private readonly M9sVampStompSettingsWindow settingsWindow = new();

    private const float OrbitStep = 0.05f;
    private const float CurseRingStep = 0.05f;
    private const float CurseRingSeconds = 16f;

    private M9sVampStompState state = null!;
    private SimWorld world = null!;
    private SimParty party = null!;
    private DamageSolver damage = null!;

    private SimEnemy? vamp;
    private SimEnemy? stompHelper;
    private SimOmen? curseRing;
    private readonly List<SimEnemy> helpers = [];
    private readonly List<(SimEnemy Bat, BatRing Ring, int Index)> bats = [];

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        party = worldParam.Party;
        damage = new DamageSolver(party);
        damage.SetStatuses(DamageType.Magic, StatusId.MagicVulnerabilityUp);
        helpers.Clear();
        bats.Clear();

        state = new M9sVampStompState(settingsWindow.Overrides);
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<M9sVampStompState>)AiStrats[idx]).Run(state, world);

        world.Events.Add(0f, SpawnVamp);
        world.Events.Add(0.08f, () => vamp?.MoveTo(new Vector3(0f, 0f, -0.5f), 7.5f));
        world.Events.Add(5f, () => vamp?.Cast(ActionId.KillerVoice, castSeconds: 4.7f));

        world.Events.Add(15.14f, () => vamp?.Cast(ActionId.HardcoreCast, castSeconds: 2.7f));
        world.Events.Add(15.14f, CastHardcoreOnTanks);
        world.Events.Add(20.10f, ResolveHardcore);

        world.Events.Add(25.24f, ApplyCurses);
        world.Events.Add(25.24f, SpawnBats);
        world.Events.Add(25.33f, () => vamp?.Cast(ActionId.VampStompCast, targetLocation: Vector3.Zero, castSeconds: 3.8f));
        world.Events.Add(25.33f, CastVampStomp);
        world.Events.Add(30.29f, () => damage.Resolve(stompHelper, ActionId.VampStomp, [DamageType.Lethal], []));
        world.Events.Add(30.47f, () => vamp?.SetPosition(new Placement(Vector3.Zero, MathF.PI)));
        world.Events.Add(30.47f, () => stompHelper?.Cast(ActionId.BatRing, castSeconds: 0f, animationLock: 0f));
        world.Events.Add(32.57f, () => vamp?.MoveTo(new Vector3(0.2f, 0f, -7.9f)));

        ScheduleBatFlight();
        world.Events.Add(33.51f, () => CastBlastBeat(state.Rings[0]));
        world.Events.Add(34.49f, () => ResolveBlastBeat(state.Rings[0]));
        world.Events.Add(36.99f, () => CastBlastBeat(state.Rings[1]));
        world.Events.Add(37.85f, () => DespawnBats(state.Rings[0]));
        world.Events.Add(37.97f, () => ResolveBlastBeat(state.Rings[1]));
        world.Events.Add(40.48f, () => CastBlastBeat(state.Rings[2]));
        world.Events.Add(41.38f, () => DespawnBats(state.Rings[1]));
        world.Events.Add(41.46f, () => ResolveBlastBeat(state.Rings[2]));
        world.Events.Add(45.06f, () => DespawnBats(state.Rings[2]));

        ScheduleCurseRing();

        world.Events.Add(41.37f, () => BrutalRainTarget()?.AttachLockonVfx(LockonId.ShareMulti, persistent: false));
        world.Events.Add(41.46f, () => vamp?.Cast(ActionId.BrutalRainCast, castSeconds: 3.5f));
        world.Events.Add(46.47f, ResolveBrutalRain);
        world.Events.Add(47.53f, ResolveBrutalRain);
        world.Events.Add(48.61f, ResolveBrutalRain);

        world.Events.Add(52f, DespawnAll);
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

    private void CastHardcoreOnTanks()
    {
        foreach (var role in new[] { PartyRole.MainTank, PartyRole.OffTank })
        {
            if (party.Get(role) is not { } tank || !tank.IsAlive()) continue;
            tank.AttachLockonVfx(LockonId.Tankbuster, persistent: false);
            SpawnHelper(vamp?.Position ?? Vector3.Zero)?.Cast(ActionId.HardcoreSmall, castSeconds: 4.7f, targetId: tank.GameObjectId);
        }
    }

    private void ResolveHardcore()
    {
        damage.Resolve(party.Get(PartyRole.MainTank), ActionId.HardcoreSmall, [DamageType.TankBuster], []);
        damage.Resolve(party.Get(PartyRole.OffTank), ActionId.HardcoreSmall, [DamageType.TankBuster], []);
    }

    private void ApplyCurses()
    {
        for (var slot = 0; slot < 8; slot++)
            if (party.Get(slot) is { } member && member.IsAlive())
                member.AddStatus(StatusId.CurseOfTheBombpyre);
    }

    private void CastVampStomp()
    {
        stompHelper = SpawnHelper(Vector3.Zero);
        stompHelper?.Cast(ActionId.VampStomp, castSeconds: 4.7f);
    }

    private void SpawnBats()
    {
        foreach (var ring in state.Rings)
            for (var i = 0; i < ring.Count; i++)
            {
                var bat = world.SpawnEnemy(new EnemySpawnConfig(
                    BNpcBaseId: BNpcBaseId.VampetteFatale,
                    NameId: BNpcNameId.VampetteFatale,
                    Level: 100,
                    Targetable: false,
                    EnemyList: EnemyListMode.Never,
                    IsVisible: true,
                    Placement: state.BatPlacement(ring, i, 0f)));
                if (bat != null) bats.Add((bat, ring, i));
            }
    }

    // Bats orbit by position steps rather than MoveTo, which only walks straight lines. Stepping on
    // world.Events keeps the flight locked to the blasts under EventTimeScale.
    private void ScheduleBatFlight()
    {
        for (var t = M9sVampStompState.BatMoveStartAt; t <= state.Rings[^1].CastAt + OrbitStep; t += OrbitStep)
        {
            var at = t;
            world.Events.Add(at, () =>
            {
                foreach (var (bat, ring, index) in bats)
                    if (at <= ring.CastAt + OrbitStep) bat.SetPosition(state.BatPlacement(ring, index, at));
            });
        }
    }

    private void CastBlastBeat(BatRing ring)
    {
        foreach (var (bat, batRing, _) in bats)
            if (batRing == ring) bat.Cast(ActionId.BlastBeatBat, castSeconds: 0.7f);
    }

    private void ResolveBlastBeat(BatRing ring)
    {
        foreach (var (bat, batRing, _) in bats)
            if (batRing == ring) damage.Resolve(bat, ActionId.BlastBeatBat, [DamageType.Lethal], []);
    }

    private void DespawnBats(BatRing ring)
    {
        foreach (var (bat, batRing, _) in bats)
            if (batRing == ring) bat.Despawn();
    }

    private void ScheduleCurseRing()
    {
        world.Events.Add(M9sVampStompState.BatRingStartAt, () =>
            curseRing = world.SpawnOmen(VfxPath.CurseRing, new Placement(Vector3.Zero, 0f), RingScale(CurseRingStep * M9sVampStompState.BatRingSpeed), CurseRingSeconds));

        var endAt = M9sVampStompState.BatRingStartAt + CurseRingSeconds;
        for (var t = M9sVampStompState.BatRingStartAt + CurseRingStep; t <= endAt; t += CurseRingStep)
        {
            var radius = M9sVampStompState.BatRingRadiusAt(t);
            world.Events.Add(t, () =>
            {
                curseRing?.SetScale(RingScale(radius));
                DetonateCursesInside(radius);
            });
        }
    }

    // Omen files are unit-sized; SimOmen scales circles and donuts by EffectRange the same way.
    private static Vector3 RingScale(float radius) => new(radius, 1f, radius);

    private void DetonateCursesInside(float radius)
    {
        for (var slot = 0; slot < 8; slot++)
        {
            if (party.Get(slot) is not { } member || !member.HasStatus(StatusId.CurseOfTheBombpyre)) continue;
            if (new Vector2(member.Position.X, member.Position.Z).Length() > radius) continue;

            member.RemoveStatus(StatusId.CurseOfTheBombpyre);
            CastOn(ActionId.BlastBeatSpread, member);
            damage.Resolve(member, ActionId.BlastBeatSpread, [DamageType.Magic],
                [(StatusId.MagicVulnerabilityUp, 1.96f)]);
        }
    }

    private SimCharacter? BrutalRainTarget() => party.Get(state.BrutalRainTarget) is { } t && t.IsAlive() ? t : null;

    private void ResolveBrutalRain()
    {
        if (BrutalRainTarget() is not { } target) return;
        CastOn(ActionId.BrutalRainHit, target);
        damage.Resolve(target, ActionId.BrutalRainHit, [], [], stackMinTargets: 4);
    }

    // A fresh helper per hit: several of these land in the same frame, and one helper can't
    // start a second cast while the first is still releasing.
    private void CastOn(uint actionId, SimCharacter target) =>
        SpawnHelper(Vector3.Zero)?.Cast(actionId, castSeconds: 0f, targetId: target.GameObjectId, animationLock: 0f);

    private void DespawnAll()
    {
        vamp?.Despawn();
        foreach (var helper in helpers) helper.Despawn();
        helpers.Clear();
        foreach (var (bat, _, _) in bats) bat.Despawn();
        bats.Clear();
    }
}
