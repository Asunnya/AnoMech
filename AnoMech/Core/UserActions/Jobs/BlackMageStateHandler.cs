using System;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Gauge;

namespace AnoMech.Core.UserActions.Jobs;

// Black Mage's elemental gauge — the Astral Fire / Umbral Ice state machine plus Umbral Hearts,
// Astral Soul (packed in EnochianFlags bits 2+), the 15s Enochian countdown, and the Paradox marker.
// A scalar ResourceGauge can't hold this. Polyglot stays in the JobActions data table (do not write it).
internal sealed unsafe class BlackMageStateHandler : IUserActionHandler
{
    // Resolved (post-GetAdjustedActionId) ids. Fire/Blizzard resolve to Paradox while its marker is up,
    // so the base ids only fire on the neutral/transition case. Fire II / Blizzard II upgrade to their
    // High-* variants at level 82 — both handled.
    private const uint Fire = 141, FireII = 147, HighFireII = 25794, FireIII = 152, FireIV = 3577;
    private const uint Blizzard = 142, BlizzardII = 25793, HighBlizzardII = 25795, BlizzardIII = 154, BlizzardIV = 3576;
    private const uint Freeze = 159, Flare = 162, FlareStar = 36989, Despair = 16505;
    private const uint UmbralSoul = 16506, Transpose = 149, Manafont = 158, Paradox = 25797;

    public void OnAction(ActionType actionType, uint actionId)
    {
        if (actionType != ActionType.Action) return;
        if (PlayerJob.Current != JobId.BlackMage) return;
        var jgm = JobGaugeManager.Instance();
        if (jgm == null) return;
        var g = &jgm->BlackMage;

        switch (actionId)
        {
            // Fire I: neutral/ice -> Astral Fire I, already-fire -> +1 (cap 3); burns one Umbral Heart.
            // VERIFY in-game: whether Fire I from Umbral Ice lands on AF1 (assumed here, per the brief)
            // or instead drops to neutral (the "removes Umbral Ice" reading of the tooltip).
            case Fire:
                SetStance(g, g->ElementStance <= 0 ? (sbyte)1 : (sbyte)Math.Min(g->ElementStance + 1, 3));
                ConsumeHearts(g, 1);
                break;

            case FireII or HighFireII:
                SetStance(g, 3);
                ConsumeHearts(g, 1);
                break;

            case FireIII or Despair:
                SetStance(g, 3);
                break;

            case Flare:
                SetStance(g, 3);
                ConsumeHearts(g, 3);   // Flare consumes all remaining (heart cap is 3)
                AddAstralSoul(g, 3);
                break;

            case FireIV:
                Refresh(g);
                AddAstralSoul(g, 1);
                break;

            case FlareStar:
                Refresh(g);
                SetAstralSoul(g, 0);   // spends the full 6 stacks
                break;

            // Enhanced Manafont (level 84+): AF3 + 3 Umbral Hearts + Paradox.
            case Manafont:
                SetStance(g, 3);
                g->UmbralHearts = 3;
                g->EnochianFlags |= EnochianFlags.Paradox;
                break;

            // Blizzard I: neutral/fire -> Umbral Ice I, already-ice -> +1 stack (cap -3).
            // VERIFY in-game: whether Blizzard I from Astral Fire lands on UI1 (assumed) or drops to neutral.
            case Blizzard:
                SetStance(g, g->ElementStance >= 0 ? (sbyte)-1 : (sbyte)Math.Max(g->ElementStance - 1, -3));
                TryArmParadox(g);
                break;

            case BlizzardII or HighBlizzardII or BlizzardIII:
                SetStance(g, -3);
                TryArmParadox(g);
                break;

            case BlizzardIV or Freeze:
                Refresh(g);
                g->UmbralHearts = 3;
                TryArmParadox(g);
                break;

            case UmbralSoul:
                SetStance(g, (sbyte)Math.Max(g->ElementStance - 1, -3));
                g->UmbralHearts = (byte)Math.Min(g->UmbralHearts + 1, 3);
                TryArmParadox(g);
                break;

            case Transpose:
                if (g->ElementStance > 0) SetStance(g, -1);
                else if (g->ElementStance < 0) SetStance(g, 1);
                break;   // no-op at neutral

            case Paradox:
                Refresh(g);
                g->EnochianFlags &= ~EnochianFlags.Paradox;
                break;
        }
    }

    public void OnTick(float deltaSeconds)
    {
        if (PlayerJob.Current != JobId.BlackMage) return;
        var jgm = JobGaugeManager.Instance();
        if (jgm == null) return;
        var g = &jgm->BlackMage;

        if (g->EnochianTimer <= 0) return;
        // Approximate: hold the stance indefinitely under Umbral Ice (Umbral Soul is the in-game refresh);
        // only Astral Fire is drained here.
        // VERIFY in-game: whether the Enochian countdown truly pauses under Umbral Ice.
        if (g->ElementStance < 0) return;

        g->EnochianTimer = (short)(g->EnochianTimer - deltaSeconds * 1000f);
        if (g->EnochianTimer <= 0)
        {
            g->EnochianTimer = 0;
            g->ElementStance = 0;
            g->UmbralHearts = 0;
            g->EnochianFlags = EnochianFlags.None;   // clears Enochian, Paradox and Astral Soul at once
        }
    }

    // Enter/maintain an aspect: set the stance, (re)grant Enochian and reset the 15s countdown.
    private static void SetStance(BlackMageGauge* g, sbyte stance)
    {
        g->ElementStance = stance;
        g->EnochianFlags |= EnochianFlags.Enochian;
        g->EnochianTimer = 15000;
    }

    // In-stance cast that keeps the current aspect but refreshes its timer.
    private static void Refresh(BlackMageGauge* g) => g->EnochianTimer = 15000;

    private static void ConsumeHearts(BlackMageGauge* g, int n)
        => g->UmbralHearts = (byte)Math.Max(g->UmbralHearts - n, 0);

    // Astral Soul (0..6) is packed into EnochianFlags bits 2+; preserve the Enochian/Paradox bits (low 2).
    private static void SetAstralSoul(BlackMageGauge* g, int stacks)
        => g->EnochianFlags = (EnochianFlags)(((int)g->EnochianFlags & 3) | (Math.Clamp(stacks, 0, 6) << 2));

    private static void AddAstralSoul(BlackMageGauge* g, int delta)
        => SetAstralSoul(g, (((int)g->EnochianFlags >> 2) & 7) + delta);

    // Fire/Blizzard swap to Paradox once its gauge marker is armed. The marker is granted on reaching
    // Umbral Ice III with 3 Umbral Hearts (plus directly by Manafont); it persists — carried into Astral
    // Fire — until Paradox is cast.
    // VERIFY in-game: exact grant condition. The brief also lists an "AF3 with Enochian" source, but a
    // blanket Astral-Fire check would spuriously re-arm Paradox on every Fire IV / Firestarter Fire III,
    // so the fire-side source is modelled solely as Manafont's explicit grant.
    private static void TryArmParadox(BlackMageGauge* g)
    {
        if (g->ElementStance == -3 && g->UmbralHearts == 3)
            g->EnochianFlags |= EnochianFlags.Paradox;
    }
}
