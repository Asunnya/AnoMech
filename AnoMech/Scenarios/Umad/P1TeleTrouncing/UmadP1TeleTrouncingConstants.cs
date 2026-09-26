namespace AnoMech.Scenarios.Umad.P1TeleTrouncing;

// Each direction has a primary status id (0x130C-0x130F, both slots of a "matching" pair) and a
// secondary (0x13D7-0x13DA, one half of a "different" pair); the in-game name is "Tele-portent"
// for all 8, direction is icon-only.
public static class UmadP1TeleTrouncingConstants
{
    public static class ActionId
    {
        public const uint TeleTrouncing = 0xBAB9U;
        public const uint GravenImage = 0xBCF2U;
        public const uint MysteryMagic = 0xBA94U;

        // The 0-damage "arrow placed" tell, on all 8 members ~0.07s after each wave's Tele-portent
        // expiry. The real caster is a per-target helper; routed through Kefka's Cast since it
        // has no VFX.
        public const uint TeleTrouncingArrowSpawn = 0xBABAU;

        // Confetti-3's stack hit: ~21.04s after Tele-trouncing's cast start, on the 3 players
        // within 6y of their nearer holder (never the holder). The arming cast isn't modeled:
        // the stack is held from an earlier, unmodeled wave.
        public const uint DoubleTroubleTrapStack = 0xBAA7U;

        // What Graven Image's tethers resolve into, 4 and 4, at the instant Confused/Sleep land.
        public const uint IndulgentWill = 0xBAB5U;
        public const uint IdyllicWill = 0xBAB6U;

        // BossMod's "Unk1BossP1" (Kefka self-cast, 3.0s, nothing to react to); modeled so Kefka
        // isn't idle while the real boss is casting.
        public const uint Unk1BossP1 = 0xC554U;

        // Two more unnamed instant Kefka actions in every window: 0xC555 at +31.19s and 0xC3FD
        // at +33.28s. Unk1's resolve sets model state 4 and 0xC555's sets 0 (see KefkaModelState).
        public const uint Unk2BossP1 = 0xC555U;
        public const uint TeleportP1 = 0xC3FDU;

        // Mystery Magic's line AoEs (helper->self, 5.0s, rect 40x10; the telegraph comes from
        // Lumina). Truth: 2x Real1 at the real slots. Lie: Real2 at the real slots plus Fake (a
        // dmg=1 marker hit on everyone) at the other two.
        public const uint ThrummingThunderReal1 = 0xBA9FU;
        public const uint ThrummingThunderFake = 0xBAA0U;
        public const uint ThrummingThunderReal2 = 0xBAA1U;

        // Flagrant Fire III's resolve: Spread = 5y circle on every player, Stack = 6y circle
        // needing 4. Both are common.
        public const uint FlagrantFireSpread = 0xBAA2U;
        public const uint FlagrantFireStack = 0xBAA3U;

        // The statue gaze: IndolentWill = normal (look away) from the NE statue, AveMaria =
        // inverted (look toward) from the NW one; a per-run coin flip.
        public const uint IndolentWill = 0xBAB4U;
        public const uint AveMaria = 0xBAB3U;
    }

    // Kefka's model state between Unk1BossP1's and Unk2BossP1's resolves.
    public static class KefkaModelState
    {
        public const byte Normal = 0;
        public const byte Unk1 = 4;
    }

    // Every 5.0s sheet cast shows a 4.7s bar, every 3.0s one a 2.7s bar, and the effect lands
    // 0.29s after the bar fills.
    public static class CastBar
    {
        public const float Long = 4.7f;
        public const float Short = 2.7f;
        public const float FireDelay = 0.29f;
    }

    // ActionEffect header animation locks from the replay.
    public static class AnimationLock
    {
        public const float TeleTrouncing = 3.1f;
        public const float GravenImage = 2.1f;
        public const float Unk1 = 2.1f;
        public const float Unk2 = 2.1f;
        public const float Teleport = 1.1f;
        public const float MysteryMagic = 3.1f;
        public const float LightOfJudgment = 3.1f;
        public const float Helper = 1.1f;
        public const float AutoAttack = 0.1f;
    }

    // The 0.96s Magic Vulnerability Up every real hit of Double-trouble Trap, Idyllic Will and
    // Flagrant Fire III carries.
    public const float MagicVulnerabilityUpSeconds = 0.96f;

    // The real track starts at the pull, 19.32s before the cast.
    public const float BgmSecondsAtStart = 17.73f;

    // Headmarkers during Mystery Magic (BossMod's IconID values): the lie/truth orbs ride on
    // Kefka, the stack/spread marker on the targeted player(s).
    public static class LockonId
    {
        public const uint FireSpread = 127;
        public const uint FireStack = 128;
        public const uint FireLie = 673;
        public const uint FireTruth = 674;
        public const uint LightningLie = 677;
        public const uint LightningTruth = 678;

        // Stand-ins for the gaze "?" tell (the real one is an EObjAnimation on the statue prop):
        // the universal gaze eye on both types, a second eye only on an inverted gaze.
        public const uint GazeTell = 23;
        public const uint GazeInvertedTell = 223;
    }

    public static class StatusId
    {
        public const ushort TelePortentUpPrimary = 0x130C;
        public const ushort TelePortentDownPrimary = 0x130D;
        public const ushort TelePortentRightPrimary = 0x130E;
        public const ushort TelePortentLeftPrimary = 0x130F;
        public const ushort TelePortentUpSecondary = 0x13D7;
        public const ushort TelePortentDownSecondary = 0x13D8;
        public const ushort TelePortentRightSecondary = 0x13D9;
        public const ushort TelePortentLeftSecondary = 0x13DA;

        // The Confetti carry debuff (BossMod's SID.DoubleTroubleTrap); BossMod keys the holder
        // identity and the knockback source off whoever has it.
        public const ushort DoubleTroubleTrap = 0x13D6;

        // Landed by Indulgent Will / Idyllic Will.
        public const ushort Confused = 0x503;
        public const ushort Sleep = 0x131E;

        // Environmental, exactly 1.00s, on whoever is mid-slide from their own teleporter; pins
        // the arrow push time.
        public const ushort Bind = 0x9D6;
    }

    public static class KnockbackId
    {
        // BossMod's P1DoubleTroubleTrapKB hardcodes 14; the real BAA7 effect decodes to
        // Knockback row 240 (Distance 14, Speed 20).
        public const uint DoubleTroubleTrapStack = 240;
    }

    public static class TetherId
    {
        // "GravenImage" in BossMod's UMAD module, used for every statue-related P1 mechanic.
        public const ushort GravenImage = 0x2D;
    }

    public static class BNpcBaseId
    {
        public const uint Kefka = 19504;
        public const uint GravenImage = 19505;
    }

    public static class BNpcNameId
    {
        public const uint Kefka = 7131;
        public const uint GravenImage = 7132;
    }

    public static class Spawn
    {
        public const int TelePortentArg2Serial = 0x50;
        // Clear of the statue props' 0x4000EB80-8A.
        public const uint TelePortentEntityIdBase = 0x4000EB90u;
    }

    public static class EObjState
    {
        public const uint TelePortentUsed = 7;
    }

    public static class EObjId
    {
        // An EObj (BossMod's OID.TelePortent), not a BNpcBase.
        public const uint TelePortent = 0x1EC023U;

        // The gaze-source props (EObj rows 2015166/2015167, for_bg SGBs). Every pull spawns two
        // of each; the gaze uses one: 0x1EBFBE plays 0x0040/0x0080 for IndolentWill, 0x1EBFBF
        // for AveMaria (the "?" is part of its model animation). The BNpc doppels still tether
        // and carry a marker stand-in in case a prop fails to render.
        public const uint GazeStatueNormal = 0x1EBFBEU;   // NE, IndolentWill
        public const uint GazeStatueInverted = 0x1EBFBFU; // NW, AveMaria

        // Zone layout instances the real spawn packets bind these props to (offset 0x0C). The
        // SGBs are pre-placed in the zone; a LayoutId-less spawn loads a detached copy nothing
        // ever activates. First instance = the tether pair, second = the gaze pair.
        public const uint GazeStatueNormalLayoutId = 0xBC1793U;        // world (107, 8.5, 43)
        public const uint GazeStatueNormalAnimLayoutId = 0xBC1794U;    // world (105.25, 13.5, 34)
        public const uint GazeStatueInvertedLayoutId = 0xBC1795U;      // world (95, 27, 25)
        public const uint GazeStatueInvertedAnimLayoutId = 0xBC1796U;  // world (95, 12.5, 25)

        // The despawned/hidden SharedTimelineState every real prop spawn carries; "appear"
        // (0x0001/0x0002) brings the statue up.
        public const ushort GazeStatueSpawnState = 0x0004;

        // The InstanceContentDirector (0x8003) for content 30162, the EventId every real prop
        // spawn carries; these props' EObj rows carry Invisibility=7.
        public const uint PropEventId = 0x800375D2U;

        // The colossus itself: sgbg_z3oa_a0_gmc01.sgb nests the four sta01-04 meshes (~47y north,
        // 17y below the platform). Spawned at every pull start at (100, 0, 100) in state 4, then:
        // appear 11s into the pull, 0x0010/0x0020 ~60s in, 0x0040/0x0080 19.3s before the
        // Tele-trouncing cast, collapse + final 54.60s after it.
        public const uint GravenStatue = 0x1EBFB4U;
        public const uint GravenStatueLayoutId = 0xBC7C91U;
        public const uint GravenStatueArg2 = 0x00400003U;
    }

    // Hit VFX; none of these has an Omen sheet entry. "c"/"t" = caster/target side, played
    // together.
    public static class VfxPath
    {
        // The props' own SGB VFX children: "Maria" (AveMaria) and "Nemuri" (sleep). Only used by
        // the direct-spawn diagnostic (PropsStaticVfxTest).
        public const string StatueMariaAppear = "bg/ex2/05_zon_z3/common/vfx/eff/b1328mari2_u.avfx";
        public const string StatueMariaWindUp = "bg/ex2/05_zon_z3/common/vfx/eff/b1328mari1_u.avfx";
        public const string StatueNemuriAppear = "bg/ex2/05_zon_z3/common/vfx/eff/b1329nemu2_u.avfx";
        public const string StatueNemuriWindUp = "bg/ex2/05_zon_z3/common/vfx/eff/b1329nemu1_u.avfx";
        // The only two of the six with an emitter of their own.
        public const string StatueMariaResolve = "bg/ex2/05_zon_z3/common/vfx/eff/b1328mari3_u.avfx";
        public const string StatueNemuriResolve = "bg/ex2/05_zon_z3/common/vfx/eff/b1329nemu3_u.avfx";

        // The teleporter's own materializing burst, at the spot the walkable EObj later spawns.
        public const string TeleTrouncingArrowSpawnHit = "vfx/monster/gimmick6/eff/z3oy_b0_g05c0c.avfx";

        // Plays at each stack holder's position, the knockback's source.
        public const string DoubleTroubleTrapStackHit = "vfx/monster/gimmick6/eff/z3oy_b0_g02c0c.avfx";

        // Two caster-side vfx plus one target-side, all simultaneous.
        public const string IndulgentWillCasterShoot = "vfx/monster/gimmick2/eff/shoot_mgc00f.avfx";
        public const string IndulgentWillCasterBurst = "vfx/monster/gimmick2/eff/f1d8_b2_g02c0f.avfx";
        public const string IndulgentWillTarget = "vfx/monster/gimmick2/eff/f1d8_b2_g02t0f.avfx";

        // One caster-side, one target-side.
        public const string IdyllicWillCaster = "vfx/monster/gimmick6/eff/z3oy_b0_g04c0c.avfx";
        public const string IdyllicWillTarget = "vfx/monster/gimmick6/eff/z3oy_b0_g04t0c.avfx";

        // Gaze resolve vfx.
        public const string AveMariaHit = "vfx/monster/gimmick6/eff/z3oy_b0_g29c0c.avfx";
        public const string IndolentWillHit = "vfx/monster/gimmick6/eff/z3oy_b0_g30c0c.avfx";

        // The ~10s "eyes light up" wind-up, ~9.8s before the gaze. The real packet is
        // byte-identical for normal and inverted; the distinguishers are which prop glows
        // (NE=away, NW=toward) and the "?" in 0x1EBFBF's model animation. A generic
        // statue-charge vfx used for both.
        public const string GazeWindUp = "vfx/monster/gimmick6/eff/z3oy_b0_g27c0c.avfx";
    }

    // Sound is a known gap: the TMBs carry real .scd cues, but AnoMech has no sound-playback call
    // yet and SoundManager.PlaySound's ~16 parameters are mostly undocumented.
}
