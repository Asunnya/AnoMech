using AnoMech.Core.Native.Implementations.Pointers;
using AnoMech.Core.Native.Interfaces;
using FFXIVClientStructs.FFXIV.Client.Graphics.Vfx;

namespace AnoMech.Core.Native.Implementations;

internal sealed unsafe class ActorVfxProxy(VfxData* vfx) : IActorVfxProxy
{
    public void Remove() => VfxDataPointers.Dtor(vfx, 0);
}
