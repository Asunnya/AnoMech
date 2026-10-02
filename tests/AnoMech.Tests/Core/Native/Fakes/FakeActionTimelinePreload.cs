using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Tests;

internal sealed class FakeActionTimelinePreload : IActionTimelinePreload
{
    public void Preload(IEnumerable<(ushort Id, string Key)> rows, string tag) { }
}
