using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.M9s.M9sConstants;

namespace AnoMech.Scenarios.M9s;

// Sanguine Scratch: eight 30° cones from the boss, 45° apart, fired in waves about 2.4s apart. Each
// wave sits 22.5° off the last, so the safe gaps flip between two sets every wave. Only the first
// wave is telegraphed; the rest are instant. Which set comes first is random in the log.
public sealed class M9sSanguineScratch(DamageSolver damage, M9sSatisfied satisfied, Func<Placement, SimEnemy?> spawnHelper)
{
    public const float ConeHalfAngleDegrees = 15f;
    private const int ConeCount = 8;

    private readonly List<SimEnemy> firstWave = [];

    public static float ConeCentre(float firstOffset, int wave, int index) => firstOffset + wave % 2 * 22.5f + index * 45f;

    // Middle of the gap between two cones of `wave`: the only bearings that wave leaves safe.
    public static float GapCentre(float firstOffset, int wave, int index) => ConeCentre(firstOffset, wave, index) + 22.5f;

    public static bool IsInsideCone(float firstOffset, int wave, Vector3 point, float marginDegrees)
    {
        if (new Vector2(point.X, point.Z).Length() < 0.5f) return true;
        var bearing = MathF.Atan2(point.X, -point.Z) * 180f / MathF.PI;
        for (var i = 0; i < ConeCount; i++)
        {
            var delta = MathF.Abs(((bearing - ConeCentre(firstOffset, wave, i)) % 360f + 540f) % 360f - 180f);
            if (delta <= ConeHalfAngleDegrees + marginDegrees) return true;
        }
        return false;
    }

    public void CastFirstWave(Vector3 boss, float firstOffset)
    {
        firstWave.Clear();
        for (var i = 0; i < ConeCount; i++)
            if (spawnHelper(new Placement(boss, RotationFacing(ConeCentre(firstOffset, 0, i)))) is { } cone)
            {
                cone.Cast(ActionId.SanguineScratchFirst, castSeconds: 2.7f);
                firstWave.Add(cone);
            }
    }

    public void ResolveWave(Vector3 boss, float firstOffset, int wave)
    {
        if (wave == 0)
        {
            foreach (var cone in firstWave) Hit(cone, ActionId.SanguineScratchFirst);
            return;
        }
        for (var i = 0; i < ConeCount; i++)
        {
            var cone = spawnHelper(new Placement(boss, RotationFacing(ConeCentre(firstOffset, wave, i))));
            cone?.Cast(ActionId.SanguineScratchRepeat, castSeconds: 0f, animationLock: 0f);
            Hit(cone, ActionId.SanguineScratchRepeat);
        }
    }

    private void Hit(SimEnemy? cone, uint actionId) =>
        satisfied.AddFor(damage.Resolve(cone, actionId, [DamageType.Lethal], [], size: ConeHalfAngleDegrees * MathF.PI / 180f));

    private static float RotationFacing(float bearingDegrees) => MathF.PI - bearingDegrees * MathF.PI / 180f;
}
