using System;
using System.Collections.Generic;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios;

// Sim-only "when was this mitigation last used" bookkeeping, independent of the real
// ability's real recast timer. Wall-clock (Environment.TickCount64), not the scenario's own
// scaled clock -- a real person presses the button in real time regardless of EventTimeScale.
// Reset alongside the rest of a run's bookkeeping (Game.ResetInternal).
//
// Charge-aware: each use of a maxCharges > 1 ability starts its own independent recovery
// timer, up to maxCharges in flight at once (same as the real game's charge system).
// maxCharges=1 collapses to plain single-cooldown behavior.
public static class TankMitigationTracker
{
    private static readonly Dictionary<(PartyRole Role, ushort StatusId), Queue<long>> chargeRecoveryMs = new();

    public static void Reset() => chargeRecoveryMs.Clear();

    public static void RecordUse(PartyRole role, ushort statusId, float cooldown)
    {
        var key = (role, statusId);
        if (!chargeRecoveryMs.TryGetValue(key, out var queue))
            chargeRecoveryMs[key] = queue = new Queue<long>();
        queue.Enqueue(Environment.TickCount64 + (long)(cooldown * 1000f));
    }

    // Charges that have already recovered are dropped here rather than only at RecordUse
    // time, so IsAvailable stays accurate even if nothing gets used again for a while.
    private static void DropRecoveredCharges(Queue<long> queue)
    {
        var now = Environment.TickCount64;
        while (queue.Count > 0 && queue.Peek() <= now) queue.Dequeue();
    }

    public static bool IsAvailable(PartyRole role, ushort statusId, int maxCharges = 1)
    {
        if (!chargeRecoveryMs.TryGetValue((role, statusId), out var queue)) return true;
        DropRecoveredCharges(queue);
        return queue.Count < Math.Max(1, maxCharges);
    }

    // How long until the next charge recovers, or null if one's available right now --
    // purely informational (e.g. for a future UI), not used by IsAvailable itself.
    public static float? SecondsUntilNextCharge(PartyRole role, ushort statusId)
    {
        if (!chargeRecoveryMs.TryGetValue((role, statusId), out var queue) || queue.Count == 0) return null;
        DropRecoveredCharges(queue);
        if (queue.Count == 0) return null;
        return (queue.Peek() - Environment.TickCount64) / 1000f;
    }
}
