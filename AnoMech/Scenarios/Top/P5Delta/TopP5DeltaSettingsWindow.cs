using AnoMech.Core.Game.Party;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Top.P5Delta;

public sealed class TopP5DeltaSettingsWindow
{
    public TopP5DeltaStateOverrides Overrides { get; } = new();

    // Which seat the per-player rows are showing. UI state only; never broadcast.
    private PartyRole editingSeat = PartyRole.MainTank;

    public void Draw()
    {
#if DEBUG
        if (ImGui.Button("Auto")) ResetFight();
        if (SettingsGrid.Begin("##p5delta"))
        {
            DrawEyeSpawn();
            DrawSwivelCannon();
            SettingsGrid.End();
        }
#else
        ImGui.TextDisabled("Everything here is per player -- use the button below.");
#endif
    }

    public void DrawPerPlayer()
    {
        if (ImGui.Button("Auto")) ResetPerPlayer();
        if (SettingsGrid.Begin("##p5deltaplayers"))
        {
            editingSeat = SettingsGrid.SeatRow("##deltaseat", editingSeat);
            DrawTetherAssignment();

            var tether = Overrides.Tether.Effective(editingSeat);
            var closeOnly = tether is
                PlayerTetherAssignment.FarAny or
                PlayerTetherAssignment.FarInner or
                PlayerTetherAssignment.FarOuter;
            var bdOnly = closeOnly || tether == PlayerTetherAssignment.CloseOuter;

            if (closeOnly) ImGui.BeginDisabled();
            DrawMonitor();
            DrawHelloWorld();
            if (closeOnly) ImGui.EndDisabled();

            if (bdOnly) ImGui.BeginDisabled();
            DrawBeyondDefence();
            if (bdOnly) ImGui.EndDisabled();

            SettingsGrid.ForcedRecapRow("Tethers set:", Overrides.Tether);
            SettingsGrid.ForcedRecapRow("Monitor set:", Overrides.Monitor);
            SettingsGrid.ForcedRecapRow("Hello World set:", Overrides.HelloWorld);
            SettingsGrid.ForcedRecapRow("Beyond Defence set:", Overrides.BeyondDefence);
            SettingsGrid.End();
        }
        SettingsGrid.ConflictRows(Overrides.Validate());
    }

#if DEBUG
    private void ResetFight()
    {
        Overrides.EyeSpawn = null;
        Overrides.SwivelCannonSide = null;
    }
#endif

    private void ResetPerPlayer()
    {
        Overrides.Tether.Clear();
        Overrides.Monitor.Clear();
        Overrides.HelloWorld.Clear();
        Overrides.BeyondDefence.Clear();
    }

#if DEBUG
    private void DrawEyeSpawn()
    {
        var mode = 0;
        if (Overrides.EyeSpawn == NorthSouth.North) mode = 1;
        if (Overrides.EyeSpawn == NorthSouth.South) mode = 2;
        SettingsGrid.Row("Eye spawn:");
        if (ImGui.RadioButton("Auto##eye",  mode == 0)) Overrides.EyeSpawn = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("North##eye", mode == 1)) Overrides.EyeSpawn = NorthSouth.North;
        ImGui.SameLine();
        if (ImGui.RadioButton("South##eye", mode == 2)) Overrides.EyeSpawn = NorthSouth.South;
    }

    private void DrawSwivelCannon()
    {
        var mode = Overrides.SwivelCannonSide == null ? 0
            : Overrides.SwivelCannonSide == Side.Left ? 1
            : 2;
        SettingsGrid.Row("Swivel Cannon:");
        if (ImGui.RadioButton("Auto##swivel",  mode == 0)) Overrides.SwivelCannonSide = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("Left##swivel",  mode == 1)) Overrides.SwivelCannonSide = Side.Left;
        ImGui.SameLine();
        if (ImGui.RadioButton("Right##swivel", mode == 2)) Overrides.SwivelCannonSide = Side.Right;
    }
#endif

    private string Whose => PerRole.SeatsActive ? "" : "Your ";

    private void DrawTetherAssignment()
    {
        var t = Overrides.Tether.Effective(editingSeat);
        SettingsGrid.Row($"{Whose}tether:");
        if (ImGui.RadioButton("Auto##tether",        t == null))                             SetTether(null);
        ImGui.SameLine();
        if (ImGui.RadioButton("Close any##tether",   t == PlayerTetherAssignment.CloseAny))   SetTether(PlayerTetherAssignment.CloseAny);
        ImGui.SameLine();
        if (ImGui.RadioButton("Close inner##tether", t == PlayerTetherAssignment.CloseInner)) SetTether(PlayerTetherAssignment.CloseInner);
        ImGui.SameLine();
        if (ImGui.RadioButton("Close outer##tether", t == PlayerTetherAssignment.CloseOuter)) SetTether(PlayerTetherAssignment.CloseOuter);
        // Second row: drop the leading SameLine so the Far options wrap within the cell.
        if (ImGui.RadioButton("Far any##tether",     t == PlayerTetherAssignment.FarAny))     SetTether(PlayerTetherAssignment.FarAny);
        ImGui.SameLine();
        if (ImGui.RadioButton("Far inner##tether",   t == PlayerTetherAssignment.FarInner))   SetTether(PlayerTetherAssignment.FarInner);
        ImGui.SameLine();
        if (ImGui.RadioButton("Far outer##tether",   t == PlayerTetherAssignment.FarOuter))   SetTether(PlayerTetherAssignment.FarOuter);
    }

    private void SetTether(PlayerTetherAssignment? value) => Overrides.Tether.Set(editingSeat, value);

    private void DrawMonitor()
    {
        var m = Overrides.Monitor.Effective(editingSeat);
        SettingsGrid.Row($"{Whose}monitor:");
        if (ImGui.RadioButton("Auto##mon", m == null))  Overrides.Monitor.Set(editingSeat, null);
        ImGui.SameLine();
        if (ImGui.RadioButton("Yes##mon",  m == true))  Overrides.Monitor.Set(editingSeat, true);
        ImGui.SameLine();
        if (ImGui.RadioButton("No##mon",   m == false)) Overrides.Monitor.Set(editingSeat, false);
    }

    private void DrawHelloWorld()
    {
        var h = Overrides.HelloWorld.Effective(editingSeat);
        SettingsGrid.Row($"{Whose}Hello World:");
        if (ImGui.RadioButton("Auto##hw", h == null))                  Overrides.HelloWorld.Set(editingSeat, null);
        ImGui.SameLine();
        if (ImGui.RadioButton("Near##hw", h == HelloWorldOption.Near)) Overrides.HelloWorld.Set(editingSeat, HelloWorldOption.Near);
        ImGui.SameLine();
        if (ImGui.RadioButton("Far##hw",  h == HelloWorldOption.Far))  Overrides.HelloWorld.Set(editingSeat, HelloWorldOption.Far);
        ImGui.SameLine();
        if (ImGui.RadioButton("No##hw",   h == HelloWorldOption.No))   Overrides.HelloWorld.Set(editingSeat, HelloWorldOption.No);
    }

    private void DrawBeyondDefence()
    {
        var b = Overrides.BeyondDefence.Effective(editingSeat);
        SettingsGrid.Row($"{Whose}Beyond Defence:");
        if (ImGui.RadioButton("Auto##bd", b == null))  Overrides.BeyondDefence.Set(editingSeat, null);
        ImGui.SameLine();
        if (ImGui.RadioButton("Yes##bd",  b == true))  Overrides.BeyondDefence.Set(editingSeat, true);
        ImGui.SameLine();
        if (ImGui.RadioButton("No##bd",   b == false)) Overrides.BeyondDefence.Set(editingSeat, false);
    }
}
