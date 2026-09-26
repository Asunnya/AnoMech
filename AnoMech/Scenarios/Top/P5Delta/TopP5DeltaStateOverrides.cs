using System.Linq;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.Top.P5Delta;

public enum PlayerTetherAssignment { Auto, CloseAny, CloseInner, CloseOuter, FarAny, FarInner, FarOuter }
public enum HelloWorldOption { Auto, Near, Far, No }

// User-controlled overrides for TopP5DeltaState's randomized fields. Bound by the scenario's
// settings UI; null/default values leave the field randomized at scenario start. The state ctor
// consumes this directly.
public sealed class TopP5DeltaStateOverrides
{
    // --- Fight-wide: one roll the whole sim shares -------------------------------------
    public NorthSouth? EyeSpawn { get; set; }
    public Side? SwivelCannonSide { get; set; }

    // --- Per player: everyone has their own ---------------------------------------------
    public PerRoleSetting<PlayerTetherAssignment> Tether { get; set; } = new();
    // Monitor, Near and Far are one seat each; the earlier seat wins a contested one.
    public PerRoleSetting<bool> Monitor { get; set; } = new();
    public PerRoleSetting<HelloWorldOption> HelloWorld { get; set; } = new();
    public PerRoleSetting<bool> BeyondDefence { get; set; } = new();

    // The band a seat actually lands in once its other choices are taken into account. Mirrors
    // TopP5DeltaState's own resolution: Beyond Defence is eaten close inner, and the monitor and
    // both Hello World tethers can only be taken from the close group.
    private PlayerTetherAssignment EffectiveTether(PartyRole role)
    {
        if (BeyondDefence[role] == true) return PlayerTetherAssignment.CloseInner;
        var tether = Tether[role] ?? PlayerTetherAssignment.Auto;
        var needsClose = Monitor[role] == true
                         || HelloWorld[role] is HelloWorldOption.Near or HelloWorldOption.Far;
        if (needsClose && tether is PlayerTetherAssignment.Auto or PlayerTetherAssignment.FarAny
                or PlayerTetherAssignment.FarInner or PlayerTetherAssignment.FarOuter)
            return PlayerTetherAssignment.CloseAny;
        return tether;
    }

    // Eight tether slots in four bands of two, one monitor, one Near and one Far tether, one
    // Beyond Defence. The jobs are all taken from the close four, so asking for one of them and
    // for a far tether is a contradiction the run would quietly resolve against the host.
    public SettingsConflicts Validate()
    {
        var conflicts = new SettingsConflicts();
        if (!PerRole.SeatsActive) return conflicts;

        conflicts.AtMost(1, PerRole.All.Where(r => Monitor[r] == true).ToList(), "the monitor");
        conflicts.AtLeast(1, PerRole.All.Where(r => Monitor[r] == false).ToList(), 8, "the monitor");
        conflicts.AtMost(1, PerRole.All.Where(r => HelloWorld[r] == HelloWorldOption.Near).ToList(), "the Near tether");
        conflicts.AtMost(1, PerRole.All.Where(r => HelloWorld[r] == HelloWorldOption.Far).ToList(), "the Far tether");
        conflicts.AtLeast(2, PerRole.All.Where(r => HelloWorld[r] == HelloWorldOption.No).ToList(), 8, "Hello World");
        conflicts.AtMost(1, PerRole.All.Where(r => BeyondDefence[r] == true).ToList(), "Beyond Defence");

        var farTethered = PerRole.All.Where(r => Tether[r] is PlayerTetherAssignment.FarAny
                                                     or PlayerTetherAssignment.FarInner
                                                     or PlayerTetherAssignment.FarOuter).ToList();
        var needClose = farTethered.Where(r => Monitor[r] == true
                                               || HelloWorld[r] is HelloWorldOption.Near or HelloWorldOption.Far
                                               || BeyondDefence[r] == true).ToList();
        if (needClose.Count > 0)
            conflicts.Add($"{SettingsConflicts.Seats(needClose)} asked for a far tether and for a job only a close player can take.");

        var bdOutside = PerRole.All.Where(r => BeyondDefence[r] == true
                                              && Tether[r] is PlayerTetherAssignment.CloseOuter).ToList();
        if (bdOutside.Count > 0)
            conflicts.Add($"{SettingsConflicts.Seats(bdOutside)} asked to eat Beyond Defence on a close outer tether, and it is taken close inner.");

        var bands = PerRole.All.ToLookup(EffectiveTether);
        conflicts.AtMost(2, bands[PlayerTetherAssignment.CloseInner].ToList(), "a close inner tether");
        conflicts.AtMost(2, bands[PlayerTetherAssignment.CloseOuter].ToList(), "a close outer tether");
        conflicts.AtMost(2, bands[PlayerTetherAssignment.FarInner].ToList(), "a far inner tether");
        conflicts.AtMost(2, bands[PlayerTetherAssignment.FarOuter].ToList(), "a far outer tether");
        conflicts.AtMost(4, PerRole.All.Where(r => EffectiveTether(r) is PlayerTetherAssignment.CloseAny
                                                       or PlayerTetherAssignment.CloseInner
                                                       or PlayerTetherAssignment.CloseOuter).ToList(), "a close tether");
        conflicts.AtMost(4, PerRole.All.Where(r => EffectiveTether(r) is PlayerTetherAssignment.FarAny
                                                       or PlayerTetherAssignment.FarInner
                                                       or PlayerTetherAssignment.FarOuter).ToList(), "a far tether");
        return conflicts;
    }
}
