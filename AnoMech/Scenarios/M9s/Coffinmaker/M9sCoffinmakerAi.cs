using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.M9s.Coffinmaker;

public sealed class M9sCoffinmakerAi : IScenarioAi<M9sCoffinmakerState>
{
    public string Name => "Toxic / Hector";

    private const float CleaveMargin = 1.5f;
    private const float WallMargin = 2f;
    private const float SouthLimit = 18f;

    private M9sCoffinmakerState state = null!;
    private Vector2 stack;

    public void Run(M9sCoffinmakerState stateParam, SimWorld world)
    {
        state = stateParam;
        stack = new Vector2(0f, 4f);
        var ai = new AiManager(world);
        ai.Move(7f, () => StackAt(new Vector2(0f, 4f)));

        ai.Move(17f, () => DodgeFirstHalf(state.Cycles[0], -10f));
        ai.Move(21.2f, () => DodgeSecondHalf(state.Cycles[0], -10f));
        ai.Move(24.6f, () => StackAt(new Vector2(0f, 6f)));

        ai.Move(34.4f, () => DodgeFirstHalf(state.Cycles[1], 0f));
        ai.Move(38.6f, () => DodgeSecondHalf(state.Cycles[1], 0f));
        ai.Move(42f, () => StackAt(new Vector2(0f, 15f)));

        ai.Move(51.8f, () => DodgeFirstHalf(state.Cycles[2], 10f));
        ai.Move(56.0f, () => DodgeSecondHalf(state.Cycles[2], 10f));
        ai.Move(62f, () => DodgeFirstHalf(state.Cycles[3], 10f));
        ai.Move(66.2f, () => DodgeSecondHalf(state.Cycles[3], 10f));
        ai.Move(70f, () => StackAt(new Vector2(0f, 15f)));
    }

    private IAiMove StackAt(Vector2 spot)
    {
        stack = spot;
        return AiMove.All(spot);
    }

    private IAiMove DodgeFirstHalf(SawCycle cycle, float northEdge) =>
        StackAt(ClosestSafeSpot(cycle, cycle.ShortRotation, cycle.SecondWave, northEdge));

    private IAiMove DodgeSecondHalf(SawCycle cycle, float northEdge) =>
        StackAt(ClosestSafeSpot(cycle, cycle.LongRotation, cycle.FirstWave, northEdge));

    private Vector2 ClosestSafeSpot(SawCycle cycle, float cleaveRotation, IReadOnlyList<float> safeColumns, float northEdge)
    {
        var best = stack;
        var bestDistance = float.MaxValue;
        foreach (var x in safeColumns)
            for (var z = northEdge + WallMargin; z <= SouthLimit; z += 1f)
            {
                if (cycle.IsInsideCleave(new Vector3(x, 0f, z), cleaveRotation, CleaveMargin, state.Satisfied.IsMore)) continue;
                var distance = Vector2.Distance(stack, new Vector2(x, z));
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = new Vector2(x, z);
            }
        return best;
    }
}
