using System;
using System.Numerics;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.M9s.Deathmatch;

// One Sanguine Scratch cycle: which cone set opens it, which way both bats sweep the rim, and which
// group's bat ends as the 7y circle (Breakdown Drop); the other ends as the 4-15y donut.
public sealed record DeathmatchCycle(float FirstConeOffset, int Spin, bool Group1BatIsCircle);

// Per-run randomization for Undead Deathmatch.
//
// Measured from five pulls of one log. Two 6y towers take four players each, on the north/south or
// west/east axis; each tower's group is then leashed to a bat that spawns on it. During each
// Sanguine Scratch both bats sweep 180° around the 12y rim together (15.1°/s), and the group follows
// its bat; each bat then goes off as a circle or a donut where it stops. Random per cycle in the log:
// the cone set that opens, the sweep direction, and which bat is the circle.
//
// A leash stretched past ~10y (the log's long-tether flips land at 10.1-13.3y) turns long and sets
// off Explosion on its player right away and every 3s while it stays long, with Damage Down; the
// log's hits run up to 60% of max HP. That a longer leash hits harder is the players' account, not
// something the log can separate from mitigation, so the scaling past 10y is this sim's guess.
//
// Toxic / Hector: G1 takes the north (or west) tower, G2 the south (or east).
public sealed class M9sDeathmatchState
{
    public const float TowerDistance = 12f;
    public const float TowerRadius = 6f;
    public const float BatSweepDegreesPerSecond = 15.09f;
    public const float CircleRadius = 7f;
    public const float DonutInnerRadius = 4f;
    public const float DonutOuterRadius = 15f;
    public const float LeashSlack = 10f;

    public static readonly PartyRole[][] Groups =
    [
        [PartyRole.MainTank, PartyRole.RegenHealer, PartyRole.MeleeDpsA, PartyRole.PhysRangedDps],
        [PartyRole.OffTank, PartyRole.ShieldHealer, PartyRole.MeleeDpsB, PartyRole.CasterDps],
    ];

    public static readonly float[] SweepStartAt = [14.47f, 32.80f];
    public static readonly float[] SweepEndAt = [26.40f, 44.73f];
    public static readonly float[][] WaveAt = [[14.85f, 17.27f, 19.67f, 22.05f, 24.47f], [33.19f, 35.59f, 37.99f, 40.38f, 42.78f]];

    public bool NorthSouth { get; }
    public DeathmatchCycle[] Cycles { get; }
    public M9sSatisfied Satisfied { get; } = new(8);
    public PartyRole BrutalRainTarget { get; }

    public M9sDeathmatchState(M9sDeathmatchStateOverrides overrides)
    {
        var rng = new Rng();
        NorthSouth = overrides.NorthSouth ?? rng.NextObj(true, true, true, true, false);
        Cycles =
        [
            new DeathmatchCycle(rng.NextObj(0f, 22.5f), rng.NextSign(), rng.NextBool()),
            new DeathmatchCycle(rng.NextObj(0f, 22.5f), rng.NextSign(), rng.NextBool()),
        ];
        BrutalRainTarget = rng.NextObj(PartyRole.RegenHealer, PartyRole.ShieldHealer);
    }

    // Compass bearing of each group's tower; G1 north or west, G2 opposite.
    public float TowerBearing(int group) => (NorthSouth ? 0f : 270f) + group * 180f;

    public Vector3 TowerPosition(int group) => AtBearing(TowerBearing(group), TowerDistance);

    public float BatBearing(int group, float time)
    {
        var bearing = TowerBearing(group);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            var swept = Math.Clamp(time - SweepStartAt[cycle], 0f, SweepEndAt[cycle] - SweepStartAt[cycle]) * BatSweepDegreesPerSecond;
            bearing += Cycles[cycle].Spin * MathF.Min(swept, 180f);
        }
        return Wrap(bearing);
    }

    public float BatFinalBearing(int group, int cycle) => BatBearing(group, SweepEndAt[cycle] + 0.1f);

    public bool BatIsCircle(int group, int cycle) => (group == 0) == Cycles[cycle].Group1BatIsCircle;

    public static Vector3 AtBearing(float bearingDegrees, float radius)
    {
        var rad = bearingDegrees * MathF.PI / 180f;
        return new Vector3(radius * MathF.Sin(rad), 0f, -radius * MathF.Cos(rad));
    }

    public static float FacingAlongRim(float bearingDegrees, int spin) => spin * MathF.PI / 2f - bearingDegrees * MathF.PI / 180f;

    public static float Wrap(float degrees) => ((degrees % 360f) + 360f) % 360f;

    public static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
