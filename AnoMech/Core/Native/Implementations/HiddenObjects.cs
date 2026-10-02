using AnoMech.Core.Native.Interfaces;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace AnoMech.Core.Native.Implementations;

// The VisibilityFlags bits alone block interaction but not rendering; DisableDraw is what
// actually tears down the draw object.
internal sealed unsafe class HiddenObjects : IHiddenObjects
{
    public ushort? Hide(uint baseId)
    {
        foreach (var go in Plugin.ObjectTable)
        {
            if (go.BaseId != baseId) continue;
            var obj = (GameObject*)go.Address;
            obj->DisableDraw();
            obj->RenderFlags |= VisibilityFlags.Model | VisibilityFlags.Nameplate;
            return go.ObjectIndex;
        }
        return null;
    }

    public void Restore(ushort objectIndex, uint baseId)
    {
        var obj = (GameObject*)Plugin.ObjectTable.GetObjectAddress(objectIndex);
        if (obj == null || obj->BaseId != baseId) return;
        obj->RenderFlags &= ~(VisibilityFlags.Model | VisibilityFlags.Nameplate);
        obj->EnableDraw();
    }
}
