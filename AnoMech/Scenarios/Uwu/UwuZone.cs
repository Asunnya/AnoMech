using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Uwu;

// The primal phases carry a "P<n>" menu prefix; the Ultima phase's empty Name has none. Every
// phase shares the fight's one BGM track and weather.
public sealed class UwuZone : IZone
{
    public static readonly UwuZone Instance = new();
    public static readonly Phase Garuda = new(Instance, "P1", 95, 547);
    public static readonly Phase Ultima = new(Instance, "", 95, 547);

    public string Name => "The Weapon's Refrain";
    public uint TerritoryId => 777;
    public Vector3 Origin => new(100f, 0f, 100f);
    public byte Level => UwuConstants.Level;
    public ushort ItemLevel => UwuConstants.ItemLevel;

    public IReadOnlyList<WaymarkLayout> WaymarkPresets { get; } =
        [new WaymarkLayout("Default", UwuConstants.NaurWaymarks)];

    public void Run(SimWorld world) => world.EnforceArenaBoundary(UwuConstants.Geometry.ArenaRadius);
}
