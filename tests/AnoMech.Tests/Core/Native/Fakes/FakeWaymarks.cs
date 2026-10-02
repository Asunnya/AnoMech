using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Tests;

internal sealed class FakeWaymarks : IWaymarks
{
    public bool Set(WaymarkSlot slot, Vector3 position) => true;
    public void ClearAll() { }
}
