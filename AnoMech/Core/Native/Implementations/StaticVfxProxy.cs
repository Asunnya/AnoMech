using System.Numerics;
using AnoMech.Core.Native.Interfaces;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace AnoMech.Core.Native.Implementations;

internal sealed unsafe class StaticVfxProxy(VfxObject* vfx) : IStaticVfxProxy
{
    // Flag 0x2 marks it dirty, as at spawn.
    public void SetScale(Vector3 scale)
    {
        vfx->Scale = scale;
        vfx->Flags |= 0x2;
    }

    public void Remove() => vfx->CleanupRender();
}
