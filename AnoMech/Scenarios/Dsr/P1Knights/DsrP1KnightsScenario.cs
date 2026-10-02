using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Network;
using static AnoMech.Scenarios.Dsr.DsrConstants;

namespace AnoMech.Scenarios.Dsr.P1Knights;

// From the pull until Thordan arrives for phase 2; the knights' HP push is scripted.
// UNVERIFIED: the third Holiest Hallowing (104.27) and everything after it are 7s later than the
// log, which killed the knights before that cast; 104.27 keeps the 18s cadence of the first two.
public sealed class DsrP1KnightsScenario : IScenario
{
    public string Name => "Knights";
    public IPhase Phase => DsrZone.Knights;

    public IReadOnlyList<IScenarioAi> AiStrats => [new DsrP1KnightsAi()];

    private const float HoliestOfHolyDamage = 0.55f;
    private const float HeavensblazeDamage = 0.5f;
    private const float SlashDamage = 0.45f;
    private const float SlashVulnSeconds = 3.96f;
    private const float SlashConeHalfAngle = MathF.PI / 4f;
    private const float SlashConeLength = 40f;
    private const int SlashConeMinTargets = 3;
    private const int SlashCasterCount = 5;
    private const float SlashCasterLifetime = 3f;
    private const float FaithUnmovingDamage = 0.1f;
    private const float HeavensflameDamage = 0.35f;
    private const float FireResistanceDownSeconds = 2.96f;
    private const float PureOfHeartDamage = 0.7f;
    private const float HolyChainDamage = 0.8f;
    private const float DamageDownSeconds = 180f;
    private const float ShockwaveDamage = 0.05f;
    private const float BrightwingDamage = 0.5f;
    private const float BrightwingHalfAngle = MathF.PI / 12f;
    private const float LightResistanceDownSeconds = 17.96f;
    private const float SkyblindSeconds = 5f;
    private const float SkyblindRadius = 3f;
    private const float ExecutionRadius = 5f;
    private const float ShiningBladeHalfWidth = 3f;
    private const float ShiningBladeLandDelay = 0.35f;
    private const float BrightFlareRadius = 9f;
    private const float BrightFlareDelay = 1.15f;
    private const float AntiKnockbackSeconds = 6f;
    private const float BotInterruptDelay = 1.6f;
    // Despawning an effect's caster cuts its VFX short.
    private const float EffectCarrierLinger = 3f;
    private const float HallowingCastSeconds = 3.7f;
    private const ushort HeavensblazeMinTargets = 4;
    // UNVERIFIED: none of these were measured; picked inside what the strat's spacing allows.
    private const float EmptyDimensionInnerRadius = 6f;
    private const float PortalTetherRange = 5f;
    private const float PortalPairRange = 8f;
    private const float PortalBrightsphereRange = 7f;
    private const float ChainBreakDistance = 30f;
    private const float PrisonRadius = 9f;

    private const uint KnightMaxHp = 3337516;
    private const uint CharibertMaxHp = 2333424;
    private const uint ZephirinMaxHp = 6920000;
    private const uint ThordanMaxHp = 7439000;

    private const uint CancelCastControl = 15;
    private const uint CancelCastReason = 540;

    private SimWorld world = null!;
    private SimParty party = null!;
    private DamageSolver damage = null!;
    private DsrP1KnightsState state = null!;

    private SimEnemy? adelphel;
    private SimEnemy? grinnaux;
    private SimEnemy? charibert;
    private SimEnemy? charibertCaster;
    private SimEnemy? zephirin;
    private SimEnemy? haurchefant;
    private SimEnemy? spear;
    private SimEnemy? thordan;
    private SimEventObject? prisonCircle;
    private readonly List<SimEnemy> helpers = [];
    private readonly List<SimEnemy?> slashCasters = [];
    private readonly List<(SimEnemy? Tear, Vector3 At)> portals = [];
    private readonly List<SimTether> burningChains = [];
    private readonly HashSet<SimCharacter> chainBurned = [];
    private bool portalsOpen;
    private bool shieldBashLocked;
    private bool prisonActive;
    private int hallowingCast;
    private bool hallowingInterrupted;
    private bool hallowingCasting;
    private float playerAntiKnockbackUntil = -1f;

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        party = world.Party;
        damage = new DamageSolver(party);
        state = new DsrP1KnightsState(world.Rng);
        helpers.Clear();
        portals.Clear();
        burningChains.Clear();
        chainBurned.Clear();
        portalsOpen = false;
        shieldBashLocked = false;
        prisonActive = false;
        hallowingCast = 0;
        hallowingInterrupted = false;
        hallowingCasting = false;
        playerAntiKnockbackUntil = -1f;

        Plugin.PlayerInputHooks.ActionExecuted -= OnPlayerAction;
        Plugin.PlayerInputHooks.ActionExecuted += OnPlayerAction;

        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<DsrP1KnightsState>)AiStrats[idx]).Run(state, world);

        world.Events.Add(0f, () => world.Map.DirectorUpdate(ArenaDirector.Layout, 0U, ArenaDirector.KnightsLayout));
        world.Events.Add(0f, () => world.Map.DirectorUpdate(ArenaDirector.MapChange, ArenaDirector.KnightsMap));
        world.Events.Add(0f, SpawnKnights);
        world.Events.Add(2f, () => world.Map.LogArena("DSR P1 start"));
        world.Events.Add(2.14f, () => CastSelf(adelphel, ActionId.HoliestOfHoly, 3.7f));
        world.Events.Add(6.10f, () => Raidwide(adelphel, ActionId.HoliestOfHoly, HoliestOfHolyDamage));

        world.Events.Add(14.17f, TetherShieldBash);
        world.Events.Add(14.26f, () => CastSelf(grinnaux, ActionId.EmptyDimension, 4.7f));
        world.Events.Add(19.25f, () => ResolveDimension(Dimension.Empty));
        world.Events.Add(19.29f, CastHeavensblaze);
        world.Events.Add(19.29f, ResolveHolyShieldBash);
        world.Events.Add(22.32f, ResolveHolyBladedance);
        world.Events.Add(24.28f, ResolveHeavensblaze);

        world.Events.Add(34.62f, AdelphelLeaves);
        world.Events.Add(34.62f, () => MarkSlashPrey(state.FirstSlashTargets));
        world.Events.Add(34.71f, () => CastSelf(grinnaux, ActionId.HyperdimensionalSlash, 4.7f));
        world.Events.Add(40.48f, SpawnSlashCasters);
        world.Events.Add(40.86f, () => ResolveSlashes(state.FirstSlashTargets));
        world.Events.Add(41.66f, () => MarkSlashPrey(state.SecondSlashTargets));
        world.Events.Add(47.56f, SpawnSlashCasters);
        world.Events.Add(47.94f, () => ResolveSlashes(state.SecondSlashTargets));

        world.Events.Add(50.71f, AdelphelLands);
        world.Events.Add(51.86f, () => CastSelf(grinnaux, ActionId.FaithUnmoving, 3.7f));
        world.Events.Add(52.93f, () => CastSelf(adelphel, ActionId.HoliestOfHoly, 3.7f));
        world.Events.Add(55.83f, () => ResolveFaithUnmoving(botsResist: true));
        world.Events.Add(56.90f, () => Raidwide(adelphel, ActionId.HoliestOfHoly, HoliestOfHolyDamage));
        ScheduleShiningBlade([59.09f, 60.15f, 61.22f, 62.29f]);
        world.Events.Add(64.83f, ResolveExecution);
        world.Events.Add(67.74f, ClosePortals);
        world.Events.Add(68.17f, StartHoliestHallowing);
        world.Events.Add(70.50f, () => adelphel?.MoveTo(new Vector3(-0.6f, 0f, 0.3f), 4f, MathF.PI));

        world.Events.Add(76.90f, MarkChainSymbols);
        world.Events.Add(76.99f, () => CastSelf(charibertCaster, ActionId.Heavensflame, 6.7f));
        world.Events.Add(78.02f, () => CastSelf(grinnaux, ActionId.FaithUnmoving, 3.7f));
        world.Events.Add(80.92f, TetherBurningChains);
        world.Events.Add(81.98f, () => ResolveFaithUnmoving(botsResist: false));
        world.Events.Add(84.04f, ResolveHolyChain);
        world.Events.Add(84.61f, ResolveHeavensflame);
        world.Events.Add(86.22f, StartHoliestHallowing);

        world.Events.Add(94.19f, () => CastSelf(adelphel, ActionId.HoliestOfHoly, 3.7f));
        world.Events.Add(95.21f, () => CastSelf(grinnaux, DimensionAction(state.SecondDimension), 4.7f));
        world.Events.Add(98.17f, () => Raidwide(adelphel, ActionId.HoliestOfHoly, HoliestOfHolyDamage));
        world.Events.Add(100.18f, () => ResolveDimension(state.SecondDimension));

        world.Events.Add(104.27f, StartHoliestHallowing);

        world.Events.Add(108.70f, () => world.Map.DirectorUpdate(ArenaDirector.Layout, 0U, ArenaDirector.PrisonLayout));
        world.Events.Add(108.70f, KnightsFall);
        world.Events.Add(110.08f, KnightsRegroupWest);
        world.Events.Add(110.08f, () => zephirin = SpawnKnight(BNpcBaseId.Zephirin, BNpcNameId.Zephirin, ZephirinMaxHp, new Placement(new Vector3(Geometry.ArenaHalfWidth, 0f, 0f), -MathF.PI / 2f), false, EnemyListMode.Never));
        world.Events.Add(112.31f, ResolvePlanarPrison);
        world.Events.Add(112.31f, ZephirinThrowsSpear);
        world.Events.Add(112.62f, SpawnPrisonCircle);
        world.Events.Add(112.84f, () => charibert?.NativeCast(ActionId.PureOfHeart, ActionType.Action, 0f, 35.2f, false, targetId: charibert.GameObjectId));
        world.Events.Add(115.28f, () => prisonCircle?.SetState(1));
        world.Events.Add(121.39f, HaurchefantArrives);
        world.Events.Add(122.28f, () => PlayEffect(zephirin, ActionId.SpearOfTheFury, 1.1f, target: haurchefant?.GameObjectId));
        world.Events.Add(122.30f, SpawnSpear);
        for (var pulse = 0; pulse < 7; pulse++)
            world.Events.Add(124.11f + pulse * 1.07f, Shockwave);
        foreach (var bait in new[] { 128.16f, 133.20f, 138.23f, 143.27f })
            world.Events.Add(bait, ResolveBrightwing);
        world.Events.Add(136.82f, () => haurchefant?.Despawn());
        world.Events.Add(141.58f, () => world.Map.DirectorUpdate(ArenaDirector.Music, ArenaDirector.ThordanMusic));
        world.Events.Add(145.32f, ReleasePrison);
        world.Events.Add(147.64f, () => prisonCircle?.SetState(0));
        world.Events.Add(148.14f, () => prisonCircle?.Despawn());
        world.Events.Add(148.31f, () => Raidwide(charibert, ActionId.PureOfHeart, PureOfHeartDamage));
        world.Events.Add(149.40f, KnightsDepart);
        world.Events.Add(151.38f, ThordanArrives);
        world.Events.Add(151.38f, () => world.PlaceWaymarks(NaurWaymarks));
        world.Events.Add(151.51f, () => world.Map.DirectorUpdate(ArenaDirector.Layout, 0U, ArenaDirector.ThordanLayout));
        world.Events.Add(151.51f, () => world.Map.DirectorUpdate(ArenaDirector.MapChange, ArenaDirector.ThordanMap));
        world.Events.Add(159.00f, DespawnAll);
    }

    public void Tick(float delta, float elapsed)
    {
        if (grinnaux is { Targetable: true, IsMoving: false, IsCasting: false } boss && party.Get(PartyRole.OffTank) is { } offTank && offTank.IsAlive())
            boss.Face(offTank);
        LockShieldBashOnTank();
        if (portalsOpen) TetherToPortals();
        BreakStretchedChains();
        if (prisonActive) KillPrisonEscapees();
    }

    private SimEnemy? SpawnEnemy(uint baseId, uint nameId, Placement placement, bool targetable, bool visible, EnemyListMode enemyList) =>
        world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: baseId,
            NameId: nameId,
            Level: Level,
            Targetable: targetable,
            EnemyList: enemyList,
            IsVisible: visible,
            Placement: placement));

    private SimEnemy? SpawnKnight(uint baseId, uint nameId, uint maxHp, Placement placement, bool targetable, EnemyListMode enemyList) =>
        world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: baseId,
            NameId: nameId,
            Level: Level,
            Targetable: targetable,
            EnemyList: enemyList,
            Placement: placement,
            NpcSpawnTemplate: DsrNpcSpawn.Build(baseId, nameId, maxHp)));

    private SimEnemy? SpawnHelper(Vector3 position, float rotation = 0f)
    {
        var helper = SpawnEnemy(BNpcBaseId.Dummy, BNpcNameId.Dummy, new Placement(position, rotation), false, true, EnemyListMode.Never);
        if (helper != null) helpers.Add(helper);
        return helper;
    }

    private static void CastSelf(SimEnemy? caster, uint actionId, float castSeconds) =>
        caster?.NativeCast(actionId, ActionType.Action, 0f, castSeconds, false, targetId: caster.GameObjectId);

    // An effect without a position plays at the arena centre, so default it to the caster.
    private static void PlayEffect(SimEnemy? caster, uint actionId, float animationLock, float? rotation = null, GameObjectId? target = null, Vector3? at = null) =>
        caster?.NativeActionEffect(actionId, animationLock, (ushort)actionId, 0, ActionType.Action, 0,
            rotation: rotation, position: at ?? caster.Position, animationTargetId: target ?? caster.GameObjectId);

    private static float RotationTowards(Vector3 from, Vector3 to) => MathF.Atan2(to.X - from.X, to.Z - from.Z);

    private static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    private IEnumerable<SimCharacter> AliveMembers()
    {
        for (var slot = 0; slot < 8; slot++)
            if (party.Get(slot) is { } member && member.IsAlive())
                yield return member;
    }

    private void Raidwide(SimEnemy? caster, uint actionId, float fraction)
    {
        PlayEffect(caster, actionId, 2.1f);
        foreach (var member in AliveMembers().ToList())
            damage.ApplyDamage(member, fraction, actionId, "Raidwide", false);
    }

    private void SpawnKnights()
    {
        grinnaux = SpawnKnight(BNpcBaseId.Grinnaux, BNpcNameId.Grinnaux, KnightMaxHp, new Placement(Vector3.Zero, MathF.PI), true, EnemyListMode.Always);
        adelphel = SpawnKnight(BNpcBaseId.Adelphel, BNpcNameId.Adelphel, KnightMaxHp, new Placement(new Vector3(-0.6f, 0f, 0.3f), MathF.PI), true, EnemyListMode.Always);
        charibertCaster = SpawnHelper(new Vector3(0f, 0f, -30f));
    }

    private void TetherShieldBash()
    {
        if (adelphel == null) return;
        state.ShieldBashTether = world.Tether(adelphel, End.Passable(party.Get(state.ShieldBashSeed)), TetherId.HolyShieldBash);
    }

    // Bots crossing the beam would otherwise pass it on after the tank has it.
    private void LockShieldBashOnTank()
    {
        if (shieldBashLocked || adelphel == null || state.ShieldBashTether is not { IsActive: true } tether) return;
        if (tether.B is not ISimPartyMember { Role: PartyRole.MainTank or PartyRole.OffTank } tank) return;
        tether.Despawn();
        state.ShieldBashTether = world.Tether(adelphel, (SimCharacter)tank, TetherId.HolyShieldBash);
        shieldBashLocked = true;
    }

    private void ResolveHolyShieldBash()
    {
        if (adelphel == null || state.ShieldBashTether?.B is not { } holder) return;
        PlayEffect(adelphel, ActionId.HolyShieldBash, 2.1f, RotationTowards(adelphel.Position, holder.Position), holder.GameObjectId);
        holder.AddStatus(StatusId.DownForTheCount, 5.96f);
        if (holder is ISimPartyMember { Role: PartyRole.MainTank or PartyRole.OffTank } tank && party.IsBotDriven(holder))
            tank.UseInvuln();
        state.ShieldBashTether.Despawn();
    }

    private void ResolveHolyBladedance()
    {
        if (adelphel == null || state.ShieldBashTether?.B is not { } holder || !holder.IsAlive()) return;
        PlayEffect(adelphel, ActionId.HolyBladedance, 2.1f, RotationTowards(adelphel.Position, holder.Position), holder.GameObjectId);
        damage.ApplyDamage(holder, 1f, ActionId.HolyBladedance, "needs an invuln", lethal: true);
        state.ShieldBashTether = null;
    }

    private Vector3 heavensblazeAt;

    private void CastHeavensblaze()
    {
        if (party.Get(state.HeavensblazeTarget) is not { } target) return;
        charibertCaster?.NativeCast(ActionId.Heavensblaze, ActionType.Action, 0f, 4.7f, false, targetId: target.GameObjectId);
    }

    private void ResolveHeavensblaze()
    {
        if (party.Get(state.HeavensblazeTarget) is not { } target) return;
        heavensblazeAt = target.Position;
        PlayEffect(charibertCaster, ActionId.Heavensblaze, 2.1f, target: target.GameObjectId, at: heavensblazeAt);
        var hits = damage.Resolve(IPositioned.From(heavensblazeAt), ActionId.Heavensblaze, [DamageType.Magic], [], stackMinTargets: HeavensblazeMinTargets);
        foreach (var hit in hits.Where(h => h.IsAlive()))
            damage.ApplyDamage(hit, HeavensblazeDamage, ActionId.Heavensblaze, "Heavensblaze", false);
    }

    private static uint DimensionAction(Dimension dimension) => dimension == Dimension.Empty ? ActionId.EmptyDimension : ActionId.FullDimension;

    private void ResolveDimension(Dimension dimension)
    {
        if (grinnaux == null) return;
        var actionId = DimensionAction(dimension);
        PlayEffect(grinnaux, actionId, 2.1f);
        damage.Resolve(grinnaux, actionId, [DamageType.Lethal], [], size: dimension == Dimension.Empty ? EmptyDimensionInnerRadius : null);
    }

    private void AdelphelLeaves()
    {
        adelphel?.Despawn();
        adelphel = null;
    }

    private void MarkSlashPrey(IReadOnlyList<PartyRole> prey)
    {
        foreach (var role in prey)
            party.Get(role)?.AttachLockonVfx(LockonId.HyperdimensionalSlash, 6f, persistent: false);
    }

    private void SpawnSlashCasters()
    {
        slashCasters.Clear();
        var origin = grinnaux?.Position ?? Vector3.Zero;
        for (var i = 0; i < SlashCasterCount; i++)
        {
            var caster = SpawnEnemy(BNpcBaseId.Dummy, BNpcNameId.Grinnaux, new Placement(origin, 0f), false, true, EnemyListMode.Never);
            if (caster != null) helpers.Add(caster);
            slashCasters.Add(caster);
        }
        var spawned = slashCasters.ToList();
        world.Events.Add(SlashCasterLifetime, () =>
        {
            foreach (var caster in spawned) caster?.Despawn();
        });
    }

    private void ResolveSlashes(IReadOnlyList<PartyRole> prey)
    {
        if (grinnaux == null) return;
        var origin = grinnaux.Position;
        var caster = 0;
        var hitBy = new Dictionary<SimCharacter, int>();
        var newPortals = new List<Vector3>();
        foreach (var role in prey)
        {
            if (party.Get(role) is not { } target || !target.IsAlive()) continue;
            var rotation = RotationTowards(origin, target.Position);
            PlayEffect(NextSlashCaster(ref caster, rotation), ActionId.HyperdimensionalSlashLine, 1.1f, rotation, target.GameObjectId);
            foreach (var hit in party.Find.InsideActionAoe(ActionId.HyperdimensionalSlashLine, new Placement(origin, rotation)))
                hitBy[hit] = hitBy.GetValueOrDefault(hit) + 1;
            newPortals.Add(EdgePoint(origin, target.Position));
        }

        var stackers = AliveMembers().Where(m => m is ISimPartyMember member && !prey.Contains(member.Role)).ToList();
        if (stackers.Count > 0)
        {
            var bait = stackers[world.Rng.Next(stackers.Count)];
            var coneRotation = RotationTowards(origin, bait.Position);
            PlayEffect(NextSlashCaster(ref caster, coneRotation), ActionId.HyperdimensionalSlashCone, 1.1f, coneRotation, bait.GameObjectId);
            var shared = party.Find.InsideCone(new Placement(origin, coneRotation), SlashConeHalfAngle, SlashConeLength);
            foreach (var hit in shared)
            {
                if (shared.Count < SlashConeMinTargets)
                    damage.ApplyDamage(hit, 1f, ActionId.HyperdimensionalSlashCone, $"{shared.Count}/{SlashConeMinTargets} in the shared cone", lethal: true);
                else
                    hitBy[hit] = hitBy.GetValueOrDefault(hit) + 1;
            }
        }

        foreach (var (hit, count) in hitBy)
        {
            if (!hit.IsAlive()) continue;
            if (count > 1 || hit.HasStatus(StatusId.MagicVulnerabilityUp))
            {
                damage.ApplyDamage(hit, 1f, ActionId.HyperdimensionalSlashLine, "took two slashes", lethal: true);
                continue;
            }
            damage.ApplyDamage(hit, SlashDamage, ActionId.HyperdimensionalSlashLine, "Hyperdimensional Slash", false);
            hit.AddStatus(StatusId.MagicVulnerabilityUp, SlashVulnSeconds);
        }

        world.Events.Add(0.6f, () => OpenPortals(newPortals));
    }

    private SimEnemy? NextSlashCaster(ref int index, float rotation)
    {
        var caster = index < slashCasters.Count ? slashCasters[index] : grinnaux;
        index++;
        caster?.SetRotation(rotation);
        return caster;
    }

    private static Vector3 EdgePoint(Vector3 origin, Vector3 through)
    {
        var dir = new Vector2(through.X - origin.X, through.Z - origin.Z);
        if (dir.LengthSquared() < 0.0001f) dir = new Vector2(0f, -1f);
        dir = Vector2.Normalize(dir);
        var half = Geometry.ArenaHalfWidth;
        var tx = MathF.Abs(dir.X) > 0.0001f ? ((dir.X > 0 ? half : -half) - origin.X) / dir.X : float.MaxValue;
        var tz = MathF.Abs(dir.Y) > 0.0001f ? ((dir.Y > 0 ? half : -half) - origin.Z) / dir.Y : float.MaxValue;
        var t = MathF.Min(tx, tz);
        return new Vector3(origin.X + dir.X * t, 0f, origin.Z + dir.Y * t);
    }

    private void OpenPortals(List<Vector3> at)
    {
        foreach (var point in at)
        {
            var clash = portals.Any(p => FlatDistance(p.At, point) < PortalPairRange);
            portals.Add((SpawnEnemy(BNpcBaseId.AetherialTear, BNpcNameId.AetherialTear, new Placement(point, 0f), false, true, EnemyListMode.Never), point));
            if (!clash) continue;
            world.Announce("Two Hyperdimensional Slash portals landed too close together and exploded.");
            party.WipeAllPlayers("Died to portals linking up");
            return;
        }
        portalsOpen = true;
    }

    private void TetherToPortals()
    {
        foreach (var member in AliveMembers().ToList())
            if (portals.Any(p => FlatDistance(p.At, member.Position) < PortalTetherRange))
                damage.ApplyDamage(member, 1f, ActionId.HyperdimensionalSlashLine, "walked into a portal", lethal: true);
    }

    private void ClosePortals()
    {
        portalsOpen = false;
        foreach (var (tear, _) in portals) tear?.Despawn();
        portals.Clear();
    }

    private void AdelphelLands()
    {
        var landing = state.AdelphelLanding;
        adelphel = SpawnKnight(BNpcBaseId.Adelphel, BNpcNameId.Adelphel, KnightMaxHp, new Placement(landing, RotationTowards(landing, Vector3.Zero)), true, EnemyListMode.Always);
    }

    private void ResolveFaithUnmoving(bool botsResist)
    {
        if (grinnaux == null) return;
        PlayEffect(grinnaux, ActionId.FaithUnmoving, 2.1f);
        if (!KnockbackLookup.TryGet(KnockbackId.FaithUnmoving, out var distance, out var speed)) return;
        foreach (var member in AliveMembers().ToList())
        {
            damage.ApplyDamage(member, FaithUnmovingDamage, ActionId.FaithUnmoving, "Faith Unmoving", false);
            var bot = party.IsBotDriven(member);
            if (bot ? botsResist : world.Events.Elapsed < playerAntiKnockbackUntil) continue;
            (member as ISimPartyMember)?.Knockback(grinnaux.Position, distance, speed);
        }
    }

    private void ScheduleShiningBlade(float[] dashTimes)
    {
        var path = state.AdelphelDashPath();
        float[][] orbOffsets = [[0.08f, 0.50f, 0.90f], [0.30f, 0.69f, 1.02f], [0.39f, 0.39f], [0.17f, 0.55f, 0.99f]];
        float[][] orbFractions = [[0f, 0.5f, 1f], [0.33f, 0.67f, 1f], [0.5f, 1f], [0.33f, 0.67f, 1f]];
        for (var dash = 0; dash < dashTimes.Length; dash++)
        {
            var from = path[dash];
            var to = path[dash + 1];
            world.Events.Add(dashTimes[dash], () => DashAcross(from, to));
            for (var orb = 0; orb < orbOffsets[dash].Length; orb++)
            {
                var at = Vector3.Lerp(from, to, orbFractions[dash][orb]);
                world.Events.Add(dashTimes[dash] + orbOffsets[dash][orb], () => DropBrightsphere(at));
            }
        }
    }

    private void DashAcross(Vector3 from, Vector3 to)
    {
        if (adelphel == null) return;
        var rotation = RotationTowards(from, to);
        var dasher = adelphel;
        dasher.SetRotation(rotation);
        PlayEffect(dasher, ActionId.ShiningBlade, 1.0f, rotation, at: to);
        world.Events.Add(ShiningBladeLandDelay, () => dasher.SetPosition(new Placement(to, rotation)));
        var length = FlatDistance(from, to);
        foreach (var hit in party.Find.InsideRect(new Placement(from, rotation), ShiningBladeHalfWidth, length).ToList())
            damage.ApplyDamage(hit, 1f, ActionId.ShiningBlade, "stood in Adelphel's path", lethal: true);
    }

    private void DropBrightsphere(Vector3 at)
    {
        var orb = SpawnEnemy(BNpcBaseId.Brightsphere, BNpcNameId.Brightsphere, new Placement(at, 0f), false, true, EnemyListMode.Never);
        if (orb != null) helpers.Add(orb);
        orb?.NativeCast(ActionId.BrightFlare, ActionType.Action, 0f, BrightFlareDelay, false, position: at);
        if (portals.Any(p => FlatDistance(p.At, at) < PortalBrightsphereRange))
        {
            world.Announce("A Brightsphere dropped next to a portal and set it off.");
            party.WipeAllPlayers("Died to a portal exploding");
        }
        world.Events.Add(BrightFlareDelay, () =>
        {
            PlayEffect(orb, ActionId.BrightFlare, 1.1f, at: at);
            foreach (var hit in party.Find.InsideCircle(at, BrightFlareRadius).ToList())
                damage.ApplyDamage(hit, 1f, ActionId.BrightFlare, "Brightsphere", lethal: true);
            world.Events.Add(EffectCarrierLinger, () => orb?.Despawn());
        });
    }

    private void ResolveExecution()
    {
        if (adelphel == null || party.Get(PartyRole.MainTank) is not { } target) return;
        var at = target.Position;
        adelphel.SetPosition(new Placement(at, adelphel.Rotation));
        PlayEffect(adelphel, ActionId.Execution, 2.1f, target: target.GameObjectId, at: at);
        foreach (var hit in party.Find.InsideCircle(at, ExecutionRadius).ToList())
        {
            if (hit == target)
            {
                damage.ApplyDamage(hit, 1f, ActionId.Execution, "needs an invuln", lethal: true);
                continue;
            }
            if (KnockbackLookup.TryGet(KnockbackId.Execution, out var distance, out var speed))
                (hit as ISimPartyMember)?.Knockback(at, distance, speed);
            damage.ApplyDamage(hit, 1f, ActionId.Execution, "caught in the main tank's Execution", lethal: true);
        }
    }

    private void StartHoliestHallowing()
    {
        if (adelphel == null || grinnaux == null) return;
        var index = hallowingCast++;
        hallowingInterrupted = false;
        hallowingCasting = true;
        adelphel.NativeCast(ActionId.HoliestHallowing, ActionType.Action, 0f, HallowingCastSeconds, true, targetId: grinnaux.GameObjectId);
        var assigned = DsrP1KnightsState.HallowingInterrupters[Math.Min(index, DsrP1KnightsState.HallowingInterrupters.Count - 1)];
        world.Events.Add(BotInterruptDelay, () =>
        {
            if (party.Get(assigned) is { } member && member.IsAlive() && party.IsBotDriven(member)) InterruptHallowing();
        });
        world.Events.Add(HallowingCastSeconds, () => ResolveHoliestHallowing(assigned));
    }

    private unsafe void InterruptHallowing()
    {
        if (!hallowingCasting || hallowingInterrupted || adelphel == null) return;
        hallowingInterrupted = true;
        hallowingCasting = false;
        PacketDispatcher.HandleActorControlPacket(adelphel.EntityId, CancelCastControl, CancelCastReason, 1, ActionId.HoliestHallowing, 1, 0, 0, 0, 0, 0xE0000000, false);
    }

    private void ResolveHoliestHallowing(PartyRole assigned)
    {
        hallowingCasting = false;
        if (hallowingInterrupted) return;
        PlayEffect(adelphel, ActionId.HoliestHallowing, 2.1f, target: grinnaux?.GameObjectId);
        world.Announce($"Holiest Hallowing was not interrupted ({assigned} had it).");
        if (party.Get(assigned) is { } member && member.IsAlive())
            member.Die("Let Holiest Hallowing go off");
    }

    private void OnPlayerAction(ActionType actionType, uint actionId, ulong targetId)
    {
        if (actionType != ActionType.Action) return;
        if (actionId is ActionId.ArmsLength or ActionId.Surecast)
        {
            playerAntiKnockbackUntil = world.Events.Elapsed + AntiKnockbackSeconds;
            return;
        }
        if (actionId is not (ActionId.Interject or ActionId.HeadGraze) || adelphel == null) return;
        if (Plugin.TargetManager.Target?.EntityId != adelphel.EntityId) return;
        InterruptHallowing();
    }

    private void MarkChainSymbols()
    {
        foreach (var (role, symbol) in state.Symbols)
            party.Get(role)?.AttachLockonVfx(SymbolLockon(symbol), 8f, persistent: false);
    }

    private static uint SymbolLockon(ChainSymbol symbol) => symbol switch
    {
        ChainSymbol.Circle => LockonId.Circle,
        ChainSymbol.Triangle => LockonId.Triangle,
        ChainSymbol.Square => LockonId.Square,
        _ => LockonId.Cross,
    };

    private void TetherBurningChains()
    {
        burningChains.Clear();
        foreach (var (a, b, _) in state.ChainPairs())
        {
            var first = party.Get(a);
            var second = party.Get(b);
            if (!first.IsAlive() || !second.IsAlive()) continue;
            burningChains.Add(world.Tether(first, second, TetherId.BurningChains, 15f, StatusId.BurningChains));
        }
    }

    private void BreakStretchedChains()
    {
        foreach (var chain in burningChains.Where(c => c.IsActive && c.StretchGt(ChainBreakDistance)).ToList())
        {
            chain.A?.RemoveStatus(StatusId.BurningChains);
            chain.B?.RemoveStatus(StatusId.BurningChains);
            chain.Despawn();
            burningChains.Remove(chain);
        }
    }

    private void ResolveHolyChain()
    {
        foreach (var chain in burningChains.Where(c => c.IsActive).ToList())
        {
            foreach (var holder in new[] { chain.A, chain.B })
            {
                if (holder == null || !holder.IsAlive()) continue;
                PlayEffect(charibertCaster, ActionId.HolyChain, 1.1f, target: holder.GameObjectId, at: holder.Position);
                damage.ApplyDamage(holder, HolyChainDamage, ActionId.HolyChain, "Burning Chains not broken", false);
                holder.AddStatus(StatusId.DamageDown, DamageDownSeconds);
                chainBurned.Add(holder);
            }
            chain.Despawn();
        }
        burningChains.Clear();
    }

    private void ResolveHeavensflame()
    {
        var members = AliveMembers().ToList();
        var hits = members.ToDictionary(m => m, _ => 0);
        foreach (var source in members)
        {
            PlayEffect(charibertCaster, ActionId.HeavensflameHit, 1.1f, target: source.GameObjectId, at: source.Position);
            foreach (var hit in party.Find.InsideActionAoe(ActionId.HeavensflameHit, new Placement(source.Position, 0f)))
                if (hits.ContainsKey(hit)) hits[hit]++;
        }
        foreach (var (member, count) in hits)
        {
            if (chainBurned.Contains(member) && member is not ISimPartyMember { Role: PartyRole.MainTank or PartyRole.OffTank })
            {
                damage.ApplyDamage(member, 1f, ActionId.HeavensflameHit, "still low from an unbroken Burning Chain", lethal: true);
                continue;
            }
            if (count > 1)
            {
                damage.ApplyDamage(member, 1f, ActionId.HeavensflameHit, $"overlapped {count} Heavensflames", lethal: true);
                continue;
            }
            damage.ApplyDamage(member, HeavensflameDamage, ActionId.HeavensflameHit, "Heavensflame", false);
            member.AddStatus(StatusId.FireResistanceDownII, FireResistanceDownSeconds);
        }
    }

    private void KnightsFall()
    {
        var fallen = new[] { adelphel, grinnaux };
        foreach (var knight in fallen)
        {
            knight?.SetTargetable(false);
            knight?.FadeOut();
        }
        adelphel = null;
        world.Events.Add(2f, () =>
        {
            foreach (var knight in fallen) knight?.Despawn();
        });
    }

    private void KnightsRegroupWest()
    {
        grinnaux = SpawnKnight(BNpcBaseId.Grinnaux, BNpcNameId.Grinnaux, KnightMaxHp, new Placement(DsrP1KnightsState.PrisonCentre, MathF.PI / 2f), false, EnemyListMode.Never);
        charibert = SpawnKnight(BNpcBaseId.Charibert, BNpcNameId.Charibert, CharibertMaxHp, new Placement(DsrP1KnightsState.CharibertSpot, 0f), true, EnemyListMode.Always);
    }

    private void ResolvePlanarPrison()
    {
        if (grinnaux == null) return;
        PlayEffect(grinnaux, ActionId.PlanarPrison, 2.1f);
        PlayEffect(adelphel, ActionId.BrightwingedFlight, 1.1f, target: charibert?.GameObjectId);
        foreach (var member in AliveMembers().ToList())
        {
            world.Tether(grinnaux, member, TetherId.PlanarPrison, 2f);
            member.AddStatus(StatusId.Stun, 1f);
            member.AddStatus(StatusId.PlanarImprisonment, 32.4f);
            if (FlatDistance(member.Position, DsrP1KnightsState.PrisonCentre) > PrisonRadius - 2f)
                (member as ISimPartyMember)?.CarryTo(DsrP1KnightsState.PrisonCentre + new Vector3(0f, 0f, 1f));
        }
        world.Events.Add(2f, () => prisonActive = true);
    }

    private void KillPrisonEscapees()
    {
        foreach (var member in AliveMembers().ToList())
            if (FlatDistance(member.Position, DsrP1KnightsState.PrisonCentre) > PrisonRadius)
                member.Die("Left the Planar Prison");
    }

    private void SpawnPrisonCircle() =>
        prisonCircle = world.SpawnEventObject(new EventObjectSpawnConfig
        {
            EObjId = EObjId.PlanarPrison,
            Placement = new Placement(DsrP1KnightsState.PrisonCentre, MathF.PI / 2f),
        });

    private void ReleasePrison()
    {
        prisonActive = false;
        foreach (var member in AliveMembers().ToList())
            member.RemoveStatus(StatusId.PlanarImprisonment);
    }

    private void ZephirinThrowsSpear()
    {
        zephirin?.NativeCast(ActionId.SpearOfTheFury, ActionType.Action, 0f, 9.7f, false, rotation: -MathF.PI / 2f);
    }

    private void HaurchefantArrives() =>
        haurchefant = SpawnEnemy(BNpcBaseId.Haurchefant, BNpcNameId.Haurchefant, new Placement(new Vector3(2f, 0f, 0f), MathF.PI / 2f), false, true, EnemyListMode.Never);

    private void SpawnSpear() =>
        spear = SpawnEnemy(BNpcBaseId.SpearOfTheFury, BNpcNameId.SpearOfTheFury, new Placement(new Vector3(3f, 0f, 0f), -MathF.PI / 2f), false, true, EnemyListMode.Never);

    private void Shockwave()
    {
        PlayEffect(spear, ActionId.Shockwave, 0.6f);
        foreach (var member in AliveMembers().ToList())
            damage.ApplyDamage(member, ShockwaveDamage, ActionId.Shockwave, "Shockwave", false);
    }

    private void ResolveBrightwing()
    {
        if (charibert == null) return;
        var origin = charibert.Position;
        var baited = party.Find.ClosestN(origin, 2).ToList();
        var struck = new HashSet<SimCharacter>();
        foreach (var bait in baited)
        {
            var rotation = RotationTowards(origin, bait.Position);
            var wing = SpawnHelper(origin, rotation);
            PlayEffect(wing, ActionId.Brightwing, 1.1f, rotation, bait.GameObjectId);
            world.Events.Add(2f, () => wing?.Despawn());
            foreach (var hit in party.Find.InsideActionAoe(ActionId.Brightwing, new Placement(origin, rotation), size: BrightwingHalfAngle))
            {
                if (!struck.Add(hit) || hit.HasStatus(StatusId.LightResistanceDown))
                {
                    damage.ApplyDamage(hit, 1f, ActionId.Brightwing, "took a second Brightwing", lethal: true);
                    continue;
                }
                damage.ApplyDamage(hit, BrightwingDamage, ActionId.Brightwing, "Brightwing", false);
                hit.AddStatus(StatusId.LightResistanceDown, LightResistanceDownSeconds);
                hit.AddStatus(StatusId.Skyblind, SkyblindSeconds);
            }
        }
        foreach (var marked in struck)
            world.Events.Add(SkyblindSeconds + 0.48f, () => DropSkyblind(marked));
    }

    private void DropSkyblind(SimCharacter marked)
    {
        if (!marked.IsAlive()) return;
        var at = marked.Position;
        var caster = SpawnHelper(at);
        caster?.NativeCast(ActionId.Skyblind, ActionType.Action, 0f, 2.5f, false, position: at);
        world.Events.Add(2.5f, () =>
        {
            PlayEffect(caster, ActionId.Skyblind, 1.1f, at: at);
            foreach (var hit in party.Find.InsideCircle(at, SkyblindRadius).ToList())
                damage.ApplyDamage(hit, 1f, ActionId.Skyblind, "stood on a Skyblind puddle", lethal: true);
            world.Events.Add(EffectCarrierLinger, () => caster?.Despawn());
        });
    }

    private void KnightsDepart()
    {
        charibert?.SetTargetable(false);
        charibert?.FadeOut();
        grinnaux?.FadeOut();
        zephirin?.FadeOut();
        spear?.Despawn();
    }

    private void ThordanArrives()
    {
        thordan = SpawnKnight(BNpcBaseId.Thordan, BNpcNameId.Thordan, ThordanMaxHp, new Placement(Vector3.Zero, MathF.PI), true, EnemyListMode.Always);
        world.Events.Add(0.5f, () =>
        {
            if (party.Get(PartyRole.MainTank) is { } mainTank) thordan?.SetTarget(mainTank, follow: false);
        });
    }

    private void DespawnAll()
    {
        Plugin.PlayerInputHooks.ActionExecuted -= OnPlayerAction;
        ClosePortals();
        foreach (var chain in burningChains) chain.Despawn();
        burningChains.Clear();
        prisonActive = false;
        prisonCircle?.Despawn();
        foreach (var enemy in new[] { adelphel, grinnaux, charibert, charibertCaster, zephirin, haurchefant, spear, thordan })
            enemy?.Despawn();
        foreach (var helper in helpers) helper.Despawn();
        helpers.Clear();
    }
}
