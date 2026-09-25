using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AnoMech.Scenarios.M9s.Flails;

// One Plummet round: two tank towers and the Electrocution puddle that becomes the doornail.
public sealed record FlailRound(Vector3 NorthTower, Vector3 SouthTower, Vector3 Doornail);

// Per-run randomization and timing for the flail phase.
//
// Measured from six pulls of one log. Towers and doornails only ever land on eight cells (x = ±5,
// z = ±4, ±16), but the three rounds come as a whole sequence, and the log holds four distinct
// ones (two of them twice); one is picked with the log's weights. The saws never vary (see
// M9sFlailsSawData). The electrified puddle appears 0.7s after Electrocution lands, starts growing
// at 0.5y/s from 3y 3.3s later, and goes out 0.45s after the doornail dies (39.08 / 59.63 / 74.52 in
// the clear, 13-16s after it turns targetable). A doornail still up when the next round's towers
// land (or the closing Sadistic Screech starts) wipes the party.
public sealed class M9sFlailsState
{
    public const float TowerRadius = 3f;
    public const float ElectrocutionRadius = 3f;
    public const float CorridorHalfWidth = 10f;
    public const float CorridorHalfLength = 20f;

    public static readonly float[] RoundCastAt = [17.26f, 35.43f, 53.58f];
    public static readonly float[] RoundResolveAt = [24.26f, 42.42f, 60.57f];
    public static readonly float[] PuddleAppearAt = [24.96f, 43.39f, 61.38f];
    public static readonly float[] PuddleGrowAt = [28.26f, 46.38f, 64.51f];
    public static readonly float[] DoornailDeadlineAt = [42.42f, 60.57f, 78.61f];
    public const float PuddleOutDelay = 0.45f;

    private readonly float[] puddleEndAt = [.. DoornailDeadlineAt];

    private static readonly IReadOnlyList<IReadOnlyList<FlailRound>> Sequences =
    [
        [Round(5, -16, -5, 4, 5, 4), Round(5, 16, 5, -4, -5, -4), Round(-5, 4, -5, -16, 5, -16)],
        [Round(-5, 16, -5, -4, -5, 4), Round(5, 4, 5, -16, 5, -4), Round(-5, 16, -5, -4, 5, 16)],
        [Round(5, -4, -5, 16, -5, -4), Round(-5, 4, -5, -16, 5, 4), Round(5, 16, 5, -4, -5, 16)],
        [Round(5, 4, 5, -16, 5, -4), Round(-5, 16, -5, -4, -5, 4), Round(5, 4, 5, -16, -5, -16)],
    ];

    public IReadOnlyList<FlailRound> Rounds { get; }
    public int Layout { get; }
    public M9sSatisfied Satisfied { get; } = new(4);

    public M9sFlailsState(M9sFlailsStateOverrides overrides)
    {
        var rng = new Rng();
        Layout = overrides.Layout ?? rng.NextObj(0, 1, 1, 2, 3, 3);
        Rounds = Sequences[Layout];
    }

    public static int LayoutCount => Sequences.Count;

    // Towers are named by which of the pair sits further north (-Z); Toxic sends MT north, OT south.
    private static FlailRound Round(float ax, float az, float bx, float bz, float nailX, float nailZ)
    {
        var a = new Vector3(ax, 0f, az);
        var b = new Vector3(bx, 0f, bz);
        return az < bz
            ? new FlailRound(a, b, new Vector3(nailX, 0f, nailZ))
            : new FlailRound(b, a, new Vector3(nailX, 0f, nailZ));
    }

    public void PuddleGoesOutAt(int round, float time) => puddleEndAt[round] = time;

    public float? PuddleRadius(int round, float time)
    {
        if (time < PuddleAppearAt[round] || time >= puddleEndAt[round]) return null;
        return ElectrocutionRadius + MathF.Max(0f, time - PuddleGrowAt[round]) / 2f;
    }

    public static int? TowerRoundAt(float time)
    {
        for (var round = 0; round < 3; round++)
            if (time >= RoundCastAt[round] && time <= RoundResolveAt[round] + 0.05f) return round;
        return null;
    }

    public static int? LatestRoundCastBy(float time)
    {
        int? latest = null;
        for (var round = 0; round < 3; round++)
            if (time >= RoundCastAt[round]) latest = round;
        return latest;
    }

    public static bool IsInsideSaw(SawHit hit, Vector3 point, float margin)
    {
        var length = hit.IsBig ? 10f : 5f;
        var forwardX = MathF.Sin(hit.Rotation);
        var forwardZ = MathF.Cos(hit.Rotation);
        var dx = point.X - hit.Position.X;
        var dz = point.Z - hit.Position.Z;
        var along = dx * forwardX + dz * forwardZ;
        var across = dx * forwardZ - dz * forwardX;
        return along >= -margin && along <= length + margin && MathF.Abs(across) <= 2.5f + margin;
    }

    public static IReadOnlyList<SawHit> SawHitsBetween(float from, float to) =>
        M9sFlailsSawData.Hits.Where(h => h.At > from && h.At <= to).ToList();

    // Whether standing at `point` at `time` gets a player killed, for anything this phase can place
    // on them; `saws` only needs the hits near `time`. Tanks may stand in towers.
    public bool IsLethal(Vector3 point, float time, bool isTank, float margin, IReadOnlyList<SawHit> saws)
    {
        if (MathF.Abs(point.X) > CorridorHalfWidth - 0.8f || MathF.Abs(point.Z) > CorridorHalfLength - 0.8f) return true;
        foreach (var hit in saws)
            if (MathF.Abs(hit.At - time) < 0.13f && IsInsideSaw(hit, point, margin)) return true;
        for (var round = 0; round < 3; round++)
        {
            var nail = Rounds[round].Doornail;
            if (PuddleRadius(round, time) is { } radius && FlatDistance(point, nail) <= radius + margin) return true;
            if (time < RoundCastAt[round] || time > RoundResolveAt[round] + 0.05f) continue;
            if (FlatDistance(point, nail) <= ElectrocutionRadius + margin) return true;
            if (isTank) continue;
            if (FlatDistance(point, Rounds[round].NorthTower) <= TowerRadius + margin) return true;
            if (FlatDistance(point, Rounds[round].SouthTower) <= TowerRadius + margin) return true;
        }
        return false;
    }

    // Towers and doornails share eight cells, numbered west column first, north to south; the map
    // effects for both are indexed by it.
    public static int CellIndex(Vector3 cell) =>
        (cell.X > 0f ? 4 : 0) + Array.IndexOf(CellRows, (int)MathF.Round(cell.Z));

    private static readonly int[] CellRows = [-16, -4, 4, 16];

    public static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
