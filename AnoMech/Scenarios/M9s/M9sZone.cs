using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;

namespace AnoMech.Scenarios.M9s;

// Single phase; empty Name = no menu prefix. Weather and BGM are left at the territory's own
// until they are read from a log.
public sealed class M9sZone : IZone
{
    public static readonly M9sZone Instance = new();
    public static readonly Phase Fight = new(Instance, "", null, 0);

    public string Name => "AAC Heavyweight M1 (Savage)";
    public uint TerritoryId => 1321;
    public Vector3 Origin => new(100f, 0f, 100f);

    public IReadOnlyList<WaymarkLayout> WaymarkPresets { get; } =
        [new WaymarkLayout("NAUR", M9sConstants.NaurWaymarks)];
}
