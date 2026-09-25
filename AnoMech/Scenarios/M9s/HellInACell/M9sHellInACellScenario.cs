using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.M9s.M9sConstants;

namespace AnoMech.Scenarios.M9s.HellInACell;

// M9S Hell in a Cell, from the arena coming back after the flails to just before Undead Deathmatch:
// Crowd Kill, Finale Fatale and the death-wall ring, Pulping Pulse, then two sets of four towers.
// Each tower needs exactly one player; it locks them in a Charnel Cell (4y) that lashes them every
// 3s until they kill it, and a player who already sat in one (Hell Awaits) dies to a second. While
// one group is caged the other takes the Ultrasonics: Spread aims a 100° cone at a tank and 45°
// cones at a healer and a DPS, Amp one shared 100° cone. Scenario time 0 is 358.0s into the clear in
// Network_30301_20260923.log (pull 10); cells die on the clear's schedule since nothing here takes
// player damage.
public sealed class M9sHellInACellScenario : IScenario
{
    public string Name => "Hell in a Cell";
    public IPhase Phase => M9sZone.Fight;
    public bool SupportsSolo => true;

    public IReadOnlyList<IScenarioAi> AiStrats => [new M9sHellInACellAi()];

    public void DrawSettings() => settingsWindow.Draw();
    private readonly M9sHellInACellSettingsWindow settingsWindow = new();

    private const float CellCheckStep = 0.1f;
    private const float BloodLashInterval = 3f;
    private const float WideConeHalfAngle = 50f * MathF.PI / 180f;
    private const float NarrowConeHalfAngle = 22.5f * MathF.PI / 180f;

    private M9sHellInACellState state = null!;
    private SimWorld world = null!;
    private DamageSolver damage = null!;

    private SimEnemy? vamp;
    private readonly List<SimEnemy> helpers = [];
    private readonly List<SimEnemy> pulses = [];
    private readonly List<Cell> cells = [];
    private readonly List<SimEnemy>[] towers = [[], []];

    private sealed class Cell(SimEnemy actor, SimCharacter prisoner, PartyRole role, SimTether? tether, uint lashId)
    {
        public SimEnemy Actor { get; } = actor;
        public SimCharacter Prisoner { get; } = prisoner;
        public PartyRole Role { get; } = role;
        public SimTether? Tether { get; } = tether;
        public uint LashId { get; } = lashId;
    }

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        damage = new DamageSolver(world.Party);
        damage.SetStatuses(DamageType.Magic, StatusId.MagicVulnerabilityUp);
        helpers.Clear();
        pulses.Clear();
        cells.Clear();
        foreach (var set in towers) set.Clear();

        state = new M9sHellInACellState(settingsWindow.Overrides);
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<M9sHellInACellState>)AiStrats[idx]).Run(state, world);

        world.Events.Add(0f, SpawnVamp);
        world.Events.Add(0f, () => world.EnforceSquareArenaBoundary(Geometry.ArenaHalfWidth, "Walked off the arena"));
        world.Events.Add(6.95f, () => vamp?.Cast(ActionId.CrowdKillCast, castSeconds: 0.2f));
        world.Events.Add(13.07f, () => M9sUtils.Raidwide(world.Party, damage, ActionId.CrowdKill, 0.51f));
        world.Events.Add(13.07f, () => state.Satisfied.Add(1));
        world.Events.Add(13.33f, () => state.Satisfied.Add(1));
        world.Events.Add(13.64f, () => state.Satisfied.Add(1));
        world.Events.Add(13.95f, () => state.Satisfied.Add(1));
        world.Events.Add(25.72f, () => vamp?.Cast(ActionId.FinaleFataleCast2, castSeconds: 4.7f));
        world.Events.Add(31.72f, () => M9sUtils.Raidwide(world.Party, damage, ActionId.FinaleFatale, 0.30f));
        world.Events.Add(31.81f, () => world.Map.AddEffect(0x00020001, 0x10));
        world.Events.Add(31.81f, () => world.EnforceArenaBoundary(Geometry.RingArenaRadius, "Walked into the death wall"));

        world.Events.Add(32.75f, () => CastPulpingPulses(0));
        world.Events.Add(36.72f, ResolvePulpingPulses);

        world.Events.Add(37.88f, () => CastTowers(0));
        world.Events.Add(42.90f, () => ResolveTowers(0));
        world.Events.Add(44.99f, () => CastUltrasonic(0, 0));
        world.Events.Add(47.00f, () => KillCells(0, tank: true));
        world.Events.Add(50.69f, () => ResolveUltrasonic(0, 0));
        world.Events.Add(52.11f, () => CastUltrasonic(0, 1));
        world.Events.Add(55.90f, () => KillCells(0, tank: false));
        world.Events.Add(57.81f, () => ResolveUltrasonic(0, 1));

        world.Events.Add(60.25f, () => CastTowers(1));
        world.Events.Add(65.20f, () => ResolveTowers(1));
        world.Events.Add(67.40f, () => CastUltrasonic(1, 0));
        world.Events.Add(69.30f, () => KillCells(1, tank: true));
        world.Events.Add(73.10f, () => ResolveUltrasonic(1, 0));
        world.Events.Add(74.55f, () => CastUltrasonic(1, 1));
        world.Events.Add(78.20f, () => KillCells(1, tank: false));
        world.Events.Add(80.25f, () => ResolveUltrasonic(1, 1));

        world.Events.Add(81.43f, () => CastPulpingPulses(1));
        world.Events.Add(85.40f, ResolvePulpingPulses);
        world.Events.Add(85.43f, () => CastPulpingPulses(2));
        world.Events.Add(89.40f, ResolvePulpingPulses);
        world.Events.Add(91f, DespawnAll);

        ScheduleCellChecks();
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

    private void CastPulpingPulses(int wave)
    {
        pulses.Clear();
        foreach (var at in state.PulpingPulses[wave])
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

    private void CastTowers(int set)
    {
        vamp?.Cast(ActionId.HellInACell, castSeconds: 3.5f);
        towers[set].Clear();
        foreach (var (_, at) in state.TowerAssignments(set))
            if (SpawnHelper(new Placement(at, 0f)) is { } tower)
            {
                tower.Cast(ActionId.BloodyBondageSolo, castSeconds: 4.7f);
                towers[set].Add(tower);
            }
    }

    // An empty tower is Unmitigated Explosion; a crowded one or a second cell for the same player kills.
    private void ResolveTowers(int set)
    {
        foreach (var tower in towers[set])
        {
            var soakers = world.Party.Find.InsideCircle(tower.Position, M9sHellInACellState.TowerRadius).ToList();
            if (soakers.Count == 0)
            {
                world.Party.WipeAllPlayers("Unmitigated Explosion (Hell in a Cell tower left empty)");
                return;
            }
            var prisoner = soakers[0];
            foreach (var extra in soakers.Skip(1))
            {
                extra.Die("Two players in one Hell in a Cell tower");
                state.Satisfied.Add(1);
            }
            if (prisoner.HasStatus(StatusId.HellAwaits))
            {
                prisoner.Die("Hell Awaits (took a second cell)");
                continue;
            }
            prisoner.AddStatus(StatusId.HellAwaits, 40f);
            LockInCell(prisoner, tower.Position);
        }
    }

    private void LockInCell(SimCharacter prisoner, Vector3 at)
    {
        var role = prisoner is ISimPartyMember member ? member.Role : PartyRole.MainTank;
        var (baseId, lashId) = role switch
        {
            PartyRole.MainTank or PartyRole.OffTank => (BNpcBaseId.CharnelCellTank, ActionId.BloodLashTank),
            PartyRole.RegenHealer or PartyRole.ShieldHealer => (BNpcBaseId.CharnelCellHealer, ActionId.BloodLashHealer),
            _ => (BNpcBaseId.CharnelCellDps, ActionId.BloodLashDps),
        };
        var actor = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: baseId,
            NameId: BNpcNameId.CharnelCell,
            Level: 100,
            Targetable: true,
            EnemyList: EnemyListMode.Always,
            IsVisible: true,
            Placement: new Placement(at, 0f)));
        if (actor == null) return;
        helpers.Add(actor);
        cells.Add(new Cell(actor, prisoner, role, world.Tether(actor, prisoner, TetherId.CharnelCell), lashId));
    }

    private void KillCells(int set, bool tank)
    {
        foreach (var cell in cells.Where(c => M9sHellInACellState.Groups[set].Contains(c.Role) && c.Role.IsTank() == tank).ToList())
        {
            cell.Tether?.Despawn();
            cell.Actor.Despawn();
            cells.Remove(cell);
        }
    }

    // A cell holds its prisoner in and everyone else out; it lashes the prisoner every 3s.
    private void ScheduleCellChecks()
    {
        for (var t = 43f; t <= 79f; t += CellCheckStep)
        {
            var at = t;
            world.Events.Add(at, () => CheckCells(at));
        }
    }

    private void CheckCells(float time)
    {
        var lashTick = MathF.Abs(time % BloodLashInterval) < CellCheckStep * 0.5f;
        foreach (var cell in cells.ToList())
        {
            var centre = cell.Actor.Position;
            if (cell.Prisoner.IsAlive() && M9sHellInACellState.FlatDistance(cell.Prisoner.Position, centre) > M9sHellInACellState.TowerRadius)
            {
                cell.Prisoner.Die("Broke out of the Charnel Cell");
                state.Satisfied.Add(1);
            }
            if (lashTick && cell.Prisoner.IsAlive())
                damage.ApplyDamage(cell.Prisoner, 0.1f, cell.LashId, "Blood Lash", lethal: false);
            foreach (var intruder in world.Party.Find.InsideCircle(centre, M9sHellInACellState.TowerRadius).ToList())
            {
                if (ReferenceEquals(intruder, cell.Prisoner)) continue;
                intruder.Die("Walked into a Charnel Cell");
                state.Satisfied.Add(1);
            }
        }
    }

    private void CastUltrasonic(int set, int step)
    {
        var kind = state.UltrasonicOrder[set][step];
        vamp?.Cast(kind == UltrasonicKind.Spread ? ActionId.UltrasonicSpreadCast : ActionId.UltrasonicAmpCast, castSeconds: 4.7f);
    }

    // Only the group not caged this set is targeted. Sharing a cone is the point (the DPS pair on
    // Spread, everyone on Amp), so these add no Satisfied stacks.
    private void ResolveUltrasonic(int set, int step)
    {
        var free = M9sHellInACellState.Groups[1 - set]
            .Select(r => world.Party.Get(r))
            .Where(m => m != null && m.IsAlive())
            .Cast<SimCharacter>()
            .ToList();
        if (free.Count == 0) return;

        if (state.UltrasonicOrder[set][step] == UltrasonicKind.Amp)
        {
            var target = free[Random.Shared.Next(free.Count)];
            ConeAt(target, ActionId.UltrasonicAmp, WideConeHalfAngle, [], [], stackMin: 3);
            return;
        }

        if (free.FirstOrDefault(IsTank) is { } tank)
            ConeAt(tank, ActionId.UltrasonicSpreadTank, WideConeHalfAngle, [DamageType.TankBuster], []);
        if (free.FirstOrDefault(IsHealer) is { } healer)
            ConeAt(healer, ActionId.UltrasonicSpreadSmall, NarrowConeHalfAngle, [DamageType.Magic], [(StatusId.MagicVulnerabilityUp, 1.96f)]);
        var dps = free.Where(m => !IsTank(m) && !IsHealer(m)).ToList();
        if (dps.Count > 0)
            ConeAt(dps[Random.Shared.Next(dps.Count)], ActionId.UltrasonicSpreadSmall, NarrowConeHalfAngle, [DamageType.Magic], [(StatusId.MagicVulnerabilityUp, 1.96f)]);
    }

    private void ConeAt(SimCharacter target, uint actionId, float halfAngle, DamageType[] types, (ushort, float)[] statuses, int stackMin = 0)
    {
        var from = vamp?.Position ?? Vector3.Zero;
        var cone = SpawnHelper(new Placement(from, M9sHellInACellState.RotationFacing(from, target.Position)));
        cone?.Cast(actionId, castSeconds: 0f, targetId: target.GameObjectId, animationLock: 0f);
        damage.Resolve(cone, actionId, types, statuses, stackMinTargets: stackMin, size: halfAngle);
    }

    private static bool IsTank(SimCharacter m) => m is ISimPartyMember { Role: PartyRole.MainTank or PartyRole.OffTank };
    private static bool IsHealer(SimCharacter m) => m is ISimPartyMember { Role: PartyRole.RegenHealer or PartyRole.ShieldHealer };

    private void DespawnAll()
    {
        vamp?.Despawn();
        foreach (var cell in cells) cell.Tether?.Despawn();
        cells.Clear();
        foreach (var helper in helpers) helper.Despawn();
        helpers.Clear();
        pulses.Clear();
    }
}
