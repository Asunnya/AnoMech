using System;

namespace AnoMech.Core;

internal static class MathUtil
{
    // Wraps any rotation into the half-open range (-π, +π]. The game stores
    // facings unbounded; some math (atan2, accumulated rotates from MoveTo)
    // can drift outside that band and trip animation/state code that assumes
    // it. Normalize on every write rather than relying on every caller.
    public static float NormalizeRotation(float r)
    {
        r = MathF.IEEERemainder(r, 2f * MathF.PI);
        if (r <= -MathF.PI) r = MathF.PI;
        return r;
    }

    // Steps `current` toward `target` by at most `maxDelta` radians, going the
    // shorter way around the circle. Used by SimEnemy/SimNetworkPuppet's
    // network-position smoothing to interpolate rotation the same way they
    // already interpolate position, instead of writing the target rotation raw
    // every tick (see NetworkAngularCatchUpSpeed's doc comment on SimEnemy for
    // why that read as "laggy" turning to a peer).
    public static float StepRotation(float current, float target, float maxDelta)
    {
        var delta = NormalizeRotation(target - current);
        if (MathF.Abs(delta) <= maxDelta) return target;
        return NormalizeRotation(current + MathF.Sign(delta) * maxDelta);
    }

    public static ushort QuantizeRotation(float degrees)
    {
        return (ushort)((degrees + MathF.PI) / (2 * MathF.PI) * ushort.MaxValue);
    }

    public static ushort QuantizePosition(float value)
    {
        return (ushort)((value + 1000) * 100 * 0.32767f);
    }
}
