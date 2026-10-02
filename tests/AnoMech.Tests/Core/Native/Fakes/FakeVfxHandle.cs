using System.Numerics;
using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Tests;

internal sealed class FakeVfxHandle : IActorVfxProxy, IStaticVfxProxy
{
    public void SetScale(Vector3 scale) { }
    public void Remove() { }
}
