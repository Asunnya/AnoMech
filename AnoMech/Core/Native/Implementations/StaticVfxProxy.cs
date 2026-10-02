using AnoMech.Core.Native.Interfaces;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace AnoMech.Core.Native.Implementations;

internal sealed unsafe class StaticVfxProxy(VfxObject* vfx) : IStaticVfxProxy
{
    public void Remove() => vfx->CleanupRender();
}
