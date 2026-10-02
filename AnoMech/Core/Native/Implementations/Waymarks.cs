using System;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Native.Interfaces;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace AnoMech.Core.Native.Implementations;

// Writes MarkingController._fieldMarkers directly instead of going through PlacePreset /
// ClearFieldMarkers: those are gated by territory ("No markers allowed in territory", return code
// 5). The renderer reads _fieldMarkers each frame, so direct writes place client-side markers
// anywhere.
internal sealed unsafe class Waymarks : IWaymarks
{
    private const int SlotCount = 8;

    public bool Set(WaymarkSlot slot, Vector3 position)
    {
        var idx = (int)slot;
        if (idx < 0 || idx >= SlotCount) return true;
        var controller = MarkingController.Instance();
        if (controller == null) return false;
        ref var marker = ref controller->FieldMarkers[idx];
        marker.Position = position;
        marker.X = (int)MathF.Round(position.X * 1000f);
        marker.Y = (int)MathF.Round(position.Y * 1000f);
        marker.Z = (int)MathF.Round(position.Z * 1000f);
        marker.Active = true;
        return true;
    }

    public void ClearAll()
    {
        var controller = MarkingController.Instance();
        if (controller == null) return;
        for (int i = 0; i < SlotCount; i++)
            controller->FieldMarkers[i].Active = false;
    }
}
