using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Ucob;

public sealed class UcobZone : IZone
{
    public static readonly UcobZone Instance = new();
    // Weather is left at the territory's own (WeatherRate 0 -> row 2); the BGM is the
    // "Answers" master track, which is what the Golden Bahamut phase plays.
    public static readonly Phase P5 = new(Instance, "P5", null, 226);

    public string Name => "The Unending Coil of Bahamut";
    public uint TerritoryId => 733;
    public Vector3 Origin => new(0f, 0f, 0f);
    public byte Level => UcobConstants.Level;
    public ushort ItemLevel => UcobConstants.ItemLevel;

    public IReadOnlyList<WaymarkLayout> WaymarkPresets { get; } =
        [new WaymarkLayout("Aether Markers", UcobConstants.AetherWaymarks)];

    // The zone loads with its default (P1/P3) arena geometry rather than the Golden Bahamut
    // floor — dressing each phase correctly means switching whole native layout layers, which
    // needs engine support this codebase doesn't have yet. Known but not implemented here.
    //
    // TODO: Spotted issue with rendering, arena appears dependent on inn the sim starts in
    //
    // One layer is suppressed regardless: with no real duty director active, the client-side
    // load activates every layer at once, so LGB layer 0x1360 (f1b4_t2_jari1 gravel ground
    // clutter, confirmed live to overlap whatever floor is actually meant to be visible)
    // z-fights against it — a rapid flicker. This is the one confirmed-safe, narrowly-scoped
    // fix; it doesn't attempt the rest of the P5 arena look.
    public void Run(SimWorld world)
    {
        world.EnforceArenaBoundary(UcobConstants.Geometry.ArenaRadius);
        world.Events.Add(1f, () => world.Map.SuppressLayer(0x1360));
    }
}
