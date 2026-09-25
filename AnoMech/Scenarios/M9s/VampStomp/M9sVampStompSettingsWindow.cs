using System;
using AnoMech.Core.Game.Party;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.M9s.VampStomp;

public sealed class M9sVampStompSettingsWindow
{
    private static readonly string[] RoleLabels = ["MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2"];

    public M9sVampStompStateOverrides Overrides { get; } = new();

    public void Draw()
    {
        if (ImGui.Button("Auto"))
        {
            Overrides.Spin = null;
            Overrides.BrutalRainTarget = null;
        }

        if (SettingsGrid.Begin("##m9svampstomp"))
        {
            SettingsGrid.Row("Bat spin:");
            if (ImGui.RadioButton("Auto##batspin", Overrides.Spin == null)) Overrides.Spin = null;
            ImGui.SameLine();
            if (ImGui.RadioButton("CW##batspin", Overrides.Spin == BatSpin.Clockwise)) Overrides.Spin = BatSpin.Clockwise;
            ImGui.SameLine();
            if (ImGui.RadioButton("CCW##batspin", Overrides.Spin == BatSpin.CounterClockwise)) Overrides.Spin = BatSpin.CounterClockwise;

            SettingsGrid.Row("Brutal Rain on:");
            if (ImGui.RadioButton("Auto##brutalrain", Overrides.BrutalRainTarget == null)) Overrides.BrutalRainTarget = null;
            foreach (var role in Enum.GetValues<PartyRole>())
            {
                ImGui.SameLine();
                if (ImGui.RadioButton($"{RoleLabels[(int)role]}##brutalrain", Overrides.BrutalRainTarget == role))
                    Overrides.BrutalRainTarget = role;
            }
            SettingsGrid.End();
        }
    }
}
