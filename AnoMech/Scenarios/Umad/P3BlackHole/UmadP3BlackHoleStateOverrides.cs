using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.Umad.P3BlackHole;

// One Thunder III set's two hits, planned for whichever tank slot ends up bot-driven. No
// "auto": every consumer switches on all four cases.
public enum ThunderIIIAssignment
{
    MtInvulnsBoth,  // MT stands closest for both hits in the set and pops an invuln before the first
    OtInvulnsBoth,  // mirror of MtInvulnsBoth
    ShareMtFirst,   // hits split -- MT takes the first (needs its own + party mitigation, no invuln), OT takes the second
    ShareOtFirst,   // mirror of ShareMtFirst
}

// Shared so the Ai and the scenario agree on the role assignment for a plan.
public static class ThunderIIIPlanning
{
    // Who stands closest to Exdeath for the first hit (targeting is party.Find.Closest) and,
    // for a Share plan, who swaps in for the second.
    public static (PartyRole First, PartyRole? Second) Roles(ThunderIIIAssignment plan) => plan switch
    {
        ThunderIIIAssignment.MtInvulnsBoth => (PartyRole.MainTank, null),
        ThunderIIIAssignment.OtInvulnsBoth => (PartyRole.OffTank, null),
        ThunderIIIAssignment.ShareMtFirst  => (PartyRole.MainTank, PartyRole.OffTank),
        ThunderIIIAssignment.ShareOtFirst  => (PartyRole.OffTank, PartyRole.MainTank),
        _                                   => throw new System.ArgumentOutOfRangeException(nameof(plan), plan, null),
    };

    // Only the InvulnsBoth plans need a scripted invuln; a Share relies on mitigation.
    public static PartyRole? InvulnRole(ThunderIIIAssignment plan) => plan switch
    {
        ThunderIIIAssignment.MtInvulnsBoth => PartyRole.MainTank,
        ThunderIIIAssignment.OtInvulnsBoth => PartyRole.OffTank,
        _                                    => null,
    };
}

public sealed class UmadP3BlackHoleStateOverrides
{
    // --- Fight-wide: one roll the whole sim shares -------------------------------------
    public uint? FirstSlap { get; set; }            // null = random; else ActionId.SlapHappy_Left / .SlapHappy_Right (debug-only UI)

    // Defaults: MT solo-tanks Set 1 behind an invuln, Set 2 is shared MT-first.
    public ThunderIIIAssignment ThunderSet1 { get; set; } = ThunderIIIAssignment.MtInvulnsBoth;
    public ThunderIIIAssignment ThunderSet2 { get; set; } = ThunderIIIAssignment.ShareMtFirst;

    // --- Per player: everyone has their own ---------------------------------------------
    // 1/2/3 = First/Second/Third in line. Line and Accretion are solved together against the
    // fight's own slot layout, so a request the layout can't satisfy is dropped and logged.
    public PerRoleSetting<int> LineNumber { get; set; } = new();
    // Tanks and third-in-line never carry Accretion in the real fight.
    public PerRoleSetting<bool> Accretion { get; set; } = new();
    // Debug-only UI: aim every first-slap cone at this seat.
    public PerRoleSetting<bool> FirstSlapAllOnMe { get; set; } = new();

    // What the run will try to seat: one line and/or Accretion per seat that asked.
    public Dictionary<PartyRole, (int? Line, bool? Accretion)> Requests(PartyRole localPlayerRole)
    {
        var requests = new Dictionary<PartyRole, (int? Line, bool? Accretion)>();
        foreach (var (role, line) in LineNumber.Resolve(localPlayerRole))
            if (line is >= 1 and <= 3)
                requests[role] = (line, requests.GetValueOrDefault(role).Accretion);
        foreach (var (role, accretion) in Accretion.Resolve(localPlayerRole))
            requests[role] = (requests.GetValueOrDefault(role).Line, accretion);
        return requests;
    }

    // Exactly one healer and exactly one DPS carry Accretion, and a tank never does; the three
    // lines hold at most three seats each. Anything the slot layout can't seat is named here
    // before the run rather than dropped during it.
    public SettingsConflicts Validate()
    {
        var conflicts = new SettingsConflicts();
        if (!PerRole.SeatsActive) return conflicts;

        // Exactly one healer and exactly one DPS hold Accretion; a tank never does.
        static bool IsHealer(PartyRole role) => !role.IsDps() && !role.IsTank();
        var wants = PerRole.All.Where(r => Accretion[r] == true).ToList();
        var refuses = PerRole.All.Where(r => Accretion[r] == false).ToList();
        var healersWanting = wants.Where(IsHealer).ToList();
        var dpsWanting = wants.Where(r => r.IsDps()).ToList();
        var healersRefusing = refuses.Where(IsHealer).ToList();
        var dpsRefusing = refuses.Where(r => r.IsDps()).ToList();

        conflicts.Forbidden(wants.Where(r => r.IsTank()).ToList(), "carry Accretion, which only ever goes to one healer and one DPS");
        if (healersWanting.Count > 1)
            conflicts.Add($"{SettingsConflicts.Seats(healersWanting)} both want Accretion, but only one healer gets it.");
        if (dpsWanting.Count > 1)
            conflicts.Add($"{SettingsConflicts.Seats(dpsWanting)} all want Accretion, but only one DPS gets it.");
        if (healersRefusing.Count == 2)
            conflicts.Add($"{SettingsConflicts.Seats(healersRefusing)} both refuse Accretion, but one healer always has it.");
        if (dpsRefusing.Count == 4)
            conflicts.Add($"{SettingsConflicts.Seats(dpsRefusing)} all refuse Accretion, but one DPS always has it.");

        foreach (var line in new[] { 1, 2, 3 })
            conflicts.AtMost(line == 3 ? 2 : 3, PerRole.All.Where(r => LineNumber[r] == line).ToList(),
                             line switch { 1 => "to be first in line", 2 => "to be second in line", _ => "to be third in line" });

        // Backstop for anything the named rules miss: line and Accretion interact through the
        // same eight slots, so a set that passes each rule alone can still be unseatable (three
        // supports all asking to be second in line, say). The argument is unused here, since
        // Resolve reads the seats while hosting.
        if (!conflicts.Any && !UmadP3BlackHoleState.CanSeat(Requests(PartyRole.MainTank)))
            conflicts.Add("these line and Accretion choices can't all happen in one run.");
        return conflicts;
    }
}
