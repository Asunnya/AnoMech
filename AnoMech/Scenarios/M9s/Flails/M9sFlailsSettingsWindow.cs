using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.M9s.Flails;

public sealed class M9sFlailsSettingsWindow
{
    public M9sFlailsStateOverrides Overrides { get; } = new();

    public void Draw()
    {
        if (ImGui.Button("Auto")) Overrides.Layout = null;

        if (SettingsGrid.Begin("##m9sflails"))
        {
            SettingsGrid.Row("Tower layout:");
            if (ImGui.RadioButton("Auto##flaillayout", Overrides.Layout == null)) Overrides.Layout = null;
            for (var layout = 0; layout < M9sFlailsState.LayoutCount; layout++)
            {
                ImGui.SameLine();
                if (ImGui.RadioButton($"{layout + 1}##flaillayout", Overrides.Layout == layout)) Overrides.Layout = layout;
            }

            SettingsGrid.End();
        }
    }
}
