using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.M9s.Coffinmaker;

public sealed class M9sCoffinmakerSettingsWindow
{
    public M9sCoffinmakerStateOverrides Overrides { get; } = new();

    public void Draw()
    {
        if (ImGui.Button("Auto"))
        {
            Overrides.FirstSide = null;
            Overrides.Order = null;
            Overrides.SawKill = null;
        }

        if (SettingsGrid.Begin("##m9scoffinmaker"))
        {
            SettingsGrid.Row("First cleave from:");
            if (ImGui.RadioButton("Auto##firstside", Overrides.FirstSide == null)) Overrides.FirstSide = null;
            ImGui.SameLine();
            if (ImGui.RadioButton("East##firstside", Overrides.FirstSide == BossSide.East)) Overrides.FirstSide = BossSide.East;
            ImGui.SameLine();
            if (ImGui.RadioButton("West##firstside", Overrides.FirstSide == BossSide.West)) Overrides.FirstSide = BossSide.West;

            SettingsGrid.Row("Half Moon:");
            if (ImGui.RadioButton("Auto##cleaveorder", Overrides.Order == null)) Overrides.Order = null;
            ImGui.SameLine();
            if (ImGui.RadioButton("Left first##cleaveorder", Overrides.Order == CleaveOrder.LeftFirst)) Overrides.Order = CleaveOrder.LeftFirst;
            ImGui.SameLine();
            if (ImGui.RadioButton("Right first##cleaveorder", Overrides.Order == CleaveOrder.RightFirst)) Overrides.Order = CleaveOrder.RightFirst;

            SettingsGrid.Row("Saw dies:");
            if (ImGui.RadioButton("Auto##sawkill", Overrides.SawKill == null)) Overrides.SawKill = null;
            ImGui.SameLine();
            if (ImGui.RadioButton("Fast##sawkill", Overrides.SawKill == SawKill.Fast)) Overrides.SawKill = SawKill.Fast;
            ImGui.SameLine();
            if (ImGui.RadioButton("Average##sawkill", Overrides.SawKill == SawKill.Average)) Overrides.SawKill = SawKill.Average;
            ImGui.SameLine();
            if (ImGui.RadioButton("Slow##sawkill", Overrides.SawKill == SawKill.Slow)) Overrides.SawKill = SawKill.Slow;
            SettingsGrid.End();
        }
    }
}
