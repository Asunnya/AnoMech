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

        // Temporary: find which gmc08 timeline slides the big saw along its lane.
        ImGui.Separator();
        ImGui.TextUnformatted("Big saw timeline test (in the arena):");
        for (uint index = 0; index < 16; index++)
        {
            if (index % 8 != 0) ImGui.SameLine();
            if (ImGui.Button($"{index}##bigsawtl")) bigSawTestResult = M9sFlailsScenario.PlayBigSawTimeline(index);
        }
        ImGui.TextUnformatted(bigSawTestResult);
    }

    private string bigSawTestResult = "";
}
