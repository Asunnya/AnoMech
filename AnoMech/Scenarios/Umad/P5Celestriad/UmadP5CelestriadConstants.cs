namespace AnoMech.Scenarios.Umad.P5Celestriad;

// IDs and tunables for Celestriad. Sourced from Network_30207_20260811.log (pull #16,
// territory 1363, via tools/parser.py ... 1363 16 <window> -x), the deepest replay-confirmed
// Celestriad attempt available; ESTIMATE markers below flag values that pull never reached.
//
// Non-obvious structural facts:
// - FireIII/BlizzardIII/ThunderIII are single-target casts (Kefka onto a specific player), not
//   ground telegraphs. Reused here for both the initial debuff (silent, no cast) and the tower
//   resolve VFX (cast onto an invisible marker at the tower, so it animates).
// - The towers are native EventObjects, not cast omens: confirmed via the replay's spawn
//   packets at radius 10 from arena centre, 40 degrees apart, in three contiguous per-element
//   blocks (not interleaved): 20/60/100, 140/180/220, 260/300/340 degrees.
//   Which element occupies each block is randomized each run; the cited replay's fixed
//   Fire/Lightning/Ice ordering is one layout, not a permanent assignment to those sectors.
// - All 9 towers spawn once and persist for the whole mechanic; each set just toggles its 4
//   active towers between DormantState (resting look, ring included) and ActiveState (lit).
// - Only two Catastrophic Choice casts happen in total, not one per set: the first governs set
//   0's resolution, the second governs set 2's; set 1 has none and resolves independently
//   between them.
// - Aero (green) is safe away from the boss; Earth (brown) is safe toward the boss.
//
// Still unverified: which raw action id (CatastrophicChoiceAero/Earth) is actually the green
// vs. the brown telegraph.
//
// Nested types are prefixed Celestriad* (not the usual bare ActionId/StatusId) so this file's
// `using static` can coexist with UmadConstants': both would otherwise declare a same-named
// nested class, which is ambiguous under two simultaneous `using static` imports.
public static class UmadP5CelestriadConstants
{
    public static class CelestriadActionId
    {
        public const uint Celestriad = 0xBB42U;
        public const uint CatastrophicChoiceAero = 0xC24EU;  // green, safe half is toward centre
        public const uint CatastrophicChoiceEarth = 0xC24FU; // brown, safe half is away from centre
        public const uint FireIII = 0xBB43U;
        public const uint BlizzardIII = 0xBB44U;
        public const uint ThunderIII = 0xBB45U;
        public const uint StardustFireIII = 0xBB46U;
        public const uint StardustBlizzardIII = 0xBB47U;
        public const uint StardustThunderIII = 0xBB48U;
        public const uint CatastrophicChoiceEarthResolution = 0xBB4BU; // Tornado, 40-yalm donut
        public const uint CatastrophicChoiceAeroResolution = 0xBB4AU;  // Quake, 10-yalm circle
    }

    // LightningResistanceDownII and DamageDown are already in the shared UmadConstants.StatusId
    // (same values); only Fire/Ice need a home here.
    public static class CelestriadStatusId
    {
        public const ushort FireResistanceDownII = 0xB56;
        public const ushort IceResistanceDownII = 0xB57;
    }

    // Real EObj rows for the 9 tower props, element mapping confirmed in-game.
    public static class CelestriadTowerEObjId
    {
        public const uint Fire = 0x1EC03EU;
        public const uint Lightning = 0x1EC040U;
        public const uint Ice = 0x1EC03FU;

        // SG states for this EObj family, confirmed for all 3 rows.
        public const ushort ActiveState = 16;
        public const ushort DormantState = 2;
    }

    public static class CelestriadGeometry
    {
        public const float RingRadius = 10f; // confirmed
        public const float SoakRadius = 3f;  // ESTIMATE, adjacent same-element towers are 6.84y apart
    }

    public static class CelestriadTiming
    {
        public const float CelestriadCastAt = 1f;
        public const float CelestriadCastTime = 4.7f;         // confirmed
        public const float DebuffApplyAt = 6.1f;
        public const float DebuffDuration = 20f;               // confirmed
        public const float CatastrophicChoiceCastTime = 4.0f;  // confirmed
        public const float DamageDownDuration = 30f;           // ESTIMATE
        public const float TowerDespawnBuffer = 3f;             // ESTIMATE, gap after DeactivateAt[2] before the towers vanish

        // Per-set absolute timestamps (scenario-start-relative). TowerStart[0] (Celestriad
        // cast-end + 0.4s) is when all 9 towers first spawn (dormant) and also when set 0's 4
        // activate; TowerStart[1]/[2] activate that set's 4 on the already-spawned towers.
        // CcAt[0]/[2] (confirmed) are each set's single Catastrophic Choice; ResolveAt[0]/[2]
        // equal CcAt + CatastrophicChoiceCastTime, so the CC1-end to CC2-end window is a
        // confirmed fixed 12.16s that has to contain all of set 1. Set 1's own timing splits
        // that budget close to evenly (an estimate, not replay-confirmed).
        public static readonly float[] TowerStart = { 6.1f, 14.4f, 20.6f };
        public static readonly float?[] CcAt = { 10.18f, null, 22.34f };
        public static readonly float[] ResolveAt = { 14.18f, 20.5f, 26.34f };
        public static readonly float[] DeactivateAt = { 14.3f, 20.6f, 26.44f };
    }
}
