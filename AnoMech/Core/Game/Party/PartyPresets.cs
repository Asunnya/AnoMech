using System.Collections.Generic;
using AnoMech.Core.UserActions;

namespace AnoMech.Core.Game.Party;

// Hardcoded preset for the standard 8-job party of female Lalafell stand-ins
// dressed in their respective level-50 artifact (AF1) gear.
//
// Standard is laid out in PartyRole order — index N == (PartyRole)N.
// ForPlayerJob returns an 8-element array where the slot the local player
// fills is null, so role indices stay stable across the skip.
public static class PartyPresets
{
    public static IReadOnlyList<PartyMemberPreset?> ForPlayerJob(uint playerJob) =>
        ForRole(SkipRoleForJob(playerJob));

    public static IReadOnlyList<PartyMemberPreset?> ForRole(PartyRole skip)
    {
        var result = new PartyMemberPreset?[Standard.Length];
        for (int i = 0; i < Standard.Length; i++)
            result[i] = (PartyRole)i == skip ? null : Standard[i];
        return result;
    }

    // Tanks default to skipping Warrior (PLD player swaps to skipping Paladin).
    // Melee defaults to skipping Monk (MNK player skips Dragoon instead).
    // Casters and unknown jobs fall through to skipping Black Mage.
    public static PartyRole SkipRoleForJob(uint job) => (JobId)job switch
    {
        JobId.Paladin => PartyRole.OffTank,
        JobId.Warrior or JobId.DarkKnight or JobId.Gunbreaker => PartyRole.MainTank,
        JobId.WhiteMage or JobId.Astrologian => PartyRole.RegenHealer,
        JobId.Scholar or JobId.Sage => PartyRole.ShieldHealer,
        JobId.Monk => PartyRole.MeleeDpsA,
        JobId.Dragoon or JobId.Ninja or JobId.Samurai or JobId.Reaper or JobId.Viper => PartyRole.MeleeDpsB,
        JobId.Bard or JobId.Machinist or JobId.Dancer => PartyRole.PhysRangedDps,
        _ => PartyRole.CasterDps,
    };

    public static readonly PartyMemberPreset[] Standard =
    [
        new("Warrior", ClassJob: (byte)JobId.Warrior, Level: 90,
            Head: 2899, Body: 3222, Hands: 3684, Legs: 3460, Feet: 3891), // Fighter's
        new("Paladin", ClassJob: (byte)JobId.Paladin, Level: 90,
            Head: 2897, Body: 3220, Hands: 3682, Legs: 3458, Feet: 3889), // Gallant
        new("White Mage", ClassJob: (byte)JobId.WhiteMage, Level: 90,
            Head: 2902, Body: 3225, Hands: 3687, Legs: 3463, Feet: 3894), // Healer's
        new("Scholar", ClassJob: (byte)JobId.Scholar, Level: 90,
            Head: 2905, Body: 3228, Hands: 3689, Legs: 3466, Feet: 3897), // Scholar's
        new("Dragoon", ClassJob: (byte)JobId.Dragoon, Level: 90,
            Head: 2900, Body: 3223, Hands: 3685, Legs: 3461, Feet: 3892), // Drachen
        new("Monk", ClassJob: (byte)JobId.Monk, Level: 90,
            Head: 2898, Body: 3221, Hands: 3683, Legs: 3459, Feet: 3890), // Temple
        new("Bard", ClassJob: (byte)JobId.Bard, Level: 90,
            Head: 2901, Body: 3224, Hands: 3686, Legs: 3462, Feet: 3893), // Choral
        new("Black Mage", ClassJob: (byte)JobId.BlackMage, Level: 90,
            Head: 2903, Body: 3226, Hands: 3690, Legs: 3464, Feet: 3895), // Wizard's
    ];
}

// A seat a real player holds over the network; both override the role preset's, and ClassJob 0
// leaves the preset's job.
public sealed record NetworkSeat(string Name, byte ClassJob);

public sealed record PartyMemberPreset(
    string Name,
    byte ClassJob,
    byte Level,
    uint Head,
    uint Body,
    uint Hands,
    uint Legs,
    uint Feet);
