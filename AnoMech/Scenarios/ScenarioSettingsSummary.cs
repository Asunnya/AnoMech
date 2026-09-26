using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios;

// Turns a scenario's overrides object into "Label: value" lines for the multiplayer lobby, so
// peers can see the combination the host set up without every scenario writing its own
// summary. Only non-default (non-null, non-Auto/Random) values are listed; a plain bool has no
// unset state (a roll is a bool?), so those are debug toggles and stay out. A value whose type
// exposes static instances (Direction.N, GlitchType.Far, OmegaAttack.Sword, ...) is named
// after the matching static field; enums print their member name; PartyRole prints its seat.
public static class ScenarioSettingsSummary
{
    public static List<string> Describe(object? overrides)
    {
        var lines = new List<string>();
        if (overrides == null) return lines;
        foreach (var property in overrides.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;
            if (property.PropertyType == typeof(bool)) continue;
            object? value;
            try { value = property.GetValue(overrides); }
            catch { continue; }
            if (value == null) continue;
            // A per-player setting names the seats it is forced for; an unset one says nothing.
            if (value is IPerRoleSetting perRole)
            {
                if (perRole.Describe() is { } seats) lines.Add($"{Label(property.Name)}: {seats}");
                continue;
            }
            var text = Format(value);
            if (text is null or "Auto" or "Random") continue;
            lines.Add($"{Label(property.Name)}: {text}");
        }
        return lines;
    }

    // Shared with PerRoleSetting.Describe, so a seat's value reads the same as a fight-wide one.
    internal static string? FormatValue(object value) => Format(value);

    private static string? Format(object value)
    {
        switch (value)
        {
            case bool b: return b ? "Yes" : "No";
            case PartyRole role: return SettingsGrid.RoleLabel(role);
            case Enum e: return e.ToString();
            case string s: return s;
            case uint u: return $"0x{u:X}";
            case int or float or double: return value.ToString();
        }
        var type = value.GetType();
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            if (field.FieldType == type && ReferenceEquals(field.GetValue(null), value))
                return field.Name;
        return value.ToString();
    }

    // "PlayerNumber" -> "Player number", "NewNorthA" -> "New north A".
    private static string Label(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
            {
                sb.Append(' ');
                sb.Append(i + 1 < name.Length && char.IsLower(name[i + 1]) ? char.ToLowerInvariant(c) : c);
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
