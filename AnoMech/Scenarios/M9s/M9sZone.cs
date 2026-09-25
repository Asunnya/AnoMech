using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;

namespace AnoMech.Scenarios.M9s;

// Single phase; empty Name = no menu prefix. Weather stays the territory's own (Fair Skies, its
// only WeatherRate entry). The territory's BGM is a null track: the fight's music is the duty's,
// InstanceContent 30155's BGM row.
public sealed class M9sZone : IZone
{
    private const ushort BgmId = 20238;

    public static readonly M9sZone Instance = new();
    public static readonly Phase Fight = new(Instance, "", null, BgmId);

    public string Name => "AAC Heavyweight M1 (Savage)";
    public string Category => "Savage";
    public string? Expansion => "Dawntrail";
    public uint TerritoryId => 1321;
    public Vector3 Origin => new(100f, 0f, 100f);

    public IReadOnlyList<WaymarkLayout> WaymarkPresets { get; } =
        [new WaymarkLayout("NAUR", M9sConstants.NaurWaymarks)];
}
