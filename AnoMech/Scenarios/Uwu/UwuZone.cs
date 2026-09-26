using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Uwu;

// The primal phases carry a "P<n>" menu prefix; the Ultima phase's empty Name has none. Every
// phase shares the fight's one BGM track.
public sealed class UwuZone : IZone
{
    public static readonly UwuZone Instance = new();
    // Each primal's sky is the weather of its trial arena (Howling Eye, Bowl of Embers); Ultimania
    // is Ultima's.
    private const byte GalesWeather = 28;
    private const byte HeatWavesWeather = 26;
    public static readonly Phase Garuda = new(Instance, "P1", GalesWeather, 547);
    public static readonly Phase Ifrit = new(Instance, "P2", HeatWavesWeather, 547);
    public static readonly Phase Ultima = new(Instance, "", 95, 547);

    public string Name => "The Weapon's Refrain";
    public uint TerritoryId => 777;
    public Vector3 Origin => new(100f, 0f, 100f);
    public byte Level => UwuConstants.Level;
    public ushort ItemLevel => UwuConstants.ItemLevel;

    public IReadOnlyList<WaymarkLayout> WaymarkPresets { get; } =
        [new WaymarkLayout("Standard", UwuConstants.StandardWaymarks), new WaymarkLayout("Naur", UwuConstants.NaurWaymarks)];

    public void Run(SimWorld world) => world.EnforceArenaBoundary(UwuConstants.Geometry.ArenaRadius);
}
