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
using LuminaAction = Lumina.Excel.Sheets.Action;
using static AnoMech.Scenarios.M9s.M9sConstants;

namespace AnoMech.Scenarios.M9s.Flails;

// M9S flail phase, the second Sadistic Screech to the third: the corridor with four saws sweeping it
// (Gravegrazer, replayed hit for hit from the clear), three rounds of tank towers (Plummet) that
// leave Fatal Flails behind, Electrocution puddles that grow under a Deadly Doornail until it dies,
// and two Killer Voices. Scenario time 0 is 272.0s into the clear in Network_30301_20260923.log
// (pull 10).
//
// The doornails have simulated HP: the bots assigned to them (healers and ranged, per Toxic) chip at
// it, and the player's hostile actions on it add their share, tuned so the full group kills it about
// when the clear did. A healer or ranged player who doesn't help leaves it alive past its deadline,
// which wipes. The flails still die on the clear's schedule, a second before Barbed Burst would wipe.
public sealed class M9sFlailsScenario : IScenario
{
    public string Name => "Flails";
    public IPhase Phase => M9sZone.Fight;
    public bool SupportsSolo => true;

    public IReadOnlyList<IScenarioAi> AiStrats => [new M9sFlailsAi()];

    public void DrawSettings() => settingsWindow.Draw();
    private readonly M9sFlailsSettingsWindow settingsWindow = new();

    private const float HazardStep = 0.1f;
    private const uint DoornailMaxHp = 964096;
    private const float DoornailKillSecondsByFour = 13.9f;
    private const float ContributorShare = 1f / (4f * DoornailKillSecondsByFour);
    private const float GcdHit = ContributorShare * 2.5f;
    private const float OgcdHit = ContributorShare * 0.8f;
    private const float PuddleDespawnDelay = 3.15f;
    private const byte GcdCooldownGroup = 58;

    private static readonly PartyRole[] DoornailKillers =
        [PartyRole.RegenHealer, PartyRole.ShieldHealer, PartyRole.PhysRangedDps, PartyRole.CasterDps];

    private M9sFlailsState state = null!;
    private SimWorld world = null!;
    private DamageSolver damage = null!;

    private SimEnemy? vamp;
    private SimEnemy? doornail;
    private int doornailRound;
    private float doornailHp;
    private float hazardTime;
    private int lastPlayerActionSequence = -1;
    private readonly bool[] doornailDown = new bool[3];
    private readonly SimOmen?[] puddleOmens = new SimOmen?[3];
    private readonly SimEventObject?[] puddles = new SimEventObject?[3];
    private bool corridorActive;
    private readonly Dictionary<SawLane, SimEnemy?> saws = [];
    private readonly List<SimEnemy> helpers = [];
    private readonly List<SimEnemy>[] flails = [[], [], []];

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        damage = new DamageSolver(world.Party);
        damage.SetStatuses(DamageType.Magic, StatusId.MagicVulnerabilityUp);
        helpers.Clear();
        foreach (var round in flails) round.Clear();
        saws.Clear();
        corridorActive = false;
        hazardTime = 0f;
        Array.Clear(doornailDown);
        Plugin.PlayerInputHooks.ActionExecuted -= OnPlayerAction;
        Plugin.PlayerInputHooks.ActionExecuted += OnPlayerAction;

        state = new M9sFlailsState(settingsWindow.Overrides);
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<M9sFlailsState>)AiStrats[idx]).Run(state, world);

        world.Events.Add(0f, SpawnVamp);
        world.Events.Add(0f, () => world.EnforceSquareArenaBoundary(Geometry.ArenaHalfWidth, "Walked off the arena"));
        world.Events.Add(6.06f, () => vamp?.Cast(ActionId.SadisticScreechCast, castSeconds: 4.7f));
        world.Events.Add(6.06f, () => MapEffect(0x00020001, 0x0F));
        world.Events.Add(11.88f, () => MapEffect(0x00020001, 0x00, 0x0D, 0x0E));
        world.Events.Add(11.88f, () => MapEffect(0x00080004, 0x0F));
        world.Events.Add(11.88f, () => corridorActive = true);
        world.Events.Add(11.88f, SpawnSaws);
        world.Events.Add(11.95f, () => M9sUtils.Raidwide(world.Party, damage, ActionId.SadisticScreech, 0.40f));
        world.Events.Add(16.02f, () => MapEffect(0x00800040, 0x0D, 0x0E));
        ScheduleSawHits();
        ScheduleHazardChecks();

        world.Events.Add(17.26f, () => CastRound(0));
        world.Events.Add(23.13f, () => FlailCellEffect(0, 0x00020001));
        world.Events.Add(24.20f, () => DoornailCellEffect(0, 0x00020001));
        world.Events.Add(24.26f, () => ResolveRound(0));
        world.Events.Add(24.78f, () => FlailCellEffect(0, 0x00400020));
        world.Events.Add(24.90f, () => CastBarbedBurst(0));
        world.Events.Add(24.96f, () => SpawnPuddle(0));
        world.Events.Add(25.23f, () => DoornailCellEffect(0, 0x00200010));
        world.Events.Add(25.23f, () => SpawnDoornail(0));
        world.Events.Add(28.26f, () => AnimatePuddle(0, 0x10, 0x20));
        world.Events.Add(28.27f, () => vamp?.Cast(ActionId.KillerVoice, castSeconds: 4.7f));
        world.Events.Add(28.62f, () => MapEffect(0x04000800, 0x0D, 0x0E));
        world.Events.Add(29.65f, () => MapEffect(0x01000020, 0x0D, 0x0E));
        world.Events.Add(33.27f, () => M9sUtils.Raidwide(world.Party, damage, ActionId.KillerVoice, 0.45f));
        world.Events.Add(35.43f, () => CastRound(1));
        world.Events.Add(39.60f, () => KillFlails(0));
        world.Events.Add(39.60f, () => FlailCellEffect(0, 0x00080004));
        world.Events.Add(41.26f, () => FlailCellEffect(1, 0x00020001));
        world.Events.Add(42.27f, () => MapEffect(0x02000001, 0x0D, 0x0E));
        world.Events.Add(42.33f, () => DoornailCellEffect(1, 0x00020001));
        world.Events.Add(42.42f, () => WipeIfDoornailSurvived(0));
        world.Events.Add(42.42f, () => ResolveRound(1));
        world.Events.Add(42.91f, () => FlailCellEffect(1, 0x00400020));
        world.Events.Add(43.06f, () => CastBarbedBurst(1));
        world.Events.Add(43.29f, () => MapEffect(0x00800040, 0x0D, 0x0E));
        world.Events.Add(43.35f, () => DoornailCellEffect(1, 0x00200010));
        world.Events.Add(43.35f, () => SpawnDoornail(1));
        world.Events.Add(43.39f, () => SpawnPuddle(1));
        world.Events.Add(46.38f, () => AnimatePuddle(1, 0x10, 0x20));
        world.Events.Add(46.44f, () => vamp?.Cast(ActionId.KillerVoice, castSeconds: 4.7f));
        world.Events.Add(51.43f, () => M9sUtils.Raidwide(world.Party, damage, ActionId.KillerVoice, 0.45f));
        world.Events.Add(53.58f, () => CastRound(2));
        world.Events.Add(55.89f, () => MapEffect(0x04000800, 0x0D, 0x0E));
        world.Events.Add(56.91f, () => MapEffect(0x01000020, 0x0D, 0x0E));
        world.Events.Add(57.76f, () => KillFlails(1));
        world.Events.Add(57.76f, () => FlailCellEffect(1, 0x00080004));
        world.Events.Add(59.39f, () => FlailCellEffect(2, 0x00020001));
        world.Events.Add(60.46f, () => DoornailCellEffect(2, 0x00020001));
        world.Events.Add(60.57f, () => WipeIfDoornailSurvived(1));
        world.Events.Add(60.57f, () => ResolveRound(2));
        world.Events.Add(61.04f, () => FlailCellEffect(2, 0x00400020));
        world.Events.Add(61.21f, () => CastBarbedBurst(2));
        world.Events.Add(61.38f, () => SpawnPuddle(2));
        world.Events.Add(61.48f, () => DoornailCellEffect(2, 0x00200010));
        world.Events.Add(61.48f, () => SpawnDoornail(2));
        world.Events.Add(64.51f, () => AnimatePuddle(2, 0x10, 0x20));
        world.Events.Add(69.51f, () => MapEffect(0x02000001, 0x0D, 0x0E));
        world.Events.Add(70.53f, () => MapEffect(0x00800040, 0x0D, 0x0E));
        world.Events.Add(75.91f, () => KillFlails(2));
        world.Events.Add(75.91f, () => FlailCellEffect(2, 0x00080004));
        world.Events.Add(78.61f, () => WipeIfDoornailSurvived(2));
        world.Events.Add(78.61f, () => vamp?.Cast(ActionId.SadisticScreechCast, castSeconds: 4.7f));
        world.Events.Add(84.34f, () => MapEffect(0x00080004, 0x00));
        world.Events.Add(84.50f, () => M9sUtils.Raidwide(world.Party, damage, ActionId.SadisticScreech, 0.40f));
        world.Events.Add(86f, DespawnAll);
    }

    private void MapEffect(uint flags, params byte[] indices)
    {
        foreach (var index in indices)
            world.Map.AddEffect(flags, index);
    }

    // Flails and doornails have no mesh either: what players see is a map-effect object per cell.
    private void FlailCellEffect(int round, uint flags)
    {
        var r = state.Rounds[round];
        MapEffect(flags, (byte)(0x01 + M9sFlailsState.CellIndex(r.NorthTower)), (byte)(0x01 + M9sFlailsState.CellIndex(r.SouthTower)));
    }

    private void DoornailCellEffect(int round, uint flags) =>
        MapEffect(flags, (byte)(0x12 + M9sFlailsState.CellIndex(state.Rounds[round].Doornail)));

    private SimEnemy? Spawn(uint baseId, uint nameId, Placement placement, bool targetable, EnemyListMode enemyList)
    {
        var enemy = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: baseId,
            NameId: nameId,
            Level: 100,
            Targetable: targetable,
            EnemyList: enemyList,
            IsVisible: true,
            Placement: placement));
        if (enemy != null) helpers.Add(enemy);
        return enemy;
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

    // Drawn so its action timelines play; the helper's ModelChara has no mesh.
    private SimEnemy? SpawnHelper(Vector3 at) =>
        Spawn(BNpcBaseId.Helper, 0, new Placement(at, 0f), false, EnemyListMode.Never);

    private void SpawnSaws()
    {
        foreach (var lane in Enum.GetValues<SawLane>())
        {
            var first = M9sFlailsSawData.Hits.First(h => h.Lane == lane);
            // The big saws' BNpc has no mesh; their blades are the 0x0D/0x0E map effects.
            saws[lane] = first.IsBig
                ? Spawn(BNpcBaseId.BigSaw, BNpcNameId.BigSaw, new Placement(first.Position, first.Rotation), false, EnemyListMode.Never)
                : Spawn(BNpcBaseId.Neckbiter, BNpcNameId.Neckbiter, new Placement(first.Position, first.Rotation), false, EnemyListMode.Never);
        }
    }

    // Each saw strikes where it stands, then rolls on to its next hit so the model moves smoothly
    // between the recorded positions.
    private void ScheduleSawHits()
    {
        foreach (var lane in Enum.GetValues<SawLane>())
        {
            var laneHits = M9sFlailsSawData.Hits.Where(h => h.Lane == lane).ToList();
            for (var i = 0; i < laneHits.Count; i++)
            {
                var hit = laneHits[i];
                var next = i + 1 < laneHits.Count ? laneHits[i + 1] : null;
                world.Events.Add(hit.At, () => StrikeSaw(hit, next));
            }
        }
    }

    private void StrikeSaw(SawHit hit, SawHit? next)
    {
        if (!saws.TryGetValue(hit.Lane, out var saw) || saw == null) return;
        saw.SetPosition(new Placement(hit.Position, hit.Rotation));
        var actionId = hit.IsBig ? ActionId.GravegrazerBig : ActionId.GravegrazerSmall;
        saw.Cast(actionId, castSeconds: 0f, animationLock: 0f);
        state.Satisfied.AddFor(damage.Resolve(saw, actionId, [DamageType.Lethal], []));

        if (next == null || next.At <= hit.At) return;
        var distance = M9sFlailsState.FlatDistance(hit.Position, next.Position);
        if (distance > 0.1f) saw.MoveTo(next.Position, distance / (next.At - hit.At), next.Rotation);
    }

    private void CastRound(int round)
    {
        var r = state.Rounds[round];
        SpawnHelper(r.NorthTower)?.Cast(ActionId.Plummet, castSeconds: 6.7f);
        SpawnHelper(r.SouthTower)?.Cast(ActionId.Plummet, castSeconds: 6.7f);
        SpawnHelper(r.Doornail)?.Cast(ActionId.Electrocution, castSeconds: 6.7f);
    }

    // A tower nobody tanks goes off as Massive Impact and wipes the party; anyone else standing in
    // one takes a buster.
    private void ResolveRound(int round)
    {
        var r = state.Rounds[round];
        foreach (var tower in new[] { r.NorthTower, r.SouthTower })
        {
            var soakers = world.Party.Find.InsideCircle(tower, M9sFlailsState.TowerRadius);
            if (!soakers.Any(IsTank))
            {
                world.Party.WipeAllPlayers("Massive Impact (tower left untanked)");
                return;
            }
            foreach (var soaker in soakers.Where(s => !IsTank(s)).ToList())
            {
                soaker.Die("Took a Plummet tower as a non-tank");
                state.Satisfied.Add(1);
            }
            SpawnFlail(round, tower);
        }
        state.Satisfied.AddFor(damage.Resolve(SpawnHelper(r.Doornail), ActionId.Electrocution, [DamageType.Lethal], []));
    }

    private static bool IsTank(SimCharacter member) => member is ISimPartyMember { Role: PartyRole.MainTank or PartyRole.OffTank };

    private void SpawnFlail(int round, Vector3 at)
    {
        if (Spawn(BNpcBaseId.FatalFlail, BNpcNameId.FatalFlail, new Placement(at, 0f), true, EnemyListMode.Always) is { } flail)
            flails[round].Add(flail);
    }

    private void CastBarbedBurst(int round)
    {
        foreach (var flail in flails[round]) flail.Cast(ActionId.BarbedBurst, castSeconds: 15.7f);
    }

    private void KillFlails(int round)
    {
        foreach (var flail in flails[round]) flail.Despawn();
        flails[round].Clear();
    }

    private unsafe void SpawnDoornail(int round)
    {
        doornail = Spawn(BNpcBaseId.DeadlyDoornail, BNpcNameId.DeadlyDoornail, new Placement(state.Rounds[round].Doornail, 0f), true, EnemyListMode.Always);
        doornailRound = round;
        doornailHp = 1f;
        var chara = doornail == null ? null : doornail.BattleCharaPtr;
        if (chara != null) chara->MaxHealth = DoornailMaxHp;
        ShowDoornailHp();
    }

    private unsafe void ShowDoornailHp()
    {
        var chara = doornail == null ? null : doornail.BattleCharaPtr;
        if (chara != null)
            chara->Health = (uint)MathF.Ceiling(DoornailMaxHp * MathF.Max(0f, doornailHp));
    }

    private void OnPlayerAction(ActionType actionType, uint actionId)
    {
        if (actionType != ActionType.Action || doornail is not { IsActive: true } nail) return;
        if (!IsNewPlayerAction()) return;
        if (Plugin.TargetManager.Target?.EntityId != nail.EntityId) return;
        if (Plugin.DataManager.GetExcelSheet<LuminaAction>().GetRowOrDefault(actionId) is not { CanTargetHostile: true } action) return;
        var isGcd = action.CooldownGroup == GcdCooldownGroup || action.AdditionalCooldownGroup == GcdCooldownGroup;
        DamageDoornail(isGcd ? GcdHit : OgcdHit);
    }

    private unsafe bool IsNewPlayerAction()
    {
        var am = ActionManager.Instance();
        if (am == null) return false;
        var seq = (int)am->LastUsedActionSequence;
        if (seq == lastPlayerActionSequence) return false;
        lastPlayerActionSequence = seq;
        return true;
    }

    private void DamageDoornail(float fraction)
    {
        if (doornail is not { IsActive: true }) return;
        doornailHp -= fraction;
        ShowDoornailHp();
        if (doornailHp <= 0f) DoornailDies();
    }

    private void DoornailDies()
    {
        var round = doornailRound;
        KillDoornail();
        doornailDown[round] = true;
        state.PuddleGoesOutAt(round, hazardTime + M9sFlailsState.PuddleOutDelay);
        world.Events.Add(M9sFlailsState.PuddleOutDelay, () =>
        {
            DoornailCellEffect(round, 0x00080004);
            AnimatePuddle(round, 0x4, 0x8);
        });
        world.Events.Add(PuddleDespawnDelay, () => DespawnPuddle(round));
    }

    private void WipeIfDoornailSurvived(int round)
    {
        if (doornailDown[round]) return;
        world.Party.WipeAllPlayers("Deadly Doornail survived (its puddle overran the arena)");
    }

    private void ChipDoornailWithBots()
    {
        if (doornail is not { IsActive: true }) return;
        var playerRole = world.Party.PlayerRole;
        // An empty slot (solo) still counts, so the player alone isn't asked to out-damage a group.
        var bots = DoornailKillers.Count(role => role != playerRole && (world.Party.Get(role) is not { } bot || bot.IsAlive()));
        DamageDoornail(bots * ContributorShare * HazardStep);
    }

    private void KillDoornail()
    {
        doornail?.Despawn();
        doornail = null;
    }

    // The puddle is an EObj whose own SG timelines grow it and put it out; the log's EObjAnimation
    // (0x19D) pairs are the old/new SharedTimelineState, 0x10 -> 0x20 to grow and 0x4 -> 0x8 to go
    // out. It spawns in state 1, the small puddle (ACT's MaxMP column reads the EObj state field and
    // shows 0 -> 1 as it appears; state 0 shows it fully grown). The omen under it marks the radius
    // this sim kills at.
    private void SpawnPuddle(int round)
    {
        var at = state.Rounds[round].Doornail;
        puddles[round] = world.SpawnEventObject(new EventObjectSpawnConfig
        {
            EObjId = EObjId.ElectroPuddle,
            Placement = new Placement(at, 0f),
            TimelineState = PuddleSpawnState,
        });
        puddleOmens[round] = world.SpawnOmen(VfxPath.ElectroPuddle, new Placement(at, 0f), CircleScale(M9sFlailsState.ElectrocutionRadius), 60f);
    }

    private unsafe void AnimatePuddle(int round, ushort oldState, ushort newState)
    {
        if (puddles[round] is not { IsAlive: true } puddle) return;
        ((GameObject*)puddle.Address)->UpdateSharedTimelineState(oldState, newState);
    }

    private void DespawnPuddle(int round)
    {
        puddles[round]?.Despawn();
        puddles[round] = null;
        puddleOmens[round]?.Despawn();
        puddleOmens[round] = null;
    }

    private const ushort PuddleSpawnState = 1;

    private static Vector3 CircleScale(float radius) => new(radius, 1f, radius);

    private void ScheduleHazardChecks()
    {
        for (var t = 11.9f; t <= 86f; t += HazardStep)
        {
            var at = t;
            world.Events.Add(at, () => CheckContinuousHazards(at));
        }
    }

    private void CheckContinuousHazards(float time)
    {
        hazardTime = time;
        ChipDoornailWithBots();
        for (var round = 0; round < 3; round++)
            if (state.PuddleRadius(round, time) is { } radius)
            {
                puddleOmens[round]?.SetScale(CircleScale(radius));
                foreach (var member in world.Party.Find.InsideCircle(state.Rounds[round].Doornail, radius).ToList())
                {
                    member.Die("Stood in the electrified puddle");
                    state.Satisfied.Add(1);
                }
            }

        if (!corridorActive) return;
        for (var slot = 0; slot < 8; slot++)
            if (world.Party.Get(slot) is { } member && member.IsAlive() && MathF.Abs(member.Position.X) > M9sFlailsState.CorridorHalfWidth)
                member.Die("Walked into the saw wall");
    }

    private void DespawnAll()
    {
        Plugin.PlayerInputHooks.ActionExecuted -= OnPlayerAction;
        vamp?.Despawn();
        foreach (var helper in helpers) helper.Despawn();
        helpers.Clear();
        foreach (var round in flails) round.Clear();
        saws.Clear();
        KillDoornail();
        for (var round = 0; round < 3; round++) DespawnPuddle(round);
    }
}
