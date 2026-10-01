using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;

namespace AnoMech.Scenarios.Dsr;

public class DsrConstants
{
    public const byte Level = 90;
    public const ushort ItemLevel = 605;

    public static class Geometry
    {
        public const float ArenaHalfWidth = 22f;
    }

    // Numbers north and letters south are the Hyperdimensional Slash prey spots.
    public static IReadOnlyList<Waymark> Phase1Waymarks =>
    [
        new(WaymarkSlot.A, new Vector3(-6.669f, 0f, 3.162f)),
        new(WaymarkSlot.B, new Vector3(-3.186f, 0f, 6.708f)),
        new(WaymarkSlot.C, new Vector3(3.22f, 0f, 6.667f)),
        new(WaymarkSlot.D, new Vector3(6.599f, 0f, 3.18f)),
        new(WaymarkSlot.One, new Vector3(-6.862f, 0f, -3.595f)),
        new(WaymarkSlot.Two, new Vector3(-3.159f, 0f, -6.721f)),
        new(WaymarkSlot.Three, new Vector3(3.441f, 0f, -6.881f)),
        new(WaymarkSlot.Four, new Vector3(6.664f, 0f, -3.598f)),
    ];

    public static IReadOnlyList<Waymark> NaurWaymarks =>
    [
        new(WaymarkSlot.A, new Vector3(0f, 0f, -9.5f)),
        new(WaymarkSlot.B, new Vector3(13.123f, 0f, -13.458f)),
        new(WaymarkSlot.C, new Vector3(9.5f, 0f, 0f)),
        new(WaymarkSlot.D, new Vector3(12.75f, 0f, 12.508f)),
        new(WaymarkSlot.One, new Vector3(0f, 0f, 9.5f)),
        new(WaymarkSlot.Two, new Vector3(-12.852f, 0f, 12.929f)),
        new(WaymarkSlot.Three, new Vector3(-9.5f, 0f, 0f)),
        new(WaymarkSlot.Four, new Vector3(-12.782f, 0f, -12.875f)),
    ];

    public static Vector3 Phase1Waymark(WaymarkSlot slot) => Phase1Waymarks.First(w => w.Slot == slot).Offset;

    public static float BearingOf(Vector3 point) => (MathF.Atan2(point.X, -point.Z) * 180f / MathF.PI + 360f) % 360f;

    // Compass degrees: 0 = north (-Z), 90 = east (+X).
    public static Vector3 AtBearing(float bearingDegrees, float radius)
    {
        var rad = bearingDegrees * MathF.PI / 180f;
        return new Vector3(radius * MathF.Sin(rad), 0f, -radius * MathF.Cos(rad));
    }

    public class BNpcBaseId
    {
        public const uint Dummy = 9020;
        public const uint Adelphel = 12601;
        public const uint Grinnaux = 12602;
        public const uint Charibert = 12603;
        public const uint Thordan = 12604;
        public const uint Zephirin = 12592;
        public const uint Haurchefant = 13117;
        public const uint SpearOfTheFury = 11810;
        public const uint Brightsphere = 13070;
        public const uint AetherialTear = 13071;
    }

    public class BNpcNameId
    {
        public const uint Dummy = 108;
        public const uint Adelphel = 3634;
        public const uint Grinnaux = 3639;
        public const uint Charibert = 3642;
        public const uint Thordan = 3632;
        public const uint Zephirin = 3633;
        public const uint Haurchefant = 1455;
        public const uint SpearOfTheFury = 11320;
        public const uint Brightsphere = 4385;
        public const uint AetherialTear = 3293;
    }

    public class ActionId
    {
        public const uint ShiningBlade = 0x62CE;
        public const uint BrightFlare = 0x62CF;
        public const uint HoliestHallowing = 0x62D0;
        public const uint HolyShieldBash = 0x62D1;
        public const uint HolyBladedance = 0x62D3;
        public const uint HoliestOfHoly = 0x62D4;
        public const uint Execution = 0x62D5;
        public const uint HyperdimensionalSlash = 0x62D6;
        public const uint HyperdimensionalSlashLine = 0x62D7;
        public const uint HyperdimensionalSlashCone = 0x63EE;
        public const uint EmptyDimension = 0x62DA;
        public const uint FullDimension = 0x62DB;
        public const uint FaithUnmoving = 0x62DC;
        public const uint Heavensblaze = 0x62DD;
        public const uint Heavensflame = 0x62DE;
        public const uint HeavensflameHit = 0x62DF;
        public const uint HolyChain = 0x62E0;
        public const uint PlanarPrison = 0x62E1;
        public const uint SpearOfTheFury = 0x62E2;
        public const uint Shockwave = 0x62E3;
        public const uint PureOfHeart = 0x62E4;
        public const uint BrightwingedFlight = 0x6316;
        public const uint Brightwing = 0x6319;
        public const uint Skyblind = 0x631A;

        public const uint Interject = 7538;
        public const uint HeadGraze = 7551;
        public const uint ArmsLength = 7548;
        public const uint Surecast = 7559;
    }

    public class StatusId
    {
        public const ushort Stun = 0x95;
        public const ushort BurningChains = 0x301;
        public const ushort LightResistanceDown = 0x8E6;
        public const ushort Skyblind = 0xA65;
        public const ushort PlanarImprisonment = 0xA66;
        public const ushort FireResistanceDownII = 0xB56;
        public const ushort MagicVulnerabilityUp = 0xB7D;
        public const ushort DownForTheCount = 0xC5D;
    }

    public class TetherId
    {
        public const ushort BurningChains = 0x09;
        public const ushort PlanarPrison = 0x35;
        public const ushort HolyShieldBash = 0x54;
    }

    public class LockonId
    {
        public const uint HyperdimensionalSlash = 0xEA;
        public const uint Circle = 0x119;
        public const uint Triangle = 0x11A;
        public const uint Square = 0x11B;
        public const uint Cross = 0x11C;
    }

    // UNVERIFIED: replayed from the log's director traffic at each phase change; whether they
    // switch the arena scenery client-side was never observed.
    public static class ArenaDirector
    {
        public const uint Layout = 0x80000016;
        public const uint MapChange = 0x8000001F;
        public const uint Music = 0x80000004;
        public const uint KnightsLayout = 0x01;
        public const uint PrisonLayout = 0x14;
        public const uint ThordanLayout = 0x1E;
        public const uint KnightsMap = 758;
        public const uint ThordanMap = 765;
        public const uint ThordanMusic = 0x1AF3;
    }

    public class KnockbackId
    {
        public const uint Execution = 111;
        public const uint FaithUnmoving = 169;
    }
}
