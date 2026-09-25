using System;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.M9s.Deathmatch;

public sealed class M9sDeathmatchAi : IScenarioAi<M9sDeathmatchState>
{
    public string Name => "Toxic / Hector";

    private const float TowerStandRadius = 10f;
    private const float DodgeRadius = 8f;
    private const float HugDonutBatRadius = 10.5f;
    private const float ClearOfCircleRadius = 4f;

    private static readonly float[] ScratchCastAt = [11.86f, 30.19f];

    private M9sDeathmatchState state = null!;

    public void Run(M9sDeathmatchState stateParam, SimWorld world)
    {
        state = stateParam;
        var ai = new AiManager(world);
        ai.Move(0.5f, () => AiMove.All(new Vector2(0f, 3f)));
        ai.Move(3.0f, () => BothGroups(group => At(state.TowerBearing(group), TowerStandRadius)));

        for (var cycle = 0; cycle < 2; cycle++)
        {
            var waves = M9sDeathmatchState.WaveAt[cycle];
            for (var wave = 0; wave < waves.Length; wave++)
            {
                var c = cycle;
                var w = wave;
                var moveAt = wave == 0 ? ScratchCastAt[cycle] + 0.2f : waves[wave - 1] + 0.1f;
                ai.Move(moveAt, () => BothGroups(group => GapNearOwnBat(group, c, w)));
            }
            var last = cycle;
            ai.Move(waves[^1] + 0.1f, () => BothGroups(group => ClearOfOwnBatShape(group, last)));
        }

        ai.Move(46.6f, () => AiMove.All(new Vector2(0f, 2f)));
    }

    private static IAiMove BothGroups(Func<int, Vector2> spotForGroup)
    {
        var spots = new Vector2?[8];
        for (var group = 0; group < 2; group++)
            foreach (var role in M9sDeathmatchState.Groups[group])
                spots[(int)role] = spotForGroup(group);
        return AiMove.Create(spots).NaturalOrder();
    }

    private Vector2 GapNearOwnBat(int group, int cycle, int wave)
    {
        var batBearing = state.BatBearing(group, M9sDeathmatchState.WaveAt[cycle][wave]);
        var offset = state.Cycles[cycle].FirstConeOffset;
        var best = M9sSanguineScratch.GapCentre(offset, wave, 0);
        var bestDelta = float.MaxValue;
        for (var i = 0; i < 8; i++)
        {
            var gap = M9sSanguineScratch.GapCentre(offset, wave, i);
            var delta = MathF.Abs(((gap - batBearing) % 360f + 540f) % 360f - 180f);
            if (delta >= bestDelta) continue;
            bestDelta = delta;
            best = gap;
        }
        return At(best, DodgeRadius);
    }

    private Vector2 ClearOfOwnBatShape(int group, int cycle)
    {
        var bearing = state.BatFinalBearing(group, cycle);
        return At(bearing, state.BatIsCircle(group, cycle) ? ClearOfCircleRadius : HugDonutBatRadius);
    }

    private static Vector2 At(float bearing, float radius)
    {
        var p = M9sDeathmatchState.AtBearing(bearing, radius);
        return new Vector2(p.X, p.Z);
    }
}
