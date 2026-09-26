using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.Umad.P5Flood;

// Two crossing diagonal lines (NE-SW and NW-SE) each march across the arena through 4 points
// over the same 4 ticks; a point from each resolves together every tick. Forward = NE->SW for
// the NE-SW line, NW->SE for the NW-SE line (see UmadP5FloodScenario.ArmPoints); Reversed starts
// from the opposite end.
public enum FloodDirection
{
    Random,
    Forward,
    Reversed,
}

// How the invisible wave/stack cast carriers are built and hidden (the "waves cut off" A/B).
// ChaosModelHidden drops the DrawObject entirely, so nothing can play on it; EmptyHuman builds a
// Midlander, so a real helper is not an empty human.
public enum FloodCarrierMode
{
    ChaosDrawHidden,    // BNpcBase.Chaos, DrawObject.IsVisible=false
    ChaosModelHidden,   // BNpcBase.Chaos, RenderFlags Model|Nameplate
    ChaosVisible,       // BNpcBase.Chaos shown as-is (diagnostic only)
    EmptyHuman,         // BNpcBase.KefkaHelper (ModelChara 480) with an all-zero CustomizeData
    RealPacket,         // the captured real 9020 NpcSpawn through the engine's own handler (default)
}

// How each wave's resolve reaches the client. Every real wave is one ActionEffect8 with the
// caster as animation target, 0 targets and a zero target position. The two holds keep the wave
// timeline (10690, a 90-frame VFX-only track) from leaving the base slot for its 3.0s.
public enum FloodWaveDelivery
{
    NativeEffect,     // ActionEffectHandler.Receive, real header shape, zero target position (default)
    RawPacket,        // the captured real ActionEffect8 bytes through the client's own dispatcher; placed no timeline in two runs
    EffectHoldLoop,   // NativeEffect, then the timeline re-queued as its own loop for 3.0s
    EffectHoldBase,   // NativeEffect, then TimelineContainer.BaseOverride = 10690 for 3.0s
    DirectTimeline,   // the sequencer's own PlayTimeline(10690), no action effect at all (diagnostic)
}

// Random leaves a field randomized at scenario start.
public sealed class UmadP5FloodStateOverrides
{
    public FloodDirection LineNeSw { get; set; } = FloodDirection.Random;
    public FloodDirection LineNwSe { get; set; } = FloodDirection.Random;

    // Which diagonal telegraphs first (tick0/tick2 vs tick1/tick3).
    public bool? NeSwFirst { get; set; } = null; // null = random

    // The stack target for every tick; the real fight rolls a fresh non-tank per tick.
    public PartyRole? AnchorRole { get; set; } = null; // null = random per tick

    // The party's starting cardinal quadrant (0=N,1=E,2=S,3=W) and rotation direction; derived
    // from the line rolls when null.
    public int? StartQuadrant { get; set; } = null;
    public bool? RotationClockwise { get; set; } = null;

    // RealPacket: the carrier the engine builds from the real helper's own NpcSpawn bytes, the
    // real fight's by construction. The Chaos/EmptyHuman modes stay as the A/B.
    public FloodCarrierMode CarrierMode { get; set; } = FloodCarrierMode.RealPacket;

    // Debug: hold each wave carrier in AnimLock for the wave timeline's length after its resolve.
    public bool WaveAnimLock { get; set; } = false;

    public FloodWaveDelivery WaveDelivery { get; set; } = FloodWaveDelivery.NativeEffect;

    // Debug: also fire each wave from Kefka (a full skeleton at the centre), to tell a carrier
    // problem from a timeline one.
    public bool WaveOnKefka { get; set; } = false;

    // Preload the three gimmick timelines at scenario start; without it the wave timeline's
    // clock sat at 0.00 and was dropped 3-4 frames later.
    public bool PreloadWaveTimelines { get; set; } = true;

    // Debug: call LoadTimelineResources on the wave timeline the moment the effect places it.
    public bool WaveForceLoad { get; set; } = false;

    // Debug: trace every VFX the client creates and destroys for the whole run. Off by default:
    // it hooks a game destructor that fires for every VFX in the world, not just this fight's.
    public bool VfxRenderLog { get; set; } = false;
}
