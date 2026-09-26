using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios;

// Banked absorbable damage for shield mitigations, as a fraction of the shielded role's max
// HP. ClearAllVisuals is separate from Reset: the native ShieldValue byte written onto the
// real local player would otherwise show a fake shield bar in real content forever.
public static class TankShieldTracker
{
    private readonly record struct ShieldChunk(float FractionOfMaxHp, long ExpiresAtMs);

    // A chunk only SetFromPeerReport replaces, never a timer.
    private const long NoExpiry = long.MaxValue;

    private static readonly Dictionary<PartyRole, List<ShieldChunk>> chunksByRole = new();

    public static void Reset() => chunksByRole.Clear();

    // Additive: real shields from different sources stack.
    public static void Grant(PartyRole role, float fractionOfMaxHp, float durationSeconds)
    {
        if (fractionOfMaxHp <= 0f) return;
        if (!chunksByRole.TryGetValue(role, out var list)) chunksByRole[role] = list = [];
        list.Add(new ShieldChunk(fractionOfMaxHp, Environment.TickCount64 + (long)(durationSeconds * 1000f)));
    }

    // Host-only: replaces the role's whole banked shield with a peer's self-reported total;
    // the peer's next report (including 0f) is what clears it.
    public static void SetFromPeerReport(PartyRole role, float fractionOfMaxHp)
    {
        chunksByRole[role] = fractionOfMaxHp > 0f ? [new ShieldChunk(fractionOfMaxHp, NoExpiry)] : [];
    }

    private static void DropExpired(List<ShieldChunk> list)
    {
        var now = Environment.TickCount64;
        list.RemoveAll(c => c.ExpiresAtMs <= now);
    }

    public static float RemainingFraction(PartyRole role)
    {
        if (!chunksByRole.TryGetValue(role, out var list)) return 0f;
        DropExpired(list);
        return list.Sum(c => c.FractionOfMaxHp);
    }

    // Drains oldest-first; returns how much was absorbed.
    public static float Consume(PartyRole role, float fractionOfDamage)
    {
        if (fractionOfDamage <= 0f) return 0f;
        if (!chunksByRole.TryGetValue(role, out var list)) return 0f;
        DropExpired(list);
        var remaining = fractionOfDamage;
        var absorbed = 0f;
        for (var i = 0; i < list.Count && remaining > 0f; i++)
        {
            var take = Math.Min(list[i].FractionOfMaxHp, remaining);
            absorbed += take;
            remaining -= take;
            list[i] = list[i] with { FractionOfMaxHp = list[i].FractionOfMaxHp - take };
        }
        list.RemoveAll(c => c.FractionOfMaxHp <= 0f);
        return absorbed;
    }

    // BattleChara.ShieldValue drives the gold overlay on the HP bar and party list. Cosmetic
    // only, so the clamp to 0-100 never affects absorption.
    public static unsafe void RefreshVisual(SimCharacter member, PartyRole role)
    {
        var bc = member.BattleCharaPtr;
        if (bc == null) return;
        var pct = RemainingFraction(role) * 100f;
        bc->ShieldValue = (byte)Math.Clamp(pct, 0f, 100f);
    }

    // On every sim exit, same call sites as RestoreGaugeIllusion.
    public static unsafe void ClearAllVisuals(SimParty party)
    {
        foreach (var role in Enum.GetValues<PartyRole>())
        {
            if (party.Get(role) is not { } member) continue;
            var bc = member.BattleCharaPtr;
            if (bc != null) bc->ShieldValue = 0;
        }
    }
}
