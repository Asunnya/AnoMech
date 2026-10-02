using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Geometry;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;

namespace AnoMech.Core.Map;

// Per-frame arena fence. Walks every active party member each tick (player
// included, since SimParty exposes the player slot through ActiveMembers) and
// kills anyone outside its IArenaShape.
//
// A circular fence also spawns a floor-ring omen VFX at the boundary so the limit is
// visible; a square one relies on the arena's own walls (map effects) for that.
//
// Added to SimWorld.children via MapController.EnforceArenaBoundary so it gets
// cleared as a normal scenario child on Reset.
internal sealed class SimArenaBoundary : ISimObject
{
    // Donut omen has a fixed inner/outer ratio of 0.82. Scale by radius/0.82 so the
    // inner edge aligns with the kill boundary (outer edge extends ~4.4y beyond it).
    private const string RingVfxPath = "vfx/omen/eff/gl_sircle_1109w.avfx";

    private readonly SimParty party;
    private readonly IArenaShape shape;
    private readonly string cause;
    private readonly IStaticVfxProxy? ringVfx;

    public bool IsAlive => true;
    public bool IsActive => true;

    internal SimArenaBoundary(SimParty party, SimWorld world, IArenaShape shape, string cause, bool showVfx = true)
    {
        this.party = party;
        this.shape = shape;
        this.cause = cause;

        if (showVfx && shape is CircleArena circle && Natives.Data.FileExists(RingVfxPath))
            ringVfx = Natives.Vfx.SpawnStatic(RingVfxPath, new Placement(world.ScenarioOrigin, 0f), new Vector3(circle.Radius / 0.82f, 1f, circle.Radius / 0.82f));
    }

    // Shared by the per-frame fence and external callers (teleport-to-spawn on reset)
    // so they always agree.
    internal bool IsOutside(Vector3 local) => shape.IsOutside(local);

    public void Tick(float deltaSeconds)
    {
        // Member positions are scenario-local; the boundary is centered on local zero.
        foreach (var member in party.ActiveMembers())
        {
            if (IsOutside(member.Position)) member.Die(cause);
        }
    }

    public void Despawn()
    {
        ringVfx?.Remove();
    }
}
