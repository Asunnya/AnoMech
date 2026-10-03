using System;
using System.Linq;

namespace AnoMech.Scenarios.Top.P2PartySynergy;

// Same order as LockonId.Playstation: the symbol's index is its conga pair.
public enum PlaystationSymbol { Cross, Square, Circle, Triangle }

// User-controlled overrides for TopP2PartySynergyState's randomized fields.
// Bound by the scenario's settings UI; null values leave the field randomized
// at scenario start. The state ctor consumes this directly.
public sealed class TopP2PartySynergyStateOverrides
{
    public Direction? NewNorthA { get; set; }
    public Direction? NewNorthB { get; set; }
    // Where Omega-F's clone stands; Omega-M's is opposite. The roll is always 22.5° off a compass point.
    public Direction? AttackDir { get; set; }
    public GlitchType? Glitch { get; set; }
    public OmegaAttack? AttackM { get; set; }
    public OmegaAttack? AttackF { get; set; }

    // Two seats per symbol and two stack targets; a seat arriving at a full one keeps the roll.
    public PerRoleSetting<PlaystationSymbol> Symbol { get; set; } = new();
    public PerRoleSetting<bool> Stack { get; set; } = new();

    public SettingsConflicts Validate()
    {
        var conflicts = new SettingsConflicts();
        if (!PerRole.SeatsActive) return conflicts;
        foreach (var symbol in Enum.GetValues<PlaystationSymbol>())
            conflicts.AtMost(2, PerRole.All.Where(r => Symbol[r] == symbol).ToList(), $"the {symbol} symbol");
        conflicts.AtMost(2, PerRole.All.Where(r => Stack[r] == true).ToList(), "a stack");
        conflicts.AtLeast(2, PerRole.All.Where(r => Stack[r] == false).ToList(), 8, "a stack");
        return conflicts;
    }
}
