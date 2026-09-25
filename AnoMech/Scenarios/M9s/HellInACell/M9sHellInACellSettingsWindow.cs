using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.M9s.HellInACell;

public sealed class M9sHellInACellSettingsWindow
{
    public M9sHellInACellStateOverrides Overrides { get; } = new();

    public void Draw()
    {
        if (ImGui.Button("Auto"))
        {
            Overrides.Layout = null;
            Overrides.SpreadFirst = null;
        }

        if (SettingsGrid.Begin("##m9shellinacell"))
        {
            SettingsGrid.Row("First towers:");
            if (ImGui.RadioButton("Auto##celllayout", Overrides.Layout == null)) Overrides.Layout = null;
            for (var layout = 0; layout < M9sHellInACellState.FirstSetCount; layout++)
            {
                ImGui.SameLine();
                if (ImGui.RadioButton($"{layout + 1}##celllayout", Overrides.Layout == layout)) Overrides.Layout = layout;
            }

            SettingsGrid.Row("Ultrasonic:");
            if (ImGui.RadioButton("Auto##ultrasonic", Overrides.SpreadFirst == null)) Overrides.SpreadFirst = null;
            ImGui.SameLine();
            if (ImGui.RadioButton("Spread first##ultrasonic", Overrides.SpreadFirst == true)) Overrides.SpreadFirst = true;
            ImGui.SameLine();
            if (ImGui.RadioButton("Amp first##ultrasonic", Overrides.SpreadFirst == false)) Overrides.SpreadFirst = false;
            SettingsGrid.End();
        }
    }
}
