using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.M9s.M9sConstants;

namespace AnoMech.Scenarios.M9s.VampStomp2;

// M9S second Vamp Stomp, from the second Hardcore to Insatiable Thirst bringing the square arena
// back: Hardcore, Pulping Pulse, Vamp Stomp (see M9sBatStomp) inside the death-wall ring, Half Moon,
// Brutal Rain on a healer (four hits at the four Satisfied stacks the phase starts with). Scenario
// time 0 is 199.89s into the clear in Network_30301_20260923.log (pull 10), which puts the stomp on
// the same clock as the opener's.
public sealed class M9sVampStomp2Scenario : IScenario
{
    public string Name => "Vamp Stomp 2";
    public IPhase Phase => M9sZone.Fight;
    public bool SupportsSolo => true;

    public IReadOnlyList<IScenarioAi> AiStrats => [new M9sVampStomp2Ai()];

    public void DrawSettings() => settingsWindow.Draw();
    private readonly M9sVampStomp2SettingsWindow settingsWindow = new();

    private M9sVampStomp2State state = null!;
    private SimWorld world = null!;
    private DamageSolver damage = null!;
    private M9sBatStomp stomp = null!;
    private M9sHardcore hardcore = null!;
    private int brutalRainHits;

    private SimEnemy? vamp;
    private SimEnemy? shortCleave;
    private SimEnemy? longCleave;
    private readonly List<SimEnemy> helpers = [];
    private readonly List<SimEnemy> pulses = [];

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        damage = new DamageSolver(world.Party);
        damage.SetStatuses(DamageType.Magic, StatusId.MagicVulnerabilityUp);
        helpers.Clear();
        pulses.Clear();

        state = new M9sVampStomp2State(world.Rng, settingsWindow.Overrides);
        stomp = new M9sBatStomp(world, damage, state.Satisfied, state.Bats, SpawnHelper);
        hardcore = new M9sHardcore(world.Party, damage, state.Satisfied, SpawnHelper);
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<M9sVampStomp2State>)AiStrats[idx]).Run(state, world);

        world.Events.Add(0f, SpawnVamp);
        world.Events.Add(0.1f, () => world.Map.AddEffect(0x00020001, 0x10));
        world.Events.Add(0.1f, () => world.EnforceArenaBoundary(Geometry.RingArenaRadius, "Walked into the death wall"));

        world.Events.Add(15.18f, () => vamp?.Cast(ActionId.HardcoreCast, castSeconds: 2.7f));
        world.Events.Add(15.18f, () => hardcore.Cast(vamp?.Position ?? Vector3.Zero));
        world.Events.Add(20.14f, hardcore.Resolve);

        world.Events.Add(21.19f, CastPulpingPulses);
        world.Events.Add(25.16f, ResolvePulpingPulses);

        world.Events.Add(25.24f, stomp.ApplyCurses);
        world.Events.Add(25.24f, stomp.SpawnBats);
        world.Events.Add(25.33f, () => vamp?.Cast(ActionId.VampStompCast, targetLocation: Vector3.Zero, castSeconds: 3.8f));
        world.Events.Add(25.33f, stomp.CastVampStomp);
        world.Events.Add(30.29f, stomp.ResolveVampStomp);
        world.Events.Add(30.47f, () => vamp?.SetPosition(new Placement(Vector3.Zero, MathF.PI)));
        world.Events.Add(30.47f, stomp.CastBatRing);
        world.Events.Add(32.57f, () => vamp?.MoveTo(M9sVampStomp2State.BossTanked.Position, 6f, M9sVampStomp2State.BossTanked.Rotation));

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

        world.Events.Add(41.53f, CastHalfMoon);
        world.Events.Add(46.55f, () => ResolveCleave(shortCleave, M9sHalfMoon.ShortId(state.HalfMoon, state.HalfMoonIsMore)));
        world.Events.Add(49.53f, () => ResolveCleave(longCleave, M9sHalfMoon.LongId(state.HalfMoon, state.HalfMoonIsMore)));

        world.Events.Add(51.59f, MarkBrutalRain);
        world.Events.Add(51.68f, () => vamp?.Cast(ActionId.BrutalRainCast, castSeconds: 3.5f));
        world.Events.Add(56.73f, () => ResolveBrutalRain(0));
        world.Events.Add(57.80f, () => ResolveBrutalRain(1));
        world.Events.Add(58.87f, () => ResolveBrutalRain(2));
        world.Events.Add(59.93f, () => ResolveBrutalRain(3));
        world.Events.Add(61.00f, () => ResolveBrutalRain(4));
        world.Events.Add(62.07f, () => ResolveBrutalRain(5));

        world.Events.Add(64.81f, () => vamp?.Cast(ActionId.InsatiableThirstCast, castSeconds: 2.5f));
        world.Events.Add(70.60f, () => M9sUtils.Raidwide(world.Party, damage, ActionId.InsatiableThirst, 0.35f));
        world.Events.Add(70.83f, () => world.Map.AddEffect(0x00080004, 0x10));
        world.Events.Add(72f, DespawnAll);
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

    private void CastPulpingPulses()
    {
        foreach (var at in state.PulpingPulses)
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

    private void CastHalfMoon()
    {
        var boss = M9sVampStomp2State.BossTanked;
        // The boss can't walk while casting, so a MoveTo can land after the omen; pin her to the
        // cleave's origin as it goes up.
        vamp?.StopMoving();
        vamp?.SetPosition(boss);
        var more = state.HalfMoonIsMore = state.Satisfied.IsMore;
        vamp?.Cast(M9sHalfMoon.BossCastId(state.HalfMoon, more), castSeconds: 4f);
        shortCleave = SpawnHelper(M9sHalfMoon.CleaveOrigin(boss, state.HalfMoonShortRotation, more));
        shortCleave?.Cast(M9sHalfMoon.ShortId(state.HalfMoon, more), castSeconds: 4.7f);
        longCleave = SpawnHelper(M9sHalfMoon.CleaveOrigin(boss, state.HalfMoonLongRotation, more));
        longCleave?.Cast(M9sHalfMoon.LongId(state.HalfMoon, more), castSeconds: 7.7f, omenDelay: 4.7f);
    }

    private void ResolveCleave(SimEnemy? cleave, uint actionId) =>
        state.Satisfied.AddFor(damage.Resolve(cleave, actionId, [DamageType.Lethal], [], size: MathF.PI / 2f));

    private SimCharacter? BrutalRainTarget() => world.Party.Get(state.BrutalRainTarget) is { } t && t.IsAlive() ? t : null;

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
        pulses.Clear();
        stomp.DespawnBats();
    }
}
