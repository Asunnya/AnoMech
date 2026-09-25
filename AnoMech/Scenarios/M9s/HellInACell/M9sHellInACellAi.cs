using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.M9s.HellInACell;

public sealed class M9sHellInACellAi : IScenarioAi<M9sHellInACellState>
{
    public string Name => "Toxic / Hector";

    private const float PulseClearance = 6.5f;
    private const float ArenaReach = 18f;

    private M9sHellInACellState state = null!;

    public void Run(M9sHellInACellState stateParam, SimWorld world)
    {
        state = stateParam;
        var ai = new AiManager(world);
        ai.Move(0.5f, () => AiMove.All(new Vector2(0f, 3f)));
        ai.Move(32.8f, () => AiMove.All(NearestSpotClearOfPulses(0, new Vector2(0f, 3f))));

        ai.Move(38.0f, () => SoakTowersAndFormUp(0, UltrasonicStep(0, 0)));
        ai.Move(50.8f, () => FormUpFreeGroup(0, UltrasonicStep(0, 1)));

        ai.Move(60.4f, () => SoakTowersAndFormUp(1, UltrasonicStep(1, 0)));
        ai.Move(73.2f, () => FormUpFreeGroup(1, UltrasonicStep(1, 1)));

        ai.Move(81.5f, () => AiMove.All(NearestSpotClearOfPulses(1, new Vector2(0f, 3f))));
        ai.Move(85.5f, () => AiMove.All(NearestSpotClearOfPulses(2, new Vector2(0f, 3f))));
    }

    private UltrasonicKind UltrasonicStep(int set, int step) => state.UltrasonicOrder[set][step];

    private IAiMove SoakTowersAndFormUp(int set, UltrasonicKind kind)
    {
        var spots = FreeGroupSpots(set, kind);
        foreach (var (role, tower) in state.TowerAssignments(set))
            spots[(int)role] = new Vector2(tower.X, tower.Z);
        return AiMove.Create(spots).NaturalOrder();
    }

    private IAiMove FormUpFreeGroup(int set, UltrasonicKind kind) => AiMove.Create(FreeGroupSpots(set, kind)).NaturalOrder();

    private Vector2?[] FreeGroupSpots(int set, UltrasonicKind kind)
    {
        var formation = state.Formation(set);
        var spots = new Vector2?[8];
        var free = M9sHellInACellState.Groups[1 - set];
        spots[(int)free[0]] = FormationSpot(formation.TankBearing);
        spots[(int)free[1]] = FormationSpot(kind == UltrasonicKind.Amp ? formation.TankBearing : formation.HealerBearing);
        spots[(int)free[2]] = FormationSpot(kind == UltrasonicKind.Amp ? formation.TankBearing : formation.DpsBearing);
        spots[(int)free[3]] = FormationSpot(kind == UltrasonicKind.Amp ? formation.TankBearing : formation.DpsBearing);
        return spots;
    }

    private static Vector2 FormationSpot(float bearing)
    {
        var p = M9sHellInACellState.AtBearing(bearing, M9sHellInACellState.FormationRadius);
        return new Vector2(p.X, p.Z);
    }

    private Vector2 NearestSpotClearOfPulses(int wave, Vector2 home)
    {
        var best = home;
        var bestDistance = float.MaxValue;
        for (var x = -ArenaReach; x <= ArenaReach; x += 1f)
            for (var z = -ArenaReach; z <= ArenaReach; z += 1f)
            {
                var spot = new Vector2(x, z);
                if (spot.Length() > ArenaReach || !IsClearOfPulses(wave, spot)) continue;
                var distance = Vector2.Distance(spot, home);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = spot;
            }
        return best;
    }

    private bool IsClearOfPulses(int wave, Vector2 spot)
    {
        foreach (var pulse in state.PulpingPulses[wave])
            if (Vector2.Distance(spot, new Vector2(pulse.X, pulse.Z)) < PulseClearance) return false;
        return true;
    }
}
