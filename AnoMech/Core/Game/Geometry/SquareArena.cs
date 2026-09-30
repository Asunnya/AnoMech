using System;
using System.Numerics;

namespace AnoMech.Core.Game.Geometry;

public sealed class SquareArena(float halfWidth) : IArenaShape
{
    public float HalfWidth { get; } = halfWidth;

    public bool IsOutside(Vector3 local) => MathF.Abs(local.X) > HalfWidth || MathF.Abs(local.Z) > HalfWidth;
}
