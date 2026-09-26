using System;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Uwu.Intermission;

public sealed class UwuIntermissionAi : IScenarioAi<UwuIntermissionState>
{
    public string Name => "Stack middle";

    private const float Spread = 0.8f;

    public void Run(UwuIntermissionState state, SimWorld world)
    {
        var ai = new AiManager(world);
        ai.Move(0.5f, () => Group(new Vector2(0f, 3f)));
        ai.Move(18.5f, () => Group(new Vector2(0f, 2f)));
    }

    private static IAiMove Group(Vector2 anchor)
    {
        var spots = new Vector2?[8];
        for (var slot = 0; slot < 8; slot++)
        {
            var angle = slot * MathF.PI / 4f;
            spots[slot] = anchor + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Spread;
        }
        return AiMove.Create(spots).NaturalOrder();
    }
}
