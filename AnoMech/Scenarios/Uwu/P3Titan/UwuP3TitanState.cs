using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.Uwu.P3Titan;

// Titan randomization, from 14 pulls. First-jump positions are authored with Titan east and rotated by FromJumpFrame.
public sealed class UwuP3TitanState
{
    public const float JumpRadius = 14f;
    public const float FirstShrinkRadius = 15.0f;
    public const float SecondShrinkRadius = 11.5f;
    public const float LandslideHalfWidth = 3f;
    public const float WeightRadius = 6f;
    public const float BuryRadius = 3f;
    public const float BurstRadius = 5f;

    public static readonly float[] LandslideOffsets = [0f, 45f, -45f, 135f, -135f];
    public static readonly float[] AwakenedLandslideOffsets = [22.5f, -22.5f, 90f, -90f, 180f];
    public static readonly float[] LateBombBearings = [45f, 135f, 225f, 315f];

    public readonly record struct Hazard(Vector2 Origin, float Bearing, float Size, float At, bool IsLane);

    public float FirstJumpBearing { get; }
    public float SecondJumpBearing { get; }
    public int SafeSide { get; }
    public IReadOnlyList<PartyRole> GaolTargets { get; }
    public PartyRole JailedHealer { get; }
    public IReadOnlyList<IReadOnlyList<PartyRole>> WeightTargets { get; }
    public int LateBombStart { get; }

    public PartyRole Holder => GaolTargets.Contains(PartyRole.MainTank) ? PartyRole.OffTank : PartyRole.MainTank;
    public bool BothTanksJailed => GaolTargets.Contains(PartyRole.MainTank) && GaolTargets.Contains(PartyRole.OffTank);

    public List<Hazard> Hazards { get; } = [];
    public Vector3 TitanPosition { get; set; }
    public HashSet<PartyRole> Jailed { get; } = [];

    public UwuP3TitanState()
    {
        var rng = new Random();
        FirstJumpBearing = 90f * rng.Next(4);
        SecondJumpBearing = (FirstJumpBearing + 90f * rng.Next(1, 4)) % 360f;
        SafeSide = rng.Next(2) == 0 ? 1 : -1;
        var everyone = Enum.GetValues<PartyRole>();
        GaolTargets = everyone.OrderBy(_ => rng.Next()).Take(3).OrderBy(r => (int)r).ToList();
        JailedHealer = rng.Next(2) == 0 ? PartyRole.RegenHealer : PartyRole.ShieldHealer;
        WeightTargets = new[] { 4, 4, 2, 2, 2, 2, 2 }
            .Select(count => (IReadOnlyList<PartyRole>)everyone.OrderBy(_ => rng.Next()).Take(count).ToList())
            .ToList();
        LateBombStart = rng.Next(4);
    }

    public IReadOnlyList<Vector3> UpheavalBombs =>
    [
        FromJumpFrame(new Vector3(-11f, 0f, 11f)),
        FromJumpFrame(new Vector3(-11f, 0f, -11f)),
        FromJumpFrame(new Vector3(-5f, 0f, 5f)),
        FromJumpFrame(new Vector3(-5f, 0f, -5f)),
        FromJumpFrame(new Vector3(-12f, 0f, -5f * SafeSide)),
    ];

    // Gaol line on the waymarks along Titan's axis, numbered from Titan's side.
    public Vector2 GaolSpot(int order) => FromJumpFrame(order switch
    {
        0 => new Vector2(6.7f, 0.5f * SafeSide),
        1 => new Vector2(0f, 1.0f * SafeSide),
        _ => new Vector2(-6.7f, 1.0f * SafeSide),
    });

    public Vector3 SixthBomb => FromJumpFrame(new Vector3(-12f, 0f, 5f * SafeSide));

    public Vector3 LateBomb(int order) => AtBearing(LateBombBearings[(LateBombStart + order) % 4], 3f * MathF.Sqrt(2f));

    public Vector2 FromJumpFrame(Vector2 eastFrame)
    {
        var rad = (FirstJumpBearing - 90f) * MathF.PI / 180f;
        var (sin, cos) = MathF.SinCos(rad);
        return new Vector2(eastFrame.X * cos - eastFrame.Y * sin, eastFrame.X * sin + eastFrame.Y * cos);
    }

    public Vector3 FromJumpFrame(Vector3 eastFrame)
    {
        var flat = FromJumpFrame(new Vector2(eastFrame.X, eastFrame.Z));
        return new Vector3(flat.X, eastFrame.Y, flat.Y);
    }

    // Compass degrees: 0 = north (-Z), 90 = east (+X).
    public static Vector3 AtBearing(float bearingDegrees, float radius)
    {
        var rad = bearingDegrees * MathF.PI / 180f;
        return new Vector3(radius * MathF.Sin(rad), 0f, -radius * MathF.Cos(rad));
    }

    public static float BearingOf(Vector2 direction) => (MathF.Atan2(direction.X, -direction.Y) * 180f / MathF.PI + 360f) % 360f;

    public static float FacingCentre(Vector3 from) => MathF.Atan2(-from.X, -from.Z);

    public static float Facing(Vector3 from, Vector3 to) => MathF.Atan2(to.X - from.X, to.Z - from.Z);
}
