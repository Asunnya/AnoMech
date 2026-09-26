using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.Top.P5Sigma;

public enum HelloWorldOption { Auto, Near, Far, No }

public sealed class TopP5SigmaStateOverrides
{
    // --- Fight-wide: one roll the whole sim shares -------------------------------------
    public Direction? NewNorthA { get; set; }
    public GlitchType? CloseFarTether { get; set; }
    public bool? TowerNorthFlip { get; set; }
    public Direction? NewNorthB { get; set; }
    public Rotation? SpinnerRotation { get; set; }
    public OmegaAttack? OmegaFForm { get; set; }

    // --- Per player: everyone has their own ---------------------------------------------
    // Near takes the first tether, Far the second, None keeps that seat out. Only one seat can
    // hold each; a later one asking for the same falls back to the roll.
    public PerRoleSetting<HelloWorldOption> HelloWorld { get; set; } = new();
    // Whether this seat starts in the six-player Dynamis group.
    public PerRoleSetting<bool> Dynamis { get; set; } = new();

    // Near/Far are indices into the two-long Hello World list; None keeps the seat out of it.
    public (Dictionary<PartyRole, int[]> Slots, Dictionary<PartyRole, bool> Membership) ResolveHelloWorld(PartyRole localPlayerRole)
    {
        var slots = new Dictionary<PartyRole, int[]>();
        var membership = new Dictionary<PartyRole, bool>();
        foreach (var (role, option) in HelloWorld.Resolve(localPlayerRole))
            switch (option)
            {
                case HelloWorldOption.Near: slots[role] = [0]; break;
                case HelloWorldOption.Far: slots[role] = [1]; break;
                case HelloWorldOption.No: membership[role] = false; break;
            }
        return (slots, membership);
    }

    public Dictionary<PartyRole, bool> ResolveDynamis(PartyRole localPlayerRole)
        => Dynamis.Resolve(localPlayerRole).ToDictionary(x => x.Role, x => x.Value);

    // One Near tether and one Far tether; six of the eight start with Dynamis.
    public SettingsConflicts Validate()
    {
        var conflicts = new SettingsConflicts();
        if (!PerRole.SeatsActive) return conflicts;

        conflicts.AtMost(1, PerRole.All.Where(r => HelloWorld[r] == HelloWorldOption.Near).ToList(), "the Near tether");
        conflicts.AtMost(1, PerRole.All.Where(r => HelloWorld[r] == HelloWorldOption.Far).ToList(), "the Far tether");
        conflicts.AtLeast(2, PerRole.All.Where(r => HelloWorld[r] == HelloWorldOption.No).ToList(), 8, "Hello World");
        conflicts.AtMost(6, PerRole.All.Where(r => Dynamis[r] == true).ToList(), "to start with Dynamis");
        conflicts.AtLeast(6, PerRole.All.Where(r => Dynamis[r] == false).ToList(), 8, "Dynamis");
        return conflicts;
    }
}
