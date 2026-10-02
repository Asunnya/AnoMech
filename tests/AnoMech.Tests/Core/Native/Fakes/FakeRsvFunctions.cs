using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Tests;

internal sealed class FakeRsvFunctions : IRsvFunctions
{
    public void Add(string rsvKey, string resolved) { }
    public void AddRaw(string rsvKey, byte[] valueBytes) { }
}
