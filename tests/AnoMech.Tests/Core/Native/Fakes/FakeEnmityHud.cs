using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;

namespace AnoMech.Tests;

internal sealed class FakeEnmityHud : IEnmityHud
{
    public void Refresh(IEnumerable<SimEnemy> enemies, float deltaSeconds) { }
    public void Clear() { }
}
