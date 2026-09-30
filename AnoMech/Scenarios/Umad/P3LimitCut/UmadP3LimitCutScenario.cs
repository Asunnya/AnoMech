using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native;
using AnoMech.Core.SimObjects;
using AnoMech.Multiplayer;
using AnoMech.Scenarios.Umad.P3BlackHole;

namespace AnoMech.Scenarios.Umad.P3LimitCut;

using Constants = UmadP3LimitCutConstants;

// Dancing Mad P3 "Limit Cut" (BossMod's P3UltimaBlaster). Scenario time 0 is 8.0s before Chaos
// starts casting Umbra Smash, the earliest start inside this mechanic (the previous resolve is
// 9.2s before); every other timestamp is that cast start plus the replay-measured offset in
// Constants.Timing.
public sealed class UmadP3LimitCutScenario : IMultiplayerReplayable
{
    public string Name => "Limit Cut";
    public IPhase Phase => UmadZone.P3;
    public float BgmSecondsAtStart => Constants.BgmSecondsAtStart;
    public bool SupportsSolo => true;
    public bool SupportsMultiplayer => true;
    public uint? TankMaxHealth => UmadConstants.Tunables.RealTankMaxHealth;
    public IReadOnlyList<IScenarioAi> AiStrats => [new UmadP3LimitCutAi()];
    public void DrawSettings() => settingsWindow.Draw();
    public bool HasPerPlayerSettings => true;
    public void DrawPerPlayerSettings() => settingsWindow.DrawPerPlayer();
    public object SettingsOverrides => settingsWindow.Overrides;
    public IReadOnlyList<string> SettingsConflicts => settingsWindow.Overrides.Validate().Problems;
    public void DrawMultiplayerSettings() => settingsWindow.DrawThunderIIIPlan();

    private readonly UmadP3LimitCutSettingsWindow settingsWindow = new();
    private SimWorld world = null!;
    private SimParty party = null!;
    private UmadP3LimitCutState state = null!;
    private DamageSolver damage = null!;
    private readonly SimEnemy?[] cycloneHelpers = new SimEnemy?[8];
    private SimEnemy? thunderHelper;
    private SimEventObject? windCrystal;
    private Vector3 umbraImpact;
    private readonly List<SimCharacter> cycloneTargets = [];
    private readonly SimCharacter?[] chargeTargets = new SimCharacter?[8];

    public UmadP3LimitCutState? LastState { get; private set; }

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        party = world.Party;
        state = new UmadP3LimitCutState(party, settingsWindow.Overrides);
        LastState = state;
        damage = new DamageSolver(party);
        damage.SetStatuses(DamageType.Lightning, UmadConstants.StatusId.LightningResistanceDownII);
        damage.SetStatuses(DamageType.Magic, UmadConstants.StatusId.MagicVulnerabilityUp);
        damage.SetMitigableStatuses(DamageType.Wind, Constants.Damage.CycloneVulnMitigation, Constants.StatusId.WindResistanceDownII);
        cycloneTargets.Clear();
        Array.Clear(chargeTargets);
        DiagnosticLog.Info(
            $"[UmadP3LimitCut] Roll: clones start {Constants.Geometry.SpotName(state.StartSpot)} going {(state.Clockwise ? "clockwise" : "counter-clockwise")}, "
            + $"bosses held {Constants.Geometry.SpotName(state.BossSpot)}, bait {state.BaitRole}, numbers "
            + string.Join(" ", state.Numbers.Select((r, k) => $"{k + 1}={r}")) + ", winds "
            + string.Join(" ", state.Winds.Select(kv => $"{kv.Key}={kv.Value}")) + ".");

        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<UmadP3LimitCutState>)AiStrats[idx]).Run(state, world);

        world.EnforceArenaBoundary(Constants.Geometry.ArenaRadius, "Fell off the arena");
        SpawnActors();
        ApplyStartingStatuses();

        var u = Constants.Timing.UmbraCastAt;
        world.Events.Add(1.0f, () =>
        {
            state.Objects.Chaos?.Follow(party.Get(PartyRole.MainTank));
            state.Objects.Exdeath?.Follow(party.Get(PartyRole.OffTank));
        });
        foreach (var at in Constants.Timing.ChaosAutoAfterUmbra)
            world.Events.Add(u + at, () => state.Objects.Chaos?.Cast(UmadConstants.ActionId.AutoAttack1, castSeconds: 0f, targetId: party.Get(PartyRole.MainTank)?.GameObjectId, animationLock: Constants.AnimationLock.AutoAttack));
        foreach (var at in Constants.Timing.ExdeathAutoAfterUmbra)
            world.Events.Add(u + at, () => state.Objects.Exdeath?.Cast(UmadConstants.ActionId.AutoAttack2, castSeconds: 0f, targetId: party.Get(PartyRole.OffTank)?.GameObjectId, animationLock: Constants.AnimationLock.AutoAttack));
        // Kefka's second trance beat. The real packet refreshes the aura in place; AddStatusParam
        // adds a slot, so drop the first or Kefka wears two auras at once.
        world.Events.Add(6.75f, () =>
        {
            state.Objects.Kefka?.Cast(Constants.ActionId.RingOfFire, castSeconds: 0f, animationLock: Constants.AnimationLock.RingOfFire);
            state.Objects.Kefka?.RemoveStatus(Constants.StatusId.KefkaTrance);
            state.Objects.Kefka?.AddStatusParam(Constants.StatusId.KefkaTrance, 0x22B);
        });

        // Both bosses stand still for their casts, which lets the tanks step into the stack (at
        // every real wave both tanks were inside it, Exdeath frozen 5.5y further out).
        world.Events.Add(u - 0.2f, () => state.Objects.Chaos?.Follow());
        world.Events.Add(u, StartUmbraSmash);
        world.Events.Add(Constants.Timing.VacuumCastAt - 0.2f, () => state.Objects.Exdeath?.Follow());
        world.Events.Add(Constants.Timing.VacuumCastAt, () => state.Objects.Exdeath?.Cast(
            Constants.ActionId.VacuumWave, castSeconds: Constants.Timing.VacuumShownCast,
            fireDelay: Constants.Timing.VacuumResolveAfterUmbra - (Constants.Timing.VacuumCastAt - u) - Constants.Timing.VacuumShownCast,
            animationLock: Constants.AnimationLock.VacuumWave));
        world.Events.Add(u + Constants.Timing.VacuumResolveAfterUmbra + Constants.Timing.VacuumApplyDelay + 8 * Constants.Timing.VacuumApplyStagger + 0.3f,
            () => state.Objects.Exdeath?.Follow(party.Get(PartyRole.OffTank)));
        for (var k = 0; k < 8; k++)
        {
            var clone = k;
            world.Events.Add(u + Constants.Timing.PlacementAfterUmbra[k], () => PlaceClone(clone));
            world.Events.Add(u + Constants.Timing.AppearAfterUmbra[k], () => CloneAppear(clone));
        }
        world.Events.Add(u + Constants.Timing.UmbraResolveAfterCast, ResolveUmbraSmash);
        world.Events.Add(u + Constants.Timing.TankLimitBreakAfterUmbra, BotTankLimitBreak);
        world.Events.Add(u + Constants.Timing.ChaosLandsAfterCast, ChaosLands);
        world.Events.Add(u + Constants.Timing.VacuumResolveAfterUmbra, ResolveVacuumWave);
        world.Events.Add(u + Constants.Timing.IconsAfterUmbra, AttachNumbers);
        world.Events.Add(u + Constants.Timing.CyclonesAfterUmbra, ResolveCyclones);
        world.Events.Add(u + Constants.Timing.AetherlinkAfterUmbra, () =>
        {
            state.Objects.Chaos?.Cast(UmadConstants.ActionId.Aetherlink_Chaos, castSeconds: 0f, animationLock: Constants.AnimationLock.Aetherlink);
            state.Objects.Exdeath?.Cast(UmadConstants.ActionId.Aetherlink_Exdeath, castSeconds: 0f, animationLock: Constants.AnimationLock.Aetherlink);
        });
        for (var k = 0; k < 8; k++)
        {
            var clone = k;
            world.Events.Add(u + Constants.Timing.ChargeSetPosAfterUmbra[k], () => PrepareCharge(clone));
            world.Events.Add(u + Constants.Timing.ChargeAfterUmbra[k], () => ResolveCharge(clone));
        }
        world.Events.Add(u + Constants.Timing.ThunderCastAfterUmbra - 0.2f, () => state.Objects.Exdeath?.Follow());
        world.Events.Add(u + Constants.Timing.ThunderCastAfterUmbra, () => state.Objects.Exdeath?.Cast(
            UmadConstants.ActionId.ThunderIII_Cast, castSeconds: Constants.Timing.ThunderShownCast,
            fireDelay: Constants.Timing.ThunderHit1AfterUmbra - Constants.Timing.ThunderCastAfterUmbra - Constants.Timing.ThunderShownCast,
            animationLock: Constants.AnimationLock.ThunderCast));
        world.Events.Add(u + Constants.Timing.ThunderHit1AfterUmbra, () => ResolveThunder(1));
        world.Events.Add(u + Constants.Timing.ThunderHit2AfterUmbra, () => ResolveThunder(2));
        world.Events.Add(u + Constants.Timing.ThunderHit2AfterUmbra + 0.5f, () => state.Objects.Exdeath?.Follow(party.Get(PartyRole.OffTank)));
        world.Events.Add(u + Constants.Timing.DecisiveBattleCastAfterUmbra - 0.2f, () =>
        {
            state.Objects.Chaos?.Follow();
            state.Objects.Exdeath?.Follow();
        });
        world.Events.Add(u + Constants.Timing.DecisiveBattleCastAfterUmbra, () =>
        {
            var fire = Constants.Timing.DecisiveBattleResolveAfterUmbra - Constants.Timing.DecisiveBattleCastAfterUmbra - Constants.Timing.DecisiveBattleShownCast;
            state.Objects.Chaos?.Cast(Constants.ActionId.DecisiveBattleChaos, castSeconds: Constants.Timing.DecisiveBattleShownCast, fireDelay: fire, animationLock: Constants.AnimationLock.DecisiveBattle);
            state.Objects.Exdeath?.Cast(Constants.ActionId.DecisiveBattleExdeath, castSeconds: Constants.Timing.DecisiveBattleShownCast, fireDelay: fire, animationLock: Constants.AnimationLock.DecisiveBattle);
        });
        world.Events.Add(u + Constants.Timing.DecisiveBattleResolveAfterUmbra, ResolveDecisiveBattle);
        world.Events.Add(u + Constants.Timing.DecisiveBattleResolveAfterUmbra + 0.2f, () =>
        {
            state.Objects.Chaos?.Follow(party.Get(PartyRole.MainTank));
            state.Objects.Exdeath?.Follow(party.Get(PartyRole.OffTank));
        });
        world.Events.Add(u + Constants.Timing.KefkaReappearAfterUmbra, () => state.Objects.Kefka?.PlayAnimationTimeline(UmadConstants.TimelineId.Spawn));
    }

    // Scheduled by host and peer alike, so broadcast: false; a peer's own clones play the same
    // materialise timelines, so the preload belongs here too, and so does a tank's LB3 gauge.
    public void RunInstanceEvents(SimWorld instanceWorld)
    {
        ActionTimelinePreload.Preload(CloneTimelines, "UmadP3LimitCut");
        instanceWorld.SetLimitBreakGauge(3f);
        var u = Constants.Timing.UmbraCastAt;
        foreach (var (offset, arg) in Constants.Timing.DirectorBeats)
            instanceWorld.Events.Add(u + offset, () => instanceWorld.Map.DirectorUpdate(Constants.Timing.DirectorCategory, arg, 0x2U, Constants.Timing.DirectorArg3, Constants.Timing.DirectorKefkaId, broadcast: false));
    }

    public MpMessage? BuildReplayStateMessage()
        => LastState is { } s
            ? new UmadP3LimitCutAiReplayStateMessage(s.StartSpot, s.Clockwise, s.Numbers.ToArray(),
                s.Winds.Where(kv => kv.Value == Wind.Headwind).Select(kv => kv.Key).ToArray(), s.BossSpot, s.BaitRole, s.ThunderPlan)
            : null;

    public object? StartReplay(MpMessage message, int aiIndex, PartyRole myRole, SimWorld replayWorld)
    {
        if (message is not UmadP3LimitCutAiReplayStateMessage msg || aiIndex < 0 || aiIndex >= AiStrats.Count) return null;
        if (UmadP3LimitCutState.FromNetworkReplay(msg.StartSpot, msg.Clockwise, msg.Numbers, msg.Headwinds, msg.BossSpot, msg.BaitRole, msg.ThunderPlan) is not { } shadowState) return null;
        ((IScenarioAi<UmadP3LimitCutState>)AiStrats[aiIndex]).Run(shadowState, replayWorld);
        return shadowState;
    }

    // Chaos/Exdeath may not be replicated yet when StartReplay runs.
    public void RefreshLiveHandles(object shadowStateObj, IReadOnlyDictionary<int, SimEnemy> peerEnemies)
    {
        if (shadowStateObj is not UmadP3LimitCutState shadow) return;
        shadow.Objects.Chaos ??= peerEnemies.Values.FirstOrDefault(e => e.BNpcBaseId == UmadConstants.BNpcBaseId.ChaosP3);
        shadow.Objects.Exdeath ??= peerEnemies.Values.FirstOrDefault(e => e.BNpcBaseId == UmadConstants.BNpcBaseId.Exdeath);
    }

    // The "hide" set's mon_sp003/mon_sp004 materialise the clone out of its dissolve; a timeline
    // that never comes up leaves it frozen white and translucent. Both key spellings, as Flood needed.
    private static readonly (ushort Id, string Key)[] CloneTimelines =
    [
        (4575, "mon_sp/m0462/hide/mon_sp003"),
        (4576, "mon_sp/m0462/hide/mon_sp004"),
    ];

    private void SpawnActors()
    {
        var objects = state.Objects;
        var hold = Constants.Geometry.SpotHeading(state.BossSpot);
        objects.Kefka = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: UmadConstants.BNpcBaseId.KefkaP3, NameId: UmadConstants.BNpcNameId.Kefka, Level: 100,
            Targetable: false, EnemyList: EnemyListMode.Always, IsVisible: true,
            Placement: new Placement(Constants.Geometry.KefkaPerch, 0f)));
        objects.Chaos = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: UmadConstants.BNpcBaseId.ChaosP3, NameId: UmadConstants.BNpcNameId.Chaos, Level: 100,
            Targetable: true, EnemyList: EnemyListMode.Always, IsVisible: true,
            Placement: new Placement(Constants.Geometry.OnCircle(hold, Constants.Geometry.ChaosHoldRadius), hold + MathF.PI)));
        objects.Exdeath = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: UmadConstants.BNpcBaseId.Exdeath, NameId: UmadConstants.BNpcNameId.Exdeath, Level: 100,
            Targetable: true, EnemyList: EnemyListMode.Always, IsVisible: true,
            Placement: new Placement(
                Constants.Geometry.OnCircle(hold, Constants.Geometry.ExdeathHoldRadius) + Constants.Geometry.OnCircle(hold + MathF.PI / 2f, 1.6f),
                hold + MathF.PI)));
        // Built from a real clone's NpcSpawn packet: visible at the centre, hidden only by
        // animation state (0,1), as the real ones sit for 86s. Spawning them invisible and
        // flipping RenderFlags at placement showed a washed-out white Kefka.
        for (var k = 0; k < 8; k++)
            objects.Clones[k] = world.SpawnEnemy(new EnemySpawnConfig(
                BNpcBaseId: Constants.BNpcBaseId.KefkaClone, NameId: UmadConstants.BNpcNameId.Kefka, Level: 100,
                Targetable: false, EnemyList: EnemyListMode.Never, IsVisible: true,
                Placement: new Placement(Vector3.Zero, 0f),
                NpcSpawnTemplate: UmadRealPackets.CloneP3NpcSpawn, PacketSpawnEnableDraw: true));
        // Cyclone's caster-side VFX needs a real skeleton; the real 9020 helpers have none.
        for (var i = 0; i < 8; i++)
            cycloneHelpers[i] = world.SpawnEnemy(new EnemySpawnConfig(
                BNpcBaseId: UmadConstants.BNpcBaseId.Chaos, NameId: UmadConstants.BNpcNameId.Chaos, Level: 1,
                Targetable: false, EnemyList: EnemyListMode.Never, IsVisible: false,
                Placement: new Placement(Constants.Geometry.WindCrystal, 0f)));
        // The strike's VFX sits on the target, so the helper's model never matters.
        thunderHelper = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: UmadConstants.BNpcBaseId.KefkaHelper, NameId: UmadConstants.BNpcNameId.Exdeath, Level: 1,
            Targetable: false, EnemyList: EnemyListMode.Never, IsVisible: false,
            Placement: new Placement(Constants.Geometry.WindCrystal, 0f)));
        // The fire and water crystals faded when their elements resolved, before this window.
        windCrystal = world.SpawnEventObject(new EventObjectSpawnConfig { EObjId = Constants.EObjId.WindCrystal, Placement = new Placement(Constants.Geometry.WindCrystal, Constants.Geometry.WindCrystalRotation) });

        // Set once the draw objects exist.
        world.Events.Add(0.5f, () =>
        {
            objects.Kefka?.SetAnimationState(0, 1);
            foreach (var clone in objects.Clones) clone?.SetAnimationState(0, 1);
        });
    }

    // No Epic/Fated sides: stripped 44.3s before the Umbra cast, back with the Decisive Battle.
    private void ApplyStartingStatuses()
    {
        world.Events.Add(0.1f, () =>
        {
            state.Objects.Kefka?.AddStatusParam(Constants.StatusId.KefkaTrance, 0x1FF);
            foreach (var role in Enum.GetValues<PartyRole>())
            {
                if (party.Get(role) is not { } member) continue;
                member.AddStatus(state.Winds[role] == Wind.Headwind ? Constants.StatusId.Headwind : Constants.StatusId.Tailwind,
                    Constants.Timing.WindRemainingAtStart);
            }
        });
    }

    // Black Hole's rules: whoever is closest to Exdeath, a tank buster, the second hit lethal
    // short of an invuln while Lightning Resistance Down II is still up.
    private void ResolveThunder(int hitNumber)
    {
        if (state.Objects.Exdeath is not { } exdeath) return;
        var target = party.Find.Closest(exdeath.Position);
        thunderHelper?.Cast(UmadConstants.ActionId.ThunderIII_Resolve, castSeconds: 0f, targetId: target?.GameObjectId, animationLock: Constants.AnimationLock.ThunderHit);
        damage.Resolve(target, UmadConstants.ActionId.ThunderIII_Resolve, [DamageType.TankBuster, DamageType.Magic, DamageType.Lightning],
            [(UmadConstants.StatusId.LightningResistanceDownII, Constants.Damage.LightningResistanceDownSeconds)],
            requiredMitigation: Constants.Damage.ThunderIIIRequiredMitigation);
    }

    private void ResolveDecisiveBattle()
    {
        state.Objects.Chaos?.AddStatus(UmadConstants.StatusId.EpicVillain);
        state.Objects.Exdeath?.AddStatus(UmadConstants.StatusId.FatedVillain);
        foreach (var role in Enum.GetValues<PartyRole>())
            if (party.Get(role) is { } member && member.IsAlive())
                member.AddStatus(role is PartyRole.MainTank or PartyRole.RegenHealer or PartyRole.MeleeDpsA or PartyRole.MeleeDpsB
                    ? UmadConstants.StatusId.EpicHero : UmadConstants.StatusId.FatedHero);
    }

    // A survivable hit, shown after the member's own active mitigation, so the LB3 at 13.6s makes
    // the appearance raidwides read as the real 80%-cut numbers.
    private void Hit(SimCharacter member, float fraction, uint actionId, string context)
        => damage.ApplyDamage(member, fraction * (1f - damage.EffectiveMitigation(member)), actionId, context, lethal: false);

    private void StartUmbraSmash()
    {
        if (state.Objects.Chaos is not { } chaos) return;
        var bait = party.Find.Farest(chaos.Position);
        umbraImpact = bait?.Position ?? chaos.Position;
        DiagnosticLog.Info($"[UmadP3LimitCut] Umbra Smash: bait {(bait as ISimPartyMember)?.Role.ToString() ?? "none"} at ({umbraImpact.X:F1},{umbraImpact.Z:F1}), {Vector3.Distance(umbraImpact, chaos.Position):F1}y from Chaos.");
        chaos.Cast(Constants.ActionId.UmbraSmash, targetLocation: umbraImpact, castSeconds: Constants.Timing.UmbraShownCast,
            fireDelay: Constants.Timing.UmbraResolveAfterCast - Constants.Timing.UmbraShownCast, animationLock: Constants.AnimationLock.UmbraSmash);
    }

    private void ResolveUmbraSmash()
        => damage.Resolve(IPositioned.From(umbraImpact), Constants.ActionId.UmbraSmash, [], [],
            lethalWithin: Constants.Geometry.UmbraLethalRadius);

    // Skipped by default when the human is a tank, so the press is theirs to make.
    private void BotTankLimitBreak()
    {
        if (settingsWindow.Overrides.BotTankLimitBreak ?? !party.PlayerRole.IsTank())
            party.BotTankLimitBreak();
    }

    private void ChaosLands()
    {
        if (state.Objects.Chaos is not { } chaos) return;
        chaos.SetPosition(new Placement(umbraImpact, chaos.Rotation));
        chaos.Follow(party.Get(PartyRole.MainTank));
    }

    // The server applies the pushes 0.80s after the wave, one player every 0.045s.
    private void ResolveVacuumWave()
    {
        if (state.Objects.Exdeath is not { } exdeath) return;
        var source = exdeath.Position;
        cycloneTargets.Clear();
        var members = party.ActiveMembers().Where(m => m.IsAlive()).ToList();
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            world.Events.Add(Constants.Timing.VacuumApplyDelay + i * Constants.Timing.VacuumApplyStagger, () => PushByWind(member, source));
        }
    }

    // Headwind faces away from Exdeath, Tailwind toward him. Within 45 deg of that is the 10y
    // push; anything else is 40y straight off the arena (the two real players who faced wrong
    // died as environment kills; Stray Gusts was never cast). No wind left: the plain 20y.
    private void PushByWind(SimCharacter member, Vector3 source)
    {
        if (member is not ISimPartyMember pm || !member.IsAlive()) return;
        var hasHeadwind = member.HasStatus(Constants.StatusId.Headwind);
        var hasTailwind = member.HasStatus(Constants.StatusId.Tailwind);
        var distance = 20f;
        var offDegrees = 0f;
        if (hasHeadwind || hasTailwind)
        {
            cycloneTargets.Add(member);
            member.RemoveStatus(hasHeadwind ? Constants.StatusId.Headwind : Constants.StatusId.Tailwind);
            var away = MathF.Atan2(member.Position.X - source.X, member.Position.Z - source.Z);
            var facingAway = MathF.Cos(member.Rotation - away);
            var towardSafe = hasHeadwind ? facingAway : -facingAway;
            offDegrees = MathF.Acos(Math.Clamp(towardSafe, -1f, 1f)) * (180f / MathF.PI);
            distance = towardSafe > Constants.Geometry.CorrectFacingCos ? 10f : 40f;
        }
        DiagnosticLog.Info($"[UmadP3LimitCut] Vacuum Wave: {pm.Role} {(hasHeadwind ? "Headwind" : hasTailwind ? "Tailwind" : "no wind")} facing {offDegrees:F0} deg off -> {distance:F0}y.");
        pm.Knockback(source, distance, Constants.Timing.KnockbackSpeed);
        if (distance < 40f) return;
        // Dead where the push crosses the arena edge, before the boundary fence names it a fall.
        var p = new Vector2(member.Position.X, member.Position.Z);
        var along = Vector2.Normalize(p - new Vector2(source.X, source.Z));
        var pu = Vector2.Dot(p, along);
        var toEdge = -pu + MathF.Sqrt(MathF.Max(0f, pu * pu - p.LengthSquared() + Constants.Geometry.ArenaRadius * Constants.Geometry.ArenaRadius));
        var wind = hasHeadwind ? "Headwind faces away from Exdeath" : "Tailwind faces toward Exdeath";
        world.Events.Add(MathF.Max(0f, toEdge / Constants.Timing.KnockbackSpeed - 0.05f),
            () => member.Die($"Died to Vacuum Wave (faced {offDegrees:F0} deg off, {wind}: pushed 40y off the arena)"));
    }

    private void PlaceClone(int k)
    {
        if (state.Objects.Clones[k] is not { } clone) return;
        var spot = state.PlacementSpot(k);
        clone.SetPosition(new Placement(Constants.Geometry.SpotPosition(spot), Constants.Geometry.SpotHeading(spot) + MathF.PI));
        clone.SetVisible(true);
        if (k == 0) state.Objects.Kefka?.SetAnimationState(0, 0);
    }

    private void CloneAppear(int k)
    {
        if (state.Objects.Clones[k] is not { } clone) return;
        clone.Cast(Constants.ActionId.UltimaBlaster, castSeconds: 0f, animationLock: Constants.AnimationLock.CloneAppear);
        foreach (var member in party.ActiveMembers().ToList())
            Hit(member, Constants.Damage.CloneAppear, Constants.ActionId.UltimaBlaster, "raidwide");
    }

    private void AttachNumbers()
    {
        for (var k = 0; k < 8; k++)
        {
            if (party.Get(state.Numbers[k]) is not { } member || !member.IsAlive()) continue;
            member.AttachLockonVfx(Constants.BlasterLockons[k],
                duration: Constants.Timing.ChargeAfterUmbra[k] - Constants.Timing.IconsAfterUmbra, persistent: false);
        }
    }

    // One Cyclone per player that carried a wind into the knockback: a two-person stack that
    // only a tank on cooldowns survives alone, each leaving a vuln the next one lands on.
    private void ResolveCyclones()
    {
        windCrystal?.FadeOut();
        var centres = cycloneTargets.Where(t => t.IsAlive()).ToList();
        for (var i = 0; i < centres.Count; i++)
        {
            cycloneHelpers[i % cycloneHelpers.Length]?.Cast(UmadConstants.ActionId.Cyclone, castSeconds: 0f, targetId: centres[i].GameObjectId, animationLock: Constants.AnimationLock.Cyclone);
            damage.Resolve(centres[i], UmadConstants.ActionId.Cyclone, [DamageType.Wind],
                [(Constants.StatusId.WindResistanceDownII, Constants.Damage.WindResistanceDownSeconds)],
                stackMinTargets: 2, understackedTankMitigation: Constants.Damage.CycloneSoloTankMitigation);
        }
    }

    // A dead number's clone still charges someone; none of the 8 real retargets fit a rule
    // (nearest, farthest, next slot, next number, enmity, or excluding a vuln carrier), so
    // uniformly random.
    private SimCharacter? ChargeTarget(int k)
    {
        if (party.Get(state.Numbers[k]) is { } numbered && numbered.IsAlive()) return numbered;
        var alive = party.ActiveMembers().Where(m => m.IsAlive()).ToArray();
        return alive.Length == 0 ? null : state.PickRandom(alive);
    }

    private void PrepareCharge(int k)
    {
        if (state.Objects.Clones[k] is not { } clone) return;
        var target = ChargeTarget(k);
        chargeTargets[k] = target;
        var spot = Constants.Geometry.SpotPosition(state.ChargeSpot(k));
        var facing = target != null ? MathF.Atan2(target.Position.X - spot.X, target.Position.Z - spot.Z) : Constants.Geometry.SpotHeading(state.ChargeSpot(k)) + MathF.PI;
        clone.SetPosition(new Placement(spot, facing));
    }

    // Aimed at the target wherever they stand, so a misplaced number drags the rect across
    // whoever is between; a second hit inside Magic Vulnerability Up is a death at any distance.
    private void ResolveCharge(int k)
    {
        if (state.Objects.Clones[k] is not { } clone) return;
        var target = chargeTargets[k] is { } t && t.IsAlive() ? t : ChargeTarget(k);
        if (target != null) clone.Face(target.Position);
        clone.Cast(Constants.ActionId.UltimaBlasterCharge, castSeconds: 0f, animationLock: Constants.AnimationLock.CloneCharge);
        damage.Resolve(clone, Constants.ActionId.UltimaBlasterCharge, [DamageType.Magic],
            [(UmadConstants.StatusId.MagicVulnerabilityUp, Constants.Damage.MagicVulnerabilityUpSeconds)],
            lethalWithin: Constants.Damage.ChargeLethalRange);
    }
}
