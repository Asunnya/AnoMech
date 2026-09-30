using System.Numerics;

namespace AnoMech.Core.Game.Geometry;

public sealed class CircleArena(float radius) : IArenaShape
{
    public float Radius { get; } = radius;

    public bool IsOutside(Vector3 local) => local.X * local.X + local.Z * local.Z > Radius * Radius;
}
