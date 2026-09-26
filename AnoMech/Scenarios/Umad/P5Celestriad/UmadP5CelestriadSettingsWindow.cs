using System;
using AnoMech.Core.Game.Party;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Umad.P5Celestriad;

// Fight-wide rolls (which element doubles when, each Catastrophic Choice's variant) in Draw;
// the per-seat debuff in DrawPerPlayer.
public sealed class UmadP5CelestriadSettingsWindow
{
    public UmadP5CelestriadStateOverrides Overrides { get; } = new();

    // Which seat the per-player rows are showing. UI state only; never broadcast.
    private PartyRole editingSeat = PartyRole.MainTank;

    private static readonly string[] DebuffLabels = ["Random", "Fire", "Ice", "Lightning", "Free (no debuff)"];
    private static readonly string[] VariantLabels = ["Random", "Aero (green)", "Earth (brown)"];
    private static readonly string[] DoubleOrderLabels =
    [
        "Random",
        "Fire, Ice, Lightning", "Fire, Lightning, Ice",
        "Ice, Fire, Lightning", "Ice, Lightning, Fire",
        "Lightning, Fire, Ice", "Lightning, Ice, Fire",
    ];

    public void Draw()
    {
        if (ImGui.Button("Auto")) ResetAll();

        if (SettingsGrid.Begin("##p5celestriad"))
        {
            SettingsGrid.Row("Doubled element:");
            var order = Overrides.DoubleOrder is { } o ? (int)o + 1 : 0;
            SettingsGrid.ItemWidth(200);
            if (ImGui.Combo("##celdoubleorder", ref order, DoubleOrderLabels, DoubleOrderLabels.Length))
                Overrides.DoubleOrder = order == 0 ? null : (CelestriadDoubleOrder)(order - 1);

            SettingsGrid.Row("Set 1 variant:");
            DrawVariant("##celset1", Overrides.Set1, v => Overrides.Set1 = v);

            SettingsGrid.Row("Set 3 variant:");
            DrawVariant("##celset3", Overrides.Set3, v => Overrides.Set3 = v);

            SettingsGrid.End();
        }
    }

    public void DrawPerPlayer()
    {
        if (ImGui.Button("Auto")) Overrides.Debuff.Clear();
        if (SettingsGrid.Begin("##p5celestriadplayers"))
        {
            editingSeat = SettingsGrid.SeatRow("##celseat", editingSeat);
            var whose = PerRole.SeatsActive ? "" : "Your ";

            SettingsGrid.Row($"{whose}debuff:");
            var debuff = Overrides.Debuff.Effective(editingSeat) is { } d ? (int)d + 1 : 0;
            SettingsGrid.ItemWidth(180);
            if (ImGui.Combo("##celdebuff", ref debuff, DebuffLabels, DebuffLabels.Length))
                Overrides.Debuff.Set(editingSeat, debuff == 0 ? null : (CelestriadDebuff)(debuff - 1));

            SettingsGrid.ForcedRecapRow("Debuffs set:", Overrides.Debuff);
            SettingsGrid.End();
        }
        SettingsGrid.ConflictRows(Overrides.Validate());
    }

    private static void DrawVariant(string id, CatastrophicVariantOverride current, Action<CatastrophicVariantOverride> set)
    {
        var index = (int)current;
        SettingsGrid.ItemWidth(200);
        if (ImGui.Combo(id, ref index, VariantLabels, VariantLabels.Length))
            set((CatastrophicVariantOverride)index);
    }

    private void ResetAll()
    {
        Overrides.DoubleOrder = null;
        Overrides.Set1 = CatastrophicVariantOverride.Random;
        Overrides.Set3 = CatastrophicVariantOverride.Random;
    }
}
