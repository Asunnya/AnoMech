using System.Collections.Generic;

namespace AnoMech.Scenarios;

public enum TankJob { Any, Paladin, Warrior, DarkKnight, Gunbreaker }

// Who a non-SourceSide mitigation applies to when intercepted. Self (default): the caster
// only. Party: every current party role (Dark Missionary, Shake It Off). Ally: whichever
// role the caster's real target resolves to (Intervention, Nascent Flash). SourceSide (its
// own separate bool) is orthogonal -- for an enemy-debuffing mitigation like Reprisal.
public enum MitigationScope { Self, Party, Ally }

// One tank mitigation ability. Null Percent/Duration/Cooldown = not pinned against a numeric
// source; StatusId/ActionId 0 = not confirmed against a real press (0 is never intercepted).
// Only a press made outside a scenario confirms an id: inside one, interception echoes back
// the id it was told to apply. Radius only matters for SourceSide.
//
// Shield opts a no-Percent entry into interception, or the real ability would fire and burn
// its cooldown. A shield's size is one of ShieldPercentOfMaxHp or ShieldPotency, both a
// percentage of the CASTER's max HP (TankShieldEstimate converts potency roughly); the caller
// re-expresses it against each recipient's max HP (LocalPlayerInputHooks.GrantShield).
public readonly record struct TankMitigationAbility(
    string Name, TankJob Job, ushort StatusId, uint ActionId, float? Percent, float? Duration, float? Cooldown,
    int Charges = 1, bool SourceSide = false, float? Radius = null, MitigationScope Scope = MitigationScope.Self,
    bool Shield = false, float? ShieldPercentOfMaxHp = null, float? ShieldPotency = null, string? Notes = null);

public static class TankShieldEstimate
{
    // From one screenshot (Sentinel's 1000 potency covered ~15% of a Paladin's bar), scaled
    // linearly; not the server's formula.
    private const float EstimatedPercentOfCasterMaxHpPer1000Potency = 0.15f;

    public static float PercentOfCasterMaxHp(float potency)
        => potency / 1000f * EstimatedPercentOfCasterMaxHpPer1000Potency;
}

// Real abilities only. Party mitigation nobody in the sim casts is not an entry here: it was
// once a synthetic status id, which the status writer drops for having no Status sheet row, so
// it never applied. See TankMitigation.BotPartyMitigationFraction.
public static class TankMitigationChart
{
    public static readonly IReadOnlyList<TankMitigationAbility> All =
    [
        // ---- Shared role actions ----
        new("Rampart", TankJob.Any, 1191, 7531, 0.20f, 20f, 90f,
            Notes: "ActionId 7531 and StatusId 1191 confirmed via real, un-intercepted presses on all four " +
                   "tank jobs (Paladin, Warrior, Dark Knight, Gunbreaker) -- same ids every time, 20s duration."),
        new("Reprisal", TankJob.Any, 1193, 7535, 0.10f, 15f, 60f, SourceSide: true, Radius: 5f,
            Notes: "ActionId 7535 confirmed on all four jobs. StatusId CORRECTED from an earlier guess of " +
                   "2101 to 1193 -- confirmed via DebugMenu's target-status scanner (added specifically for " +
                   "this, since Reprisal debuffs whatever's targeted, not the caster) after a real press on " +
                   "a real boss in real content. 2101 had been listed as one of several legacy/same-named " +
                   "candidates; 1193 turned out to be the live one, not 2101. Hits every enemy within 5y of " +
                   "the caster (a radius AoE, not a single targeted enemy) -- see " +
                   "LocalPlayerInputHooks.ApplySourceSideMitigation, which already applies it that way. " +
                   "TankMitigation.SurvivalFraction's mitigationSource just needs to be the enemy actually " +
                   "dealing the tankbuster; since Reprisal already lands on every enemy in range including " +
                   "that one (as long as it was within 5y when cast), there's no real \"which enemy\" " +
                   "ambiguity to resolve -- an earlier note overstated this as an open gap. Legacy id 753 " +
                   "also mapped. 15s duration reconfirmed directly by the user against real numbers -- one " +
                   "of four thebalanceffxiv.com job pages summarized it as 10s, an outlier against both the " +
                   "other three and this real confirmation, not acted on."),

        // ---- Job invulnerabilities ----
        new("Hallowed Ground", TankJob.Paladin, 82, 30, 1f, 10f, 420f,
            Notes: "ActionId 30 confirmed live (Paladin). StatusId corrected from an earlier wrong guess " +
                   "of 1302 -- that id happened to ALSO resolve to the display name \"Hallowed Ground\" " +
                   "(a second, unrelated Status-sheet row sharing the name), so an interception test's " +
                   "matching log line was a false positive, not real confirmation. A real, un-intercepted " +
                   "press showed the true id is 82, active for 10s. True invulnerability."),
        new("Superbolide", TankJob.Gunbreaker, 1836, 16152, 1f, 10f, 360f,
            Notes: "ActionId 16152 and StatusId 1836 both confirmed via a real, un-intercepted press " +
                   "(Gunbreaker), 10s duration. Also drops current HP to 50% of max (no-op if already below that)."),
        new("Living Dead", TankJob.DarkKnight, 810, 3638, 1f, 10f, 300f,
            Notes: "ActionId 3638 and StatusId 810 both confirmed via a real, un-intercepted press (Dark " +
                   "Knight), 10s duration. Approximated as a flat invuln here. Real mechanic: HP can't hit " +
                   "0 while active (Walking Dead, status 811, also 10s); must be healed for 100% of max HP " +
                   "within that window or still dies."),
        new("Holmgang", TankJob.Warrior, 409, 43, 1f, 10f, 240f,
            Notes: "ActionId 43 and StatusId 409 both confirmed via a real, un-intercepted press (Warrior), " +
                   "10s duration. Same numeric id as SimParty.InvulnStatusId (the engine's own fallback " +
                   "\"give invuln\" marker) -- NOT harmless: GiveInvuln used to apply this id to every job, " +
                   "so a Paladin/Dark Knight/Gunbreaker's scripted invuln visibly showed Warrior's own " +
                   "Holmgang icon. Fixed via TankMitigationChart.InvulnStatusIdByJob -- see GiveInvuln's own " +
                   "doc comment. Real effect is an HP floor at 1, not a damage reduction -- treated as a flat " +
                   "survive here since tankbusters resolve as binary lethal/not."),

        // ---- Job 40%, 120s-cooldown mitigations ----
        new("Vengeance", TankJob.Warrior, 3832, 44, 0.40f, 15f, 120f,
            Notes: "ActionId 44 and StatusId 3832 both confirmed via a real, un-intercepted press (Warrior), " +
                   "15s duration. The status itself is actually named \"Damnation\" in the current Status " +
                   "sheet (the action's own name is still \"Vengeance\") -- same naming quirk as Sentinel/Guardian."),
        new("Sentinel", TankJob.Paladin, 3829, 17, 0.40f, 15f, 120f, ShieldPotency: 1000f,
            Notes: "ActionId 17 and StatusId 3829 both confirmed live (Paladin), 15s duration matches this " +
                   "entry exactly. The status itself is actually named \"Guardian\" in the current Status " +
                   "sheet (the action's own name is still \"Sentinel\") -- also grants a paired status " +
                   "\"Guardian's Will\" (3830, same 15s duration, always gained/lost together with 3829) " +
                   "not modeled separately here since Percent above is keyed on 3829 alone. Guardian also " +
                   "grants a 1000-potency shield alongside the 40% reduction -- see ShieldPotency and " +
                   "TankShieldEstimate for how that gets turned into an actual banked amount."),
        new("Shadow Wall", TankJob.DarkKnight, 3835, 36927, 0.40f, 15f, 120f,
            Notes: "ActionId CHANGED from an earlier guess of 3636 to 36927 -- pressing 3636 (\"Shadow " +
                   "Wall\") produced no status at all, but a \"Shadowed Vigil\" (36927) pressed moments " +
                   "later did (StatusId 3835, 15s, matching this entry), suggesting 3636 is a stale/non-" +
                   "current id and 36927 is what the ability actually resolves to now -- same rework " +
                   "pattern as Sentinel/Guardian and Vengeance/Damnation. Also grants a paired status " +
                   "\"Vigilant\" (3902, 20s) not modeled separately."),
        new("Nebula", TankJob.Gunbreaker, 3838, 16148, 0.40f, 15f, 120f,
            Notes: "ActionId 16148 and StatusId 3838 both confirmed via a real, un-intercepted press " +
                   "(Gunbreaker), 15s duration. The status itself is actually named \"Great Nebula\" -- same " +
                   "naming quirk as Sentinel/Guardian and Vengeance/Damnation."),

        // ---- Shorter personal mitigations ----
        new("Bulwark", TankJob.Paladin, 77, 22, 0.20f, 10f, 90f,
            Notes: "ActionId 22 and StatusId 77 both confirmed live (Paladin), 10s duration matches this " +
                   "entry exactly. Grants 100% block rate; block's own reduction scales with shield item " +
                   "level, so 20% is still an approximation, not a fixed number."),
        new("Holy Sheltron", TankJob.Paladin, 2674, 3542, 0.30f, 8f, null,
            Notes: "ActionId 3542 and StatusId 2674 confirmed live (Paladin), ~8s duration observed. Real " +
                   "kit is actually two separate 15% windows -- 15% for the full 8s, plus another 15% " +
                   "(Knight's Resolve, 2675) for just the first 4s -- collapsed into one flat 30% entry for " +
                   "the full duration rather than modeled as two statuses, per an explicit simplification " +
                   "call; slightly generous for the back half, close enough for the front half. Also grants " +
                   "Knight's Benediction (2676, ~12s, a heal-over-time, not mitigation). Oath-gauge gated " +
                   "(costs 50 Oath); cooldown still unverified."),
        new("Oblation", TankJob.DarkKnight, 2682, 25754, 0.10f, 10f, 60f, Charges: 2, Scope: MitigationScope.Ally,
            Notes: "ActionId 25754 and StatusId 2682 both confirmed via a real, un-intercepted press (Dark " +
                   "Knight), 10s duration matches this entry exactly. Castable on self or any party member -- " +
                   "Scope.Ally applies it to whichever role is actually targeted (self-targeting yourself " +
                   "works the same as any other Ally-scope press). Charges: 2 means each use is tracked as " +
                   "its own independent 60s recovery, so two presses close together are both allowed rather " +
                   "than the second being blocked for a full 60s -- see TankMitigationTracker."),
        new("Camouflage", TankJob.Gunbreaker, 1832, 16140, 0.10f, 20f, 90f,
            Notes: "ActionId 16140 and StatusId 1832 both confirmed via a real, un-intercepted press " +
                   "(Gunbreaker), 20s duration matches this entry exactly. Also +50% parry rate."),
        new("Dark Mind", TankJob.DarkKnight, 746, 3634, 0.10f, 10f, 60f,
            Notes: "ActionId 3634 and StatusId 746 both confirmed via a real, un-intercepted press (Dark " +
                   "Knight), 10s duration matches this entry exactly. Percent CORRECTED -- the earlier note " +
                   "claiming this was magic-only was wrong; it's 20% magic AND 10% physical, both apply. " +
                   "Percent above uses the physical value (0.10f) since that's what a physical tankbuster " +
                   "sees -- there's no magic-tankbuster concept in this engine yet to use the higher 20%."),
        new("Bloodwhetting", TankJob.Warrior, 2678, 3551, 0.10f, 8f, 25f,
            Notes: "ActionId 3551 and StatusId 2678 both confirmed via a real, un-intercepted press " +
                   "(Warrior), 8s duration -- the action is named \"Raw Intuition\" (its pre-upgrade name) " +
                   "but the status it grants is \"Bloodwhetting\", matching this entry's name. Also grants " +
                   "two paired statuses -- Stem the Flow (2679, ~4s) and Stem the Tide (2680, ~20s) -- not " +
                   "modeled separately. Self mitigation + heal-over-time."),
        new("Heart of Corundum", TankJob.Gunbreaker, 2683, 16161, 0.15f, 8f, null, Scope: MitigationScope.Ally,
            Notes: "ActionId 16161 and StatusId 2683 both confirmed via a real, un-intercepted press " +
                   "(Gunbreaker), 8s duration -- the action is named \"Heart of Stone\" (its pre-upgrade " +
                   "name) but the status it grants is \"Heart of Corundum\", matching this entry's name. " +
                   "Also grants two paired statuses -- Clarity of Corundum (2684, 15% for ~4s, not modeled " +
                   "separately here) and Catharsis of Corundum (2685, ~20s, a heal not mitigation). " +
                   "Castable on self or a party member -- Scope.Ally applies it to whichever role is " +
                   "actually targeted. Cooldown still unverified."),

        // ---- Party-wide mitigations (Scope.Party) ----
        new("Dark Missionary", TankJob.DarkKnight, 1894, 16471, 0.05f, 15f, 90f, Scope: MitigationScope.Party,
            Notes: "ActionId 16471 and StatusId 1894 both confirmed via a real, un-intercepted press (Dark " +
                   "Knight), 15s duration. 10% magic / 5% physical -- Percent above uses the physical value. " +
                   "Cooldown CORRECTED from an earlier \"unverified\" null to 90f, per thebalanceffxiv.com."),
        new("Heart of Light", TankJob.Gunbreaker, 1839, 16160, 0.05f, 15f, 90f, Scope: MitigationScope.Party,
            Notes: "ActionId 16160 and StatusId 1839 both confirmed via a real, un-intercepted press " +
                   "(Gunbreaker), 15s duration. 10% magic / 5% physical -- Percent above uses the physical value."),
        new("Shake It Off", TankJob.Warrior, 1457, 7388, null, 30f, 90f, Scope: MitigationScope.Party, Shield: true,
            ShieldPercentOfMaxHp: 0.15f,
            Notes: "ActionId 7388 and StatusId 1457 both confirmed via a real, un-intercepted press " +
                   "(Warrior), 30s duration, 90s cooldown per thebalanceffxiv.com. CORRECTED -- this was " +
                   "wrongly modeled as a flat 0.15f Percent (a standing damage reduction for the whole " +
                   "duration); it's actually an absorption shield worth 15% of the CASTING Warrior's own " +
                   "max HP (not each recipient's), same category as Divine Veil, not a repeated-hit " +
                   "reduction. Also grants a paired status \"Shake It Off (Over Time)\" (2108, ~15s, a " +
                   "heal-over-time, not mitigation)."),

        // ---- Not wired into mitigation math -- listed for completeness ----
        new("Thrill of Battle", TankJob.Warrior, 87, 40, null, 10f, 90f, Shield: true,
            Notes: "ActionId 40 and StatusId 87 both confirmed via a real, un-intercepted press (Warrior) " +
                   "-- duration corrected from an earlier guess of 20f to the real 10s. HP buffer (+20% " +
                   "max/current HP), not a damage-reduction percentage."),
        new("The Blackest Night", TankJob.DarkKnight, 1178, 7393, null, null, 15f, Shield: true,
            ShieldPercentOfMaxHp: 0.25f,
            Notes: "ActionId 7393 and StatusId 1178 confirmed via a real, un-intercepted press (Dark " +
                   "Knight). MP-gated shield absorbing 25% of the caster's max HP once, not a flat % " +
                   "reduction while active -- deliberately no Percent set, forcing one in would misrepresent " +
                   "the mechanic. Self-scope, so caster and recipient are the same person -- no caster/" +
                   "recipient conversion needed. Cooldown 15s per thebalanceffxiv.com."),
        new("Nascent Flash", TankJob.Warrior, 1858, 16464, 0.10f, 8f, 25f, Scope: MitigationScope.Ally,
            Notes: "ActionId 16464 and StatusId 1858 (the target-facing \"Nascent Glint\" status) confirmed " +
                   "via a real, un-intercepted press and target-status scan (Warrior). Percent/Duration/" +
                   "Cooldown per thebalanceffxiv.com (targeted mitigation + heal, WAR's equivalent of " +
                   "Oblation). The same press also grants the caster two self-only statuses -- 2679 Stem " +
                   "the Flow (a timer) expiring into 2680 Stem the Tide (a self shield/heal, same shape as " +
                   "Intervention's Knight's Resolve -> Knight's Benediction) -- neither is the target's " +
                   "mitigation and neither is tracked here. Scope.Ally applies 1858 to whichever role is " +
                   "actually targeted."),
        new("Divine Veil", TankJob.Paladin, 1362, 3540, null, 30f, 90f, Scope: MitigationScope.Party, Shield: true,
            ShieldPercentOfMaxHp: 0.10f,
            Notes: "ActionId 3540 and StatusId 1362 confirmed live (Paladin). Duration CORRECTED from an " +
                   "earlier approximate 22f to 30f, per thebalanceffxiv.com and matching this chart's own " +
                   "second observation (the first, shorter reading was likely a mid-duration recheck, not " +
                   "the true length). Shields each affected ally for 10% of the CASTING Paladin's own max " +
                   "HP once (not each recipient's own), not a flat % reduction while active -- deliberately " +
                   "no Percent set, forcing one in would misrepresent the mechanic."),
        // TODO: Passage of Arms is channeled, party-wide and positional; none of it fits this shape.
        new("Passage of Arms", TankJob.Paladin, 1175, 7385, null, null, 120f,
            Notes: "TODO -- too complicated for now (channeled, party-wide, positional). See the comment " +
                   "above this entry."),
        new("Intervention", TankJob.Paladin, 1174, 7382, 0.10f, 8f, 10f, Scope: MitigationScope.Ally,
            Notes: "ActionId 7382 and StatusId 1174 (the target-facing \"Intervention\" status) confirmed " +
                   "via a real, un-intercepted press and target-status scan (Paladin). Percent/Duration/" +
                   "Cooldown per thebalanceffxiv.com (10%, or 20% if Rampart or Sentinel is also active on " +
                   "the target). The same press also grants the caster two self-only statuses -- 2675 " +
                   "Knight's Resolve (a timer) expiring into 2676 Knight's Benediction (a self HoT) -- " +
                   "neither is the target's mitigation and neither is tracked here. Scope.Ally applies 1174 " +
                   "to whichever role is actually targeted (usually the co-tank)."),
        // TODO: Cover is a damage redirect, not a reduction; needs a different mechanism.
        new("Cover", TankJob.Paladin, 0, 0, null, 12f, 120f,
            Notes: "TODO -- out of scope (pure damage redirect, not a %). See the comment above this entry."),

        // ---- Tank limit break 3 ----
        // Party-wide 80% for 8s. The "cooldown" stands in for the gauge: one per run. The gauge
        // itself is faked full while a sim runs (LocalPlayerInputHooks.UpdateLimitBreakIllusion).
        new("Last Bastion", TankJob.Paladin, 196, 199, 0.80f, 8f, 600f, Scope: MitigationScope.Party,
            Notes: "Tank LB3. Action 199 / status 196 per the Action and Status sheets (2026-09-14)."),
        new("Land Waker", TankJob.Warrior, 863, 4240, 0.80f, 8f, 600f, Scope: MitigationScope.Party,
            Notes: "Tank LB3. Action 4240 / status 863 confirmed in real logs (Network_30208_20260902.log: " +
                   "the '1090 Land Waker' AoE on 8 targets, then status 35F=863 on each with dur=8.00)."),
        new("Dark Force", TankJob.DarkKnight, 864, 4241, 0.80f, 8f, 600f, Scope: MitigationScope.Party,
            Notes: "Tank LB3. Action 4241 / status 864 per the Action and Status sheets (2026-09-14)."),
        new("Gunmetal Soul", TankJob.Gunbreaker, 1931, 17105, 0.80f, 8f, 600f, Scope: MitigationScope.Party,
            Notes: "Tank LB3. Action 17105 / status 1931 per the Action and Status sheets (2026-09-14)."),

    ];

    // Native ClassJob row id -> that job's real invuln status id (SimParty.GiveInvuln).
    public static readonly IReadOnlyDictionary<uint, ushort> InvulnStatusIdByJob = new Dictionary<uint, ushort>
    {
        [19] = 82,   // Paladin: Hallowed Ground
        [21] = 409,  // Warrior: Holmgang
        [32] = 810,  // Dark Knight: Living Dead
        [37] = 1836, // Gunbreaker: Superbolide
    };
}
