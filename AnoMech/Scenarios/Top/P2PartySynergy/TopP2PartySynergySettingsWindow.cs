using System;
using AnoMech.Core.Game.Party;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Top.P2PartySynergy;

public sealed class TopP2PartySynergySettingsWindow
{
    public TopP2PartySynergyStateOverrides Overrides { get; } = new();

    // Which seat the per-player rows are showing. UI state only; never broadcast.
    private PartyRole editingSeat = PartyRole.MainTank;

    public void Draw()
    {
        var solo = !PerRole.SeatsActive;
        if (ImGui.Button("Auto"))
        {
            ResetAll();
            if (solo) ResetMine();
        }
        if (SettingsGrid.Begin("##partysynergy"))
        {
#if DEBUG
            DrawNewNorthA();
            DrawNewNorthB();
#endif
            DrawGlitch();
            DrawAttackM();
            DrawAttackF();
            if (solo) DrawPlayerRows();
            SettingsGrid.End();
        }
    }

    public void DrawPerPlayer()
    {
        if (ImGui.Button("Auto")) ResetPerPlayer();
        if (SettingsGrid.Begin("##partysynergyplayers"))
        {
            editingSeat = SettingsGrid.SeatRow("##partysynergyseat", editingSeat);
            DrawPlayerRows();
            SettingsGrid.ForcedRecapRow("Symbols set:", Overrides.Symbol);
            SettingsGrid.ForcedRecapRow("Stacks set:", Overrides.Stack);
            SettingsGrid.End();
        }
        SettingsGrid.ConflictRows(Overrides.Validate());
    }

    private void DrawPlayerRows()
    {
        DrawSymbol();
        DrawStack();
    }

    private void ResetAll()
    {
#if DEBUG
        Overrides.NewNorthA = null;
        Overrides.NewNorthB = null;
#endif
        Overrides.Glitch    = null;
        Overrides.AttackM   = null;
        Overrides.AttackF   = null;
    }

    private void ResetPerPlayer()
    {
        Overrides.Symbol.Clear();
        Overrides.Stack.Clear();
    }

    // Solo's own picks only: a host's seat assignments are kept apart.
    private void ResetMine()
    {
        Overrides.Symbol.Mine = null;
        Overrides.Stack.Mine = null;
    }

    private void DrawSymbol()
    {
        var s = Overrides.Symbol.Effective(editingSeat);
        SettingsGrid.PlayerRow("symbol:");
        if (ImGui.RadioButton("Auto##symbol", s == null)) Overrides.Symbol.Set(editingSeat, null);
        foreach (var symbol in Enum.GetValues<PlaystationSymbol>())
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{symbol}##symbol", s == symbol)) Overrides.Symbol.Set(editingSeat, symbol);
        }
    }

    private void DrawStack()
    {
        var s = Overrides.Stack.Effective(editingSeat);
        SettingsGrid.PlayerRow("stack:");
        if (ImGui.RadioButton("Auto##stack", s == null))  Overrides.Stack.Set(editingSeat, null);
        ImGui.SameLine();
        if (ImGui.RadioButton("Yes##stack",  s == true))  Overrides.Stack.Set(editingSeat, true);
        ImGui.SameLine();
        if (ImGui.RadioButton("No##stack",   s == false)) Overrides.Stack.Set(editingSeat, false);
    }

#if DEBUG
    private void DrawNewNorthA()
    {
        SettingsGrid.Row("New north (A):");
        if (ImGui.RadioButton("Auto##northA", Overrides.NewNorthA == null)) Overrides.NewNorthA = null;
        foreach (var d in Direction.All)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{d.Name()}##northA", Overrides.NewNorthA == d)) Overrides.NewNorthA = d;
        }
    }

    private void DrawNewNorthB()
    {
        SettingsGrid.Row("New north (B):");
        if (ImGui.RadioButton("Auto##northB", Overrides.NewNorthB == null)) Overrides.NewNorthB = null;
        foreach (var d in Direction.All)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{d.Name()}##northB", Overrides.NewNorthB == d)) Overrides.NewNorthB = d;
        }
    }
#endif

    private void DrawGlitch()
    {
        var v = Overrides.Glitch;
        SettingsGrid.Row("Glitch:");
        if (ImGui.RadioButton("Auto##glitch", v == null))           Overrides.Glitch = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("Mid##glitch",  v == GlitchType.Mid)) Overrides.Glitch = GlitchType.Mid;
        ImGui.SameLine();
        if (ImGui.RadioButton("Far##glitch",  v == GlitchType.Far)) Overrides.Glitch = GlitchType.Far;
    }

    private void DrawAttackM()
    {
        var v = Overrides.AttackM;
        SettingsGrid.Row("Omega-M form:");
        if (ImGui.RadioButton("Auto##atkM",   v == null))               Overrides.AttackM = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("Sword##atkM",  v == OmegaAttack.Sword))  Overrides.AttackM = OmegaAttack.Sword;
        ImGui.SameLine();
        if (ImGui.RadioButton("Shield##atkM", v == OmegaAttack.Shield)) Overrides.AttackM = OmegaAttack.Shield;
    }

    private void DrawAttackF()
    {
        var v = Overrides.AttackF;
        SettingsGrid.Row("Omega-F form:");
        if (ImGui.RadioButton("Auto##atkF",  v == null))              Overrides.AttackF = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("Staff##atkF", v == OmegaAttack.Staff)) Overrides.AttackF = OmegaAttack.Staff;
        ImGui.SameLine();
        if (ImGui.RadioButton("Legs##atkF",  v == OmegaAttack.Legs))  Overrides.AttackF = OmegaAttack.Legs;
    }
}
