using System;
using System.Linq;

namespace AnoMech.Scenarios.Umad.P5Celestriad;

// Free is the two seats with no element, who fill in for whichever one doubles that set.
public enum CelestriadDebuff { Fire, Ice, Lightning, Free }

public enum CatastrophicVariantOverride { Random, Aero, Earth }

// Each element doubles exactly once across the three sets, so it is one order rather than three
// independent picks.
public enum CelestriadDoubleOrder
{
    FireIceLightning,
    FireLightningIce,
    IceFireLightning,
    IceLightningFire,
    LightningFireIce,
    LightningIceFire,
}

// User-controlled overrides for UmadP5CelestriadState's randomized fields. Random / null leaves
// the field to the roll at scenario start.
public sealed class UmadP5CelestriadStateOverrides
{
    public CelestriadDoubleOrder? DoubleOrder { get; set; } = null;
    public CatastrophicVariantOverride Set1 { get; set; } = CatastrophicVariantOverride.Random;
    public CatastrophicVariantOverride Set3 { get; set; } = CatastrophicVariantOverride.Random;

    public PerRoleSetting<CelestriadDebuff> Debuff { get; set; } = new();

    public const int SeatsPerDebuff = 2;

    public static string Label(CelestriadDebuff debuff) =>
        debuff == CelestriadDebuff.Free ? "no debuff" : debuff.ToString();

    // Two seats carry each element and two carry none, so a third claim is a party the fight
    // never deals.
    public SettingsConflicts Validate()
    {
        var conflicts = new SettingsConflicts();
        if (!PerRole.SeatsActive) return conflicts;
        foreach (var debuff in Enum.GetValues<CelestriadDebuff>())
            conflicts.AtMost(SeatsPerDebuff, PerRole.All.Where(r => Debuff[r] == debuff).ToList(), Label(debuff));
        return conflicts;
    }
}
