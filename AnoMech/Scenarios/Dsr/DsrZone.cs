using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Geometry;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Dsr;

// The territory's BGM is a null track; the fight's music is InstanceContent 30106's.
public sealed class DsrZone : IZone
{
    private const ushort BgmId = 920;

    public static readonly DsrZone Instance = new();
    public static readonly Phase Knights = new(Instance, "P1", null, BgmId);

    public string Name => "Dragonsong's Reprise";
    public uint TerritoryId => 968;
    public Vector3 Origin => new(100f, 0f, 100f);
    public byte Level => DsrConstants.Level;
    public ushort ItemLevel => DsrConstants.ItemLevel;

    public IReadOnlyList<WaymarkLayout> WaymarkPresets { get; } =
        [new WaymarkLayout("DSR Phase 1", DsrConstants.Phase1Waymarks), new WaymarkLayout("NAUR", DsrConstants.NaurWaymarks)];

    public void Run(SimWorld world) =>
        world.EnforceArenaBoundary(new SquareArena(DsrConstants.Geometry.ArenaHalfWidth), "Touched the arena wall");
}
