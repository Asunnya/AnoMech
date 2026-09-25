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
        public const float RingArenaRadius = 20f;
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
        public const uint Coffinmaker = 19168;
        public const uint FatalFlail = 19169;
        public const uint DeadlyDoornail = 19170;
        public const uint Neckbiter = 19189;
        public const uint BigSaw = 19190;
        public const uint CharnelCellTank = 19171;
        public const uint CharnelCellHealer = 19187;
        public const uint CharnelCellDps = 19188;
        public const uint Helper = 9020;
    }

    public class BNpcNameId
    {
        public const uint VampFatale = 14300;
        public const uint Coffinmaker = 14301;
        public const uint FatalFlail = 14302;
        public const uint DeadlyDoornail = 14303;
        public const uint BigSaw = 14499;
        public const uint Neckbiter = 14500;
        public const uint CharnelCell = 14304;
        public const uint VampetteFatale = 14501;
    }

    public class ActionId
    {
        public const uint KillerVoice = 45956;
        public const uint HardcoreCast = 45914;
        public const uint HardcoreSmall = 45951;
        public const uint HardcoreBig = 45952;
        public const uint VampStompCast = 45898;
        public const uint VampStomp = 45940;
        public const uint BatRing = 45900;
        public const uint BlastBeatBat = 45941;
        public const uint BlastBeatSpread = 45942;
        public const uint BrutalRainCast = 45917;
        public const uint BrutalRainHit = 45955;

        public const uint SadisticScreechCast = 45875;
        public const uint DeadWakeCast = 46853;
        public const uint DeadWake = 45927;
        public const uint CoffinfillerCast = 46854;
        public const uint CoffinfillerLong = 45928;
        public const uint CoffinfillerMedium = 45929;
        public const uint CoffinfillerShort = 45930;
        public const uint HalfMoonLeftCast = 45902;
        public const uint HalfMoonRightCast = 45904;
        public const uint HalfMoonLeftShort = 45943;
        public const uint HalfMoonLeftLong = 45944;
        public const uint HalfMoonRightShort = 45947;
        public const uint HalfMoonRightLong = 45948;
        public const uint HalfMoonLeftCastMore = 45903;
        public const uint HalfMoonRightCastMore = 45905;
        public const uint HalfMoonLeftShortMore = 45945;
        public const uint HalfMoonLeftLongMore = 45946;
        public const uint HalfMoonRightShortMore = 45949;
        public const uint HalfMoonRightLongMore = 45950;

        public const uint CrowdKillCast = 45886;
        public const uint FinaleFataleCast = 45888;
        public const uint PulpingPulse = 45939;
        public const uint AetherlettingCast = 45967;
        public const uint AetherlettingCone = 45969;
        public const uint AetherlettingSpread = 45970;
        public const uint AetherlettingCross = 45971;
        public const uint InsatiableThirstCast = 45892;

        public const uint SadisticScreech = 45926;
        public const uint CrowdKill = 45933;
        public const uint FinaleFatale = 45936;
        public const uint InsatiableThirst = 45938;

        public const uint GravegrazerBig = 45931;
        public const uint GravegrazerSmall = 45932;
        public const uint Plummet = 45963;
        public const uint MassiveImpact = 45964;
        public const uint BarbedBurst = 45965;
        public const uint Electrocution = 46857;

        public const uint FinaleFataleCast2 = 45889;
        public const uint HellInACell = 45973;
        public const uint BloodyBondageSolo = 45974;
        public const uint UnmitigatedExplosion = 45975;
        public const uint BloodLashTank = 45977;
        public const uint BloodLashHealer = 45978;
        public const uint BloodLashDps = 45979;
        public const uint UltrasonicSpreadCast = 45980;
        public const uint UltrasonicAmpCast = 45981;
        public const uint UltrasonicSpreadSmall = 45982;
        public const uint UltrasonicSpreadTank = 47235;
        public const uint UltrasonicAmp = 45983;

        public const uint UndeadDeathmatch = 45984;
        public const uint BloodyBondageParty = 45985;
        public const uint SanguineScratchCast = 45988;
        public const uint SanguineScratchFirst = 45989;
        public const uint SanguineScratchRepeat = 45991;
        public const uint BreakdownDrop1 = 45992;
        public const uint BreakwingBeat1 = 45993;
        public const uint BreakdownDrop2 = 45994;
        public const uint BreakwingBeat2 = 45995;
        public const uint LeashExplosion = 45987;
        public const uint FinaleFataleEnrageCast = 45934;
    }

    public class StatusId
    {
        public const ushort CurseOfTheBombpyre = 4729;
        public const ushort MagicVulnerabilityUp = 2941;
        public const ushort Satisfied = 4727;
        public const ushort HellAwaits = 4730;
        public const ushort BatShape = 2056;
        public const ushort DamageDown = 2911;
    }

    public class EObjId
    {
        public const uint ElectroPuddle = 0x1EBF1F;
    }

    public class VfxPath
    {
        // BatRing has no Action omen to reuse, so a thin donut omen (inner edge at 90% of the
        // outer) stands in for the curse ring; scaled by the ring radius, its outer edge is the hit line.
        public const string CurseRing = "vfx/omen/eff/gl_sircle_2018w.avfx";

        // The electrified puddle grows from the doornail with no omen of its own; a plain circle
        // omen scaled to its radius stands in for it.
        public const string ElectroPuddle = "vfx/omen/eff/general_1bf.avfx";
    }

    public class TetherId
    {
        public const ushort CharnelCell = 353;
        public const ushort BatLeash = 353;
        public const ushort BatLeashStretched = 354;
    }

    public class LockonId
    {
        public const uint Tankbuster = 468;
        public const uint ShareMulti = 305;
        public const uint Aetherletting = 652;
    }
}
