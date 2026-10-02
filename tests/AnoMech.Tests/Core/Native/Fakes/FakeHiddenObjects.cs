using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Tests;

// The fake zone has no objects of its own.
internal sealed class FakeHiddenObjects : IHiddenObjects
{
    public ushort? Hide(uint baseId) => null;
    public void Restore(ushort objectIndex, uint baseId) { }
}
