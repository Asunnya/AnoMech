using System;
using System.Numerics;
using AnoMech.Core.Game;
using static AnoMech.Scenarios.M9s.M9sConstants;

namespace AnoMech.Scenarios.M9s;

public enum CleaveOrder
{
    LeftFirst,
    RightFirst,
}

// Half Moon: two 180° cleaves split the arena through the boss, the boss's left (LeftFirst) or
// right half first, the other half 3s later. The boss cast id says which: 45902 left, 45904 right,
// read off six pulls against the cleave rotations.
//
// At 8+ Satisfied (`more`) the larger version fires from the boss's back edge instead of her centre,
// so each half reaches a hitbox-width past the split: the log's one sample (45903 with 45945/45946,
// left first) sits 4y behind the boss. The right-first ids follow the same numbering, unverified.
public static class M9sHalfMoon
{
    private const float BossHitboxRadius = 4f;

    public static float ShortRotation(float bossRotation, CleaveOrder order) =>
        bossRotation + (order == CleaveOrder.LeftFirst ? 1f : -1f) * MathF.PI / 2f;

    public static float LongRotation(float bossRotation, CleaveOrder order) => ShortRotation(bossRotation, order) + MathF.PI;

    public static uint BossCastId(CleaveOrder order, bool more = false) => (order, more) switch
    {
        (CleaveOrder.LeftFirst, false) => ActionId.HalfMoonLeftCast,
        (CleaveOrder.RightFirst, false) => ActionId.HalfMoonRightCast,
        (CleaveOrder.LeftFirst, true) => ActionId.HalfMoonLeftCastMore,
        _ => ActionId.HalfMoonRightCastMore,
    };

    public static uint ShortId(CleaveOrder order, bool more = false) => (order, more) switch
    {
        (CleaveOrder.LeftFirst, false) => ActionId.HalfMoonLeftShort,
        (CleaveOrder.RightFirst, false) => ActionId.HalfMoonRightShort,
        (CleaveOrder.LeftFirst, true) => ActionId.HalfMoonLeftShortMore,
        _ => ActionId.HalfMoonRightShortMore,
    };

    public static uint LongId(CleaveOrder order, bool more = false) => (order, more) switch
    {
        (CleaveOrder.LeftFirst, false) => ActionId.HalfMoonLeftLong,
        (CleaveOrder.RightFirst, false) => ActionId.HalfMoonRightLong,
        (CleaveOrder.LeftFirst, true) => ActionId.HalfMoonLeftLongMore,
        _ => ActionId.HalfMoonRightLongMore,
    };

    public static Placement CleaveOrigin(Placement boss, float rotation, bool more = false)
    {
        if (!more) return boss with { Rotation = rotation };
        var back = new Vector3(MathF.Sin(rotation), 0f, MathF.Cos(rotation)) * -BossHitboxRadius;
        return new Placement(boss.Position + back, rotation);
    }

    public static bool IsInsideCleave(Placement boss, float rotation, Vector3 point, float margin = 0f, bool more = false)
    {
        var origin = CleaveOrigin(boss, rotation, more).Position;
        var forward = new Vector2(MathF.Sin(rotation), MathF.Cos(rotation));
        var offset = new Vector2(point.X - origin.X, point.Z - origin.Z);
        return Vector2.Dot(offset, forward) > -margin;
    }
}
