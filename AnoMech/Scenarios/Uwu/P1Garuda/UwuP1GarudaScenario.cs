using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using static AnoMech.Scenarios.Uwu.UwuConstants;

namespace AnoMech.Scenarios.Uwu.P1Garuda;

// UWU P1, Garuda from the pull to her death. Scenario time 0 is the pull of the clear in
// Network_30208_20260816.log (pull 18); every timestamp below is that pull's, and the phase ends
// where it killed her.
//
// Thermal Low is the phase's bookkeeping: Friction and the Spiny Plume's Cyclone stack it (to 2),
// standing in the bubble the Spiny's Gigastorm leaves cleanses it, and Mesohigh only spares a player
// who still carries it. Each two-stack cleanse charges Garuda; the fourth wakes her (the aura only:
// the clear killed her before a woken Wicked Wheel came up).
public sealed class UwuP1GarudaScenario : IScenario
{
    public string Name => "Garuda";
    public IPhase Phase => UwuZone.Garuda;
    public bool SupportsSolo => true;

    public IReadOnlyList<IScenarioAi> AiStrats => [new UwuP1GarudaAi()];

    private const float MistralSongHalfWidth = 2.5f;
    private const float MistralSongLength = 40f;
    private const float SlipstreamHalfAngle = MathF.PI / 4f;
    private const float SlipstreamLength = 11.7f;
    private const float GreatWhirlwindRadius = 8f;
    private const float FrictionRadius = 5f;
    private const float GigastormRadius = 6.5f;
    private const float WickedWheelRadius = 8.7f;
    private const float EyeOfTheStormInner = 12f;
    private const float EyeOfTheStormOuter = 25f;
    private const float MesohighRadius = 3f;
    private const float PassableHalfWidth = 1f;
    private const int MaxThermalLow = 2;
    private const uint BubbleEObjId = 0x1E8F68;
    private const float HazardStep = 0.1f;
    private const int ChargesToWake = 4;

    private SimWorld world = null!;
    private SimParty party = null!;
    private UwuUtils utils = null!;
    private DamageSolver damage = null!;
    private UwuP1GarudaState state = null!;

    private SimEnemy? garuda;
    private SimEnemy? suparna;
    private SimEnemy? chirada;
    private SimEnemy? spiny;
    private SimEventObject? bubble;
    private bool bubbleActive;
    private int aetherialCharges;
    private readonly List<SimEnemy> satinPlumes = [];
    private readonly List<SimEnemy> helpers = [];
    private readonly SimEnemy?[] featherDummies = new SimEnemy?[5];
    private readonly Dictionary<SimCharacter, float> bubbleDwell = [];

    private Func<SimEnemy?>[] FeatherRainDummies =>
        [() => featherDummies[0], () => featherDummies[1], () => featherDummies[2], () => featherDummies[3], () => featherDummies[4]];

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        party = world.Party;
        utils = new UwuUtils(world);
        damage = new DamageSolver(party);
        state = new UwuP1GarudaState();
        satinPlumes.Clear();
        helpers.Clear();
        greatWhirlwindCasters.Clear();
        greatWhirlwindSpots.Clear();
        eyeOfTheStorm = null;
        bubbleDwell.Clear();
        bubbleActive = false;
        aetherialCharges = 0;

        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<UwuP1GarudaState>)AiStrats[idx]).Run(state, world);

        world.Events.Add(0f, SpawnGaruda);
        world.Events.Add(0.2f, () => garuda?.MoveTo(new Vector3(0f, 0f, -0.7f), 8f, MathF.PI));
        ScheduleHazards();

        world.Events.Add(5.20f, () => Get(state.MistralSongTarget)?.AttachLockonVfx(LockonId.MistralSong, 5.2f));
        world.Events.Add(5.29f, () => CastSelf(garuda, ActionId.Slipstream, 2.2f));
        world.Events.Add(7.78f, () => ResolveCone(garuda, ActionId.Slipstream, garuda?.Rotation ?? MathF.PI, 2.1f, "Slipstream"));
        world.Events.Add(10.32f, () => ResolveMistralSong(garuda, ActionId.MistralSongBoss, Get(state.MistralSongTarget), GreatWhirlwindSpot.Boss));
        world.Events.Add(12.60f, () => garuda?.MoveTo(new Vector3(-6.3f, 0f, -0.5f), 8f, MathF.PI));
        world.Events.Add(13.44f, () => CastGreatWhirlwind(GreatWhirlwindSpot.Boss));
        world.Events.Add(16.43f, () => ResolveGreatWhirlwind(GreatWhirlwindSpot.Boss));

        world.Events.Add(18.52f, () => SpawnPlumes(state.SatinPlumesFirst, withSpiny: true));
        world.Events.Add(19.59f, () => CastGreatWhirlwind(GreatWhirlwindSpot.Boss));
        world.Events.Add(20.33f, ShowPlumes);
        world.Events.Add(20.52f, FixateSpinyOnOffTank);
        world.Events.Add(20.61f, () => CastSelf(garuda, ActionId.Slipstream, 2.2f));
        world.Events.Add(22.57f, () => ResolveGreatWhirlwind(GreatWhirlwindSpot.Boss));
        world.Events.Add(23.11f, () => ResolveCone(garuda, ActionId.Slipstream, garuda?.Rotation ?? MathF.PI, 2.1f, "Slipstream"));
        world.Events.Add(25.73f, () => CastGreatWhirlwind(GreatWhirlwindSpot.Boss));
        world.Events.Add(26.58f, () => ResolveDownburst());
        world.Events.Add(26.71f, SpinyCyclone);
        world.Events.Add(26.80f, () => spiny?.MoveTo(new Vector3(-6.8f, 0f, 1.9f), 2f));
        world.Events.Add(28.72f, () => ResolveGreatWhirlwind(GreatWhirlwindSpot.Boss));

        world.Events.Add(33.93f, () => garuda?.SetTargetable(false));
        world.Events.Add(34.02f, () => garuda?.PlayActionTimeline(ActionTimelineId.WarpStart2));
        utils.FeatherRain(FeatherRainDummies, 34.02f, 35.53f, 36.52f);
        world.Events.Add(35.17f, () => garuda?.SetVisible(false));
        world.Events.Add(35.85f, SpinyCyclone);
        world.Events.Add(35.90f, () => spiny?.MoveTo(UwuP1GarudaState.GigastormSpot, 2f));
        world.Events.Add(36.07f, () => garuda?.SetPosition(new Placement(Vector3.Zero, MathF.PI)));
        world.Events.Add(36.16f, () => garuda?.PlayActionTimeline(ActionTimelineId.WarpEnd));
        world.Events.Add(37.99f, () => garuda?.SetVisible(true));
        world.Events.Add(38.21f, () => garuda?.SetTargetable(true));
        world.Events.Add(38.21f, () => CastSelf(spiny, ActionId.Gigastorm, 2.7f));
        world.Events.Add(38.30f, () => CastSelf(garuda, ActionId.MistralShriek, 2.7f));
        world.Events.Add(39.12f, () => DespawnSatinPlume(0));
        world.Events.Add(41.19f, ResolveGigastorm);
        world.Events.Add(41.28f, () => Raidwide(garuda, ActionId.MistralShriek, 0.4f, 2.3f));
        world.Events.Add(42.98f, () => DespawnSatinPlume(1));
        world.Events.Add(43.45f, SpawnBubble);
        world.Events.Add(43.45f, () => bubbleActive = true);
        world.Events.Add(46.16f, () => DespawnSatinPlume(2));
        world.Events.Add(46.16f, () => DespawnSatinPlume(3));
        world.Events.Add(47.77f, () => bubble?.SetVisible(true));

        world.Events.Add(48.68f, () => CastFriction(state.FrictionTargets[0]));
        world.Events.Add(50.64f, () => ResolveFriction(state.FrictionTargets[0]));
        world.Events.Add(54.78f, () => CastFriction(state.FrictionTargets[1]));
        world.Events.Add(56.74f, () => ResolveFriction(state.FrictionTargets[1]));
        world.Events.Add(66.19f, DeactivateBubble);

        world.Events.Add(68.81f, () => garuda?.SetTargetable(false));
        world.Events.Add(68.90f, () => garuda?.PlayActionTimeline(ActionTimelineId.WarpStart2));
        utils.FeatherRain(FeatherRainDummies, 68.90f, 70.42f, 71.40f);
        world.Events.Add(69.63f, () => bubble?.Despawn());
        world.Events.Add(70.37f, () => garuda?.SetVisible(false));
        world.Events.Add(70.91f, () => garuda?.SetVisible(true));
        world.Events.Add(70.95f, () => garuda?.SetPosition(new Placement(Vector3.Zero, MathF.PI)));
        world.Events.Add(71.04f, () => garuda?.PlayActionTimeline(ActionTimelineId.WarpEnd));
        world.Events.Add(73.09f, () => garuda?.SetTargetable(true));
        world.Events.Add(73.18f, () => CastSelf(garuda, ActionId.AerialBlast, 2.7f));
        world.Events.Add(76.17f, () => Raidwide(garuda, ActionId.AerialBlast, 0.5f, 2.3f));

        world.Events.Add(87.62f, SpawnSisters);
        world.Events.Add(87.71f, () => PlaySisters(ActionTimelineId.SistersArrive));
        world.Events.Add(89.52f, () => ShowSisters(true));
        world.Events.Add(89.85f, () => PlaySisters(ActionTimelineId.WarpStart2));
        utils.FeatherRain(FeatherRainDummies, 89.85f, 91.36f, 92.34f);
        world.Events.Add(91.57f, () => ShowSisters(false));
        world.Events.Add(91.90f, () => PlaceSisters(state.SuparnaSongSpot, state.ChiradaSongSpot));
        world.Events.Add(91.99f, () => PlaySisters(ActionTimelineId.WarpEnd));
        world.Events.Add(94.04f, MarkSistersSongTargets);
        world.Events.Add(95.99f, () => ShowSisters(true));
        world.Events.Add(96.58f, CastEyeOfTheStorm);
        world.Events.Add(96.71f, () => CastSelf(garuda, ActionId.WickedWheelAwaken, 2.7f));
        world.Events.Add(99.20f, ResolveSistersSongs);
        world.Events.Add(99.56f, ResolveEyeOfTheStorm);
        world.Events.Add(99.69f, ResolveWickedWheel);
        world.Events.Add(101.29f, () => PlaySisters(ActionTimelineId.WarpStart2));
        utils.FeatherRain(FeatherRainDummies, 101.29f, 102.81f, 103.79f);
        world.Events.Add(102.32f, () => CastGreatWhirlwind(GreatWhirlwindSpot.Suparna));
        world.Events.Add(102.32f, () => CastGreatWhirlwind(GreatWhirlwindSpot.Chirada));
        world.Events.Add(102.57f, () => ShowSisters(false));
        world.Events.Add(105.31f, () => ResolveGreatWhirlwind(GreatWhirlwindSpot.Suparna));
        world.Events.Add(105.31f, () => ResolveGreatWhirlwind(GreatWhirlwindSpot.Chirada));

        world.Events.Add(110.84f, () => SpawnPlumes(state.SatinPlumesSecond, withSpiny: false));
        world.Events.Add(110.84f, ShowPlumes);
        world.Events.Add(112.40f, () => garuda?.MoveTo(new Vector3(-0.1f, 0f, -6.6f), 1.5f, MathF.PI));
        world.Events.Add(117.38f, () => PlaceSisters(UwuP1GarudaState.SuparnaTetherSpot, UwuP1GarudaState.ChiradaTetherSpot));
        world.Events.Add(117.47f, () => PlaySisters(ActionTimelineId.WarpEnd));
        world.Events.Add(118.76f, () => CastSelf(garuda, ActionId.Slipstream, 2.2f));
        world.Events.Add(119.28f, () => ShowSisters(true));
        world.Events.Add(119.56f, TetherMesohigh);
        world.Events.Add(119.61f, CastEyeOfTheStorm);
        world.Events.Add(121.26f, () => ResolveCone(garuda, ActionId.Slipstream, garuda?.Rotation ?? MathF.PI, 2.1f, "Slipstream"));
        world.Events.Add(122.59f, ResolveEyeOfTheStorm);
        world.Events.Add(124.64f, ResolveMesohigh);
        world.Events.Add(124.73f, () => ResolveDownburst());
        world.Events.Add(126.74f, () => PlaySisters(ActionTimelineId.WarpStart2));
        utils.FeatherRain(FeatherRainDummies, 126.74f, 128.25f, 129.23f);
        world.Events.Add(129.28f, () => DespawnSatinPlume(0));
        world.Events.Add(131.16f, () => DespawnSatinPlume(1));
        world.Events.Add(131.16f, () => DespawnSatinPlume(2));
        world.Events.Add(133.30f, () => DespawnSatinPlume(3));
        world.Events.Add(134.50f, KillGaruda);
        world.Events.Add(136f, DespawnAll);
    }

    private SimCharacter? Get(PartyRole role) => party.Get(role);

    private static bool IsTank(SimCharacter member) => member is ISimPartyMember { Role: PartyRole.MainTank or PartyRole.OffTank };

    private SimEnemy? SpawnEnemy(uint baseId, uint nameId, Placement placement, bool targetable, bool visible, EnemyListMode enemyList) =>
        world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: baseId,
            NameId: nameId,
            Level: Level,
            Targetable: targetable,
            EnemyList: enemyList,
            IsVisible: visible,
            Placement: placement));

    private SimEnemy? SpawnDummy(Vector3 at)
    {
        var dummy = SpawnEnemy(BNpcBaseId.Dummy, BNpcNameId.Dummy, new Placement(at, 0f), false, true, EnemyListMode.Never);
        if (dummy != null) helpers.Add(dummy);
        return dummy;
    }

    private void SpawnGaruda()
    {
        garuda = SpawnEnemy(BNpcBaseId.Garuda, BNpcNameId.Garuda, new Placement(new Vector3(0f, 0f, -10f), MathF.PI), true, true, EnemyListMode.Always);
        for (var i = 0; i < featherDummies.Length; i++) featherDummies[i] = SpawnDummy(Vector3.Zero);
    }

    private void CastSelf(SimEnemy? caster, uint actionId, float castSeconds) =>
        caster?.NativeCast(actionId, ActionType.Action, 0f, castSeconds, false, targetId: caster.GameObjectId);

    private void PlayEffect(SimEnemy? caster, uint actionId, float animationLock, float? rotation = null, GameObjectId? target = null) =>
        caster?.NativeActionEffect(actionId, animationLock, (ushort)actionId, 0, ActionType.Action, 0,
            rotation: rotation, animationTargetId: target ?? caster.GameObjectId);

    private void ResolveCone(SimEnemy? caster, uint actionId, float rotation, float animationLock, string cause)
    {
        if (caster == null) return;
        PlayEffect(caster, actionId, animationLock, rotation);
        foreach (var hit in party.Find.InsideCone(new Placement(caster.Position, rotation), SlipstreamHalfAngle, SlipstreamLength).ToList())
            if (!IsTank(hit)) hit.Die($"Died to {cause} (stood in front of Garuda)");
    }

    private void ResolveDownburst()
    {
        if (garuda == null || Get(PartyRole.MainTank) is not { } mainTank) return;
        var toTank = mainTank.Position - garuda.Position;
        ResolveCone(garuda, ActionId.Downburst, MathF.Atan2(toTank.X, toTank.Z), 2.1f, "Downburst");
    }

    private enum GreatWhirlwindSpot { Boss, Suparna, Chirada }

    private readonly Dictionary<GreatWhirlwindSpot, Vector3> greatWhirlwindSpots = [];
    private readonly Dictionary<GreatWhirlwindSpot, SimEnemy?> greatWhirlwindCasters = [];

    // The first player the song's line reaches takes it; a non-tank there dies. The line's green
    // tornado then drops where it was stopped.
    private void ResolveMistralSong(SimEnemy? caster, uint actionId, SimCharacter? target, GreatWhirlwindSpot spot)
    {
        if (caster == null || target == null) return;
        var toTarget = target.Position - caster.Position;
        var rotation = MathF.Atan2(toTarget.X, toTarget.Z);
        PlayEffect(caster, actionId, 1.1f, rotation, target.GameObjectId);

        var line = party.Find.InsideRect(new Placement(caster.Position, rotation), MistralSongHalfWidth, MistralSongLength);
        var first = line.OrderBy(m => FlatDistance(m.Position, caster.Position)).FirstOrDefault();
        greatWhirlwindSpots[spot] = first?.Position ?? target.Position;
        if (first != null && !IsTank(first)) first.Die("Died to Mistral Song (no tank intercepted it)");
    }

    private void CastGreatWhirlwind(GreatWhirlwindSpot spot)
    {
        if (!greatWhirlwindSpots.TryGetValue(spot, out var at)) return;
        if (!greatWhirlwindCasters.TryGetValue(spot, out var caster) || caster == null)
            greatWhirlwindCasters[spot] = caster = SpawnDummy(at);
        caster?.SetPosition(new Placement(at, 0f));
        CastSelf(caster, ActionId.GreatWhirlwind, 2.7f);
    }

    private void ResolveGreatWhirlwind(GreatWhirlwindSpot spot)
    {
        if (!greatWhirlwindSpots.TryGetValue(spot, out var at)) return;
        if (greatWhirlwindCasters.GetValueOrDefault(spot) is { } caster) PlayEffect(caster, ActionId.GreatWhirlwind, 2.1f);
        utils.ResolveSnapshot(party.Find.InsideCircle(at, GreatWhirlwindRadius).ToList(), "Great Whirlwind");
    }

    private void SpawnPlumes(IReadOnlyList<Vector3> satinSpots, bool withSpiny)
    {
        satinPlumes.Clear();
        foreach (var at in satinSpots)
            if (SpawnEnemy(BNpcBaseId.SatinPlume, BNpcNameId.SatinPlume, new Placement(at, 0f), true, false, EnemyListMode.Always) is { } plume)
                satinPlumes.Add(plume);
        if (withSpiny)
            spiny = SpawnEnemy(BNpcBaseId.SpinyPlume, BNpcNameId.SpinyPlume, new Placement(UwuP1GarudaState.SpinyPlumeSpawn, 0f), true, false, EnemyListMode.Always);
    }

    private void ShowPlumes()
    {
        foreach (var plume in satinPlumes) plume.SetVisible(true);
        spiny?.SetVisible(true);
    }

    private void DespawnSatinPlume(int index)
    {
        if (index < satinPlumes.Count) satinPlumes[index].Despawn();
    }

    private void FixateSpinyOnOffTank()
    {
        if (spiny == null || Get(PartyRole.OffTank) is not { } offTank) return;
        world.Tether(spiny, offTank, TetherId.SpinyFixate, 20f);
        spiny.MoveTo(new Vector3(-8f, 0f, 1.6f), 2f);
    }

    private void SpinyCyclone()
    {
        if (spiny == null || Get(PartyRole.OffTank) is not { } offTank || !offTank.IsAlive()) return;
        PlayEffect(spiny, ActionId.Cyclone, 1.1f, target: offTank.GameObjectId);
        AddThermalLow(offTank);
    }

    private void ResolveGigastorm()
    {
        if (spiny == null) return;
        PlayEffect(spiny, ActionId.Gigastorm, 2.1f);
        utils.ResolveSnapshot(party.Find.InsideCircle(spiny.Position, GigastormRadius).ToList(), "Gigastorm");
        spiny.Despawn();
    }

    private void SpawnBubble()
    {
        bubble = world.SpawnEventObject(new EventObjectSpawnConfig
        {
            EObjId = BubbleEObjId,
            Placement = new Placement(UwuP1GarudaState.GigastormSpot, 0f),
            TimelineState = 1,
            SpawnVisible = false,
        });
    }

    private void DeactivateBubble()
    {
        bubble?.SetState(0);
        bubbleActive = false;
    }

    private void CastFriction(PartyRole role)
    {
        if (Get(role) is not { } target) return;
        garuda?.NativeCast(ActionId.Friction, ActionType.Action, 0f, 1.7f, false, targetId: target.GameObjectId);
    }

    private void ResolveFriction(PartyRole role)
    {
        if (Get(role) is not { } target) return;
        PlayEffect(garuda, ActionId.Friction, 1.1f, target: target.GameObjectId);
        foreach (var hit in party.Find.InsideCircle(target.Position, FrictionRadius).ToList())
        {
            damage.ApplyDamage(hit, 0.15f, ActionId.Friction, "Friction", false);
            AddThermalLow(hit);
        }
    }

    private static void AddThermalLow(SimCharacter member)
    {
        if (member.FindStatus(StatusId.ThermalLow) is { Stacks: >= MaxThermalLow }) return;
        member.AddStatus(StatusId.ThermalLow);
    }

    // Cleansing two stacks at once is what charges Garuda toward waking; the explosion itself is a
    // light raidwide either way.
    private void CleanseThermalLow(SimCharacter member)
    {
        if (member.FindStatus(StatusId.ThermalLow) is not { } thermalLow) return;
        var awakening = thermalLow.Stacks >= MaxThermalLow;
        member.RemoveStatus(StatusId.ThermalLow);
        var caster = SpawnDummy(member.Position);
        PlayEffect(caster, awakening ? ActionId.SuperCycloneAwaken : ActionId.SuperCyclone, 1.1f);
        foreach (var hit in party.Find.InsideCircle(member.Position, 50f).ToList())
            damage.ApplyDamage(hit, 0.1f, ActionId.SuperCyclone, "Super Cyclone", false);
        if (!awakening) return;
        aetherialCharges++;
        garuda?.AddStatus(StatusId.AetheriallyCharged, 0f, 1);
        if (aetherialCharges == ChargesToWake) utils.Awaken(garuda, false);
    }

    private void ScheduleHazards()
    {
        for (var t = 43.5f; t <= 66.2f; t += HazardStep)
            world.Events.Add(t, CleanseInBubble);
    }

    private void CleanseInBubble()
    {
        if (!bubbleActive) return;
        for (var slot = 0; slot < 8; slot++)
        {
            if (party.Get(slot) is not { } member || !member.IsAlive()) continue;
            var inside = FlatDistance(member.Position, UwuP1GarudaState.GigastormSpot) <= UwuP1GarudaState.BubbleRadius;
            if (!inside || !member.HasStatus(StatusId.ThermalLow))
            {
                bubbleDwell.Remove(member);
                continue;
            }
            var dwell = bubbleDwell.GetValueOrDefault(member) + HazardStep;
            if (dwell < UwuP1GarudaState.BubbleCleanseSeconds)
            {
                bubbleDwell[member] = dwell;
                continue;
            }
            bubbleDwell.Remove(member);
            CleanseThermalLow(member);
        }
    }

    private void Raidwide(SimEnemy? caster, uint actionId, float fraction, float animationLock)
    {
        PlayEffect(caster, actionId, animationLock);
        for (var slot = 0; slot < 8; slot++)
            if (party.Get(slot) is { } member && member.IsAlive())
                damage.ApplyDamage(member, fraction, actionId, "Raidwide", false);
    }

    private void SpawnSisters()
    {
        suparna = SpawnEnemy(BNpcBaseId.SuparnaChirada, BNpcNameId.Suparna, new Placement(new Vector3(-6f, 0f, 0f), 0f), false, false, EnemyListMode.Never);
        chirada = SpawnEnemy(BNpcBaseId.SuparnaChirada, BNpcNameId.Chirada, new Placement(new Vector3(6f, 0f, 0f), 0f), false, false, EnemyListMode.Never);
    }

    private void PlaySisters(ushort timelineId)
    {
        suparna?.PlayActionTimeline(timelineId);
        chirada?.PlayActionTimeline(timelineId);
    }

    private void ShowSisters(bool visible)
    {
        suparna?.SetVisible(visible);
        chirada?.SetVisible(visible);
    }

    private void PlaceSisters(Vector3 suparnaAt, Vector3 chiradaAt)
    {
        suparna?.SetPosition(new Placement(suparnaAt, FacingCentre(suparnaAt)));
        chirada?.SetPosition(new Placement(chiradaAt, FacingCentre(chiradaAt)));
    }

    private static float FacingCentre(Vector3 from) => MathF.Atan2(-from.X, -from.Z);

    private void MarkSistersSongTargets()
    {
        foreach (var role in state.SistersSongTargets)
            Get(role)?.AttachLockonVfx(LockonId.MistralSong, 5.2f);
    }

    private void ResolveSistersSongs()
    {
        ResolveMistralSong(suparna, ActionId.MistralSongSuparnaChirada, Get(state.SistersSongTargets[0]), GreatWhirlwindSpot.Suparna);
        ResolveMistralSong(chirada, ActionId.MistralSongSuparnaChirada, Get(state.SistersSongTargets[1]), GreatWhirlwindSpot.Chirada);
    }

    private SimEnemy? eyeOfTheStorm;

    private void CastEyeOfTheStorm()
    {
        eyeOfTheStorm ??= SpawnDummy(Vector3.Zero);
        CastSelf(eyeOfTheStorm, ActionId.EyeOfTheStorm, 2.7f);
    }

    private void ResolveEyeOfTheStorm()
    {
        PlayEffect(eyeOfTheStorm, ActionId.EyeOfTheStorm, 2.1f);
        utils.ResolveSnapshot(party.Find.InsideRing(Vector3.Zero, EyeOfTheStormInner, EyeOfTheStormOuter).ToList(), "Eye of the Storm");
    }

    private void ResolveWickedWheel()
    {
        if (garuda == null) return;
        PlayEffect(garuda, ActionId.WickedWheelAwaken, 2.1f);
        utils.ResolveSnapshot(party.Find.InsideCircle(garuda.Position, WickedWheelRadius).ToList(), "Wicked Wheel");
    }

    private void TetherMesohigh()
    {
        var seeds = Enumerable.Range(0, 8).Select(party.Get).Where(m => m is { } c && c.IsAlive()).OrderBy(_ => Random.Shared.Next()).ToList();
        if (suparna != null && seeds.Count > 0)
            state.SuparnaMesohigh = world.Tether(suparna, End.Passable(seeds[0], PassableHalfWidth), TetherId.Mesohigh);
        if (chirada != null && seeds.Count > 1)
            state.ChiradaMesohigh = world.Tether(chirada, End.Passable(seeds[1], PassableHalfWidth), TetherId.Mesohigh);
    }

    // Mesohigh bursts on whoever holds each tether: anyone caught without Thermal Low dies, and it
    // cleanses those who had it.
    private void ResolveMesohigh()
    {
        foreach (var (sister, tether) in new[] { (suparna, state.SuparnaMesohigh), (chirada, state.ChiradaMesohigh) })
        {
            if (sister == null || tether?.B is not { } holder) continue;
            PlayEffect(sister, ActionId.Mesohigh, 1.1f, target: holder.GameObjectId);
            foreach (var hit in party.Find.InsideCircle(holder.Position, MesohighRadius).ToList())
            {
                if (hit.HasStatus(StatusId.ThermalLow)) CleanseThermalLow(hit);
                else hit.Die("Died to Mesohigh (took it without Thermal Low)");
            }
            tether.Despawn();
        }
        state.SuparnaMesohigh = null;
        state.ChiradaMesohigh = null;
    }

    private void KillGaruda()
    {
        garuda?.Despawn();
        garuda = null;
    }

    private void DespawnAll()
    {
        KillGaruda();
        suparna?.Despawn();
        chirada?.Despawn();
        spiny?.Despawn();
        bubble?.Despawn();
        foreach (var plume in satinPlumes) plume.Despawn();
        foreach (var helper in helpers) helper.Despawn();
        satinPlumes.Clear();
        helpers.Clear();
        greatWhirlwindCasters.Clear();
        greatWhirlwindSpots.Clear();
    }

    private static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
