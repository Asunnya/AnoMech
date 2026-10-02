using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Core.Native.Implementations;

// Client-side party-sign writer. Bypasses the network path (the canonical
// MarkingController.MarkObject member function broadcasts to the party); we
// just stamp the GameObjectId directly into _markers, which is what the
// nameplate renderer reads each frame. Empty/cleared slots are 0.
//
// Lemegeton drives signs by sig-scanning the same member function; for a
// single-player simulator we don't need network broadcast, so direct writes
// (mirroring Waymarks) are simpler and have no side effects.
internal sealed unsafe class Markings : IMarkings
{
    private const int SlotCount = 17;

    public void Set(Sign sign, GameObjectId target)
    {
        var idx = (int)sign;
        if (idx < 0 || idx >= SlotCount) return;
        var ctrl = MarkingController.Instance();
        if (ctrl == null) return;
        ctrl->Markers[idx] = target;
    }

    public void Clear(Sign sign)
    {
        var idx = (int)sign;
        if (idx < 0 || idx >= SlotCount) return;
        var ctrl = MarkingController.Instance();
        if (ctrl == null) return;
        ctrl->Markers[idx] = default;
    }

    public void ClearAll()
    {
        var ctrl = MarkingController.Instance();
        if (ctrl == null) return;
        for (int i = 0; i < SlotCount; i++)
            ctrl->Markers[i] = default;
    }
}
