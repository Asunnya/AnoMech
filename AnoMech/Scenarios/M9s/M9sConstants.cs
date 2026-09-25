using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;

namespace AnoMech.Scenarios.M9s;

public class M9sConstants
{
    public static class Geometry
    {
        // The opening arena is a 40y square; the circle (and later rectangle) come from map effects.
        public const float ArenaHalfWidth = 20f;
    }

    public static IReadOnlyList<Waymark> NaurWaymarks =>
    [
        new(WaymarkSlot.A,     new Vector3(  0f, 0f, -10f)),
        new(WaymarkSlot.B,     new Vector3( 10f, 0f,   0f)),
        new(WaymarkSlot.C,     new Vector3(  0f, 0f,  10f)),
        new(WaymarkSlot.D,     new Vector3(-10f, 0f,   0f)),
        new(WaymarkSlot.One,   new Vector3(-10f, 0f, -10f)),
        new(WaymarkSlot.Two,   new Vector3( 10f, 0f, -10f)),
        new(WaymarkSlot.Three, new Vector3( 10f, 0f,  10f)),
        new(WaymarkSlot.Four,  new Vector3(-10f, 0f,  10f)),
    ];

    public class BNpcBaseId
    {
        public const uint VampFatale = 19167;
        public const uint VampetteFatale = 19503;
        public const uint Helper = 9020;
    }

    public class BNpcNameId
    {
        public const uint VampFatale = 14300;
        public const uint VampetteFatale = 14501;
    }

    public class ActionId
    {
        public const uint KillerVoice = 45956;
        public const uint HardcoreCast = 45914;
        public const uint HardcoreSmall = 45951;
        public const uint VampStompCast = 45898;
        public const uint VampStomp = 45940;
        public const uint BatRing = 45900;
        public const uint BlastBeatBat = 45941;
        public const uint BlastBeatSpread = 45942;
        public const uint BrutalRainCast = 45917;
        public const uint BrutalRainHit = 45955;
    }

    public class StatusId
    {
        public const ushort CurseOfTheBombpyre = 4729;
        public const ushort MagicVulnerabilityUp = 2941;
    }

    public class VfxPath
    {
        // BatRing has no Action omen to reuse, so a thin donut omen (inner edge at 90% of the
        // outer) stands in for the curse ring; scaled by the ring radius, its outer edge is the hit line.
        public const string CurseRing = "vfx/omen/eff/gl_sircle_2018w.avfx";
    }

    public class LockonId
    {
        public const uint Tankbuster = 468;
        public const uint ShareMulti = 305;
    }
}
