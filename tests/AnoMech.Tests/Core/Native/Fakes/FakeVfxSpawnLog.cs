using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Tests;

internal sealed class FakeVfxSpawnLog : IVfxSpawnLog
{
    public long Frame => 0;
    public void Enable() { }
    public void Disable() { }
    public void Tick() { }
}
