using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.M9s.Deathmatch;

public sealed class M9sDeathmatchSettingsWindow
{
    public M9sDeathmatchStateOverrides Overrides { get; } = new();

    public void Draw()
    {
        if (ImGui.Button("Auto")) Overrides.NorthSouth = null;

        if (SettingsGrid.Begin("##m9sdeathmatch"))
        {
            SettingsGrid.Row("Towers:");
            if (ImGui.RadioButton("Auto##deathaxis", Overrides.NorthSouth == null)) Overrides.NorthSouth = null;
            ImGui.SameLine();
            if (ImGui.RadioButton("North/South##deathaxis", Overrides.NorthSouth == true)) Overrides.NorthSouth = true;
            ImGui.SameLine();
            if (ImGui.RadioButton("West/East##deathaxis", Overrides.NorthSouth == false)) Overrides.NorthSouth = false;
            SettingsGrid.End();
        }
    }
}
