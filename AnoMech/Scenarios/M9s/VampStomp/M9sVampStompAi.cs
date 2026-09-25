using System;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.M9s.VampStomp;

public sealed class M9sVampStompAi : IScenarioAi<M9sVampStompState>
{
    public string Name => "Toxic / Hector";

    private static readonly float[] SquareArenaRadii = [2f, 3.5f, 5f, 8.5f, 10f, 11f, 12f, 14.5f, 17f, 19.5f, 22f, 24.5f, 26.5f];
    private const float ArenaReach = M9sConstants.Geometry.ArenaHalfWidth - 1f;

    public void Run(M9sVampStompState state, SimWorld world)
    {
        var dodge = new M9sBatStompDodge(world, state.Bats, IsInsideSquareArena, SquareArenaRadii);
        var ai = new AiManager(world);
        ai.Move(0.5f, Uptime);
        ai.Move(12.5f, TanksSplitForHardcore);
        ai.Move(20.3f, () => AiMove.Create(dodge.MarkerSpots()).NaturalOrder());
        world.Events.Add(30.3f, () => dodge.DodgeBatRing(0, 30.3f, 34.6f));
        world.Events.Add(34.6f, () => dodge.DodgeBatRing(1, 34.6f, 38.1f));
        world.Events.Add(38.1f, () => dodge.DodgeBatRing(2, 38.1f, 41.6f));
        ai.Move(41.6f, StackForBrutalRain);
    }

    private static IAiMove Uptime() => AiMove.Create(
        new(0f, -5f),
        new(-3f, -4.5f),
        new(-3f, 7f),
        new(3f, 7f),
        new(-5f, 0f),
        new(5f, 0f),
        new(-5f, 6f),
        new(5f, 6f)
    ).NaturalOrder();

    private static IAiMove TanksSplitForHardcore() => AiMove.Create(
        new(-5f, -6f),
        new(5f, -6f),
        new(-3f, 7f),
        new(3f, 7f),
        new(-4.5f, 2f),
        new(4.5f, 2f),
        new(-5f, 6f),
        new(5f, 6f)
    ).NaturalOrder();

    private static IAiMove StackForBrutalRain() => AiMove.All(new Vector2(0f, -1.5f));

    private static bool IsInsideSquareArena(Vector3 spot) => MathF.Abs(spot.X) <= ArenaReach && MathF.Abs(spot.Z) <= ArenaReach;
}
