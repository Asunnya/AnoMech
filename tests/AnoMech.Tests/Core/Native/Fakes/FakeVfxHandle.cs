using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Tests;

internal sealed class FakeVfxHandle : IActorVfxProxy, IStaticVfxProxy
{
    public void Remove() { }
}
