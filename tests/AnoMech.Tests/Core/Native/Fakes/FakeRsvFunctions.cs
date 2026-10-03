using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Tests;

// Stands in for Dalamud's RsvResolver: once a key is seeded, sheet text reads resolve it.
internal sealed class FakeRsvFunctions : IRsvFunctions
{
    private readonly Dictionary<string, string> resolved = new();

    public void Add(string rsvKey, string resolved) => this.resolved[rsvKey] = resolved;
    public void AddRaw(string rsvKey, byte[] valueBytes) { }

    public string Resolve(string text) => resolved.TryGetValue(text, out var value) ? value : text;
}
