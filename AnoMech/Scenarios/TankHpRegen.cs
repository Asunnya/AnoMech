using System;
using System.Collections.Generic;
using AnoMech.Core;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios;

// Natural HP regen for a tank sitting below max HP after a tankbuster hit (see
// TankMitigation.ApplyTankBusterDamage). Ticked unconditionally every frame for every role in
// every scenario -- a no-op for anyone at full HP or dead, so it costs nothing elsewhere.
// Only per-role elapsed-time bookkeeping lives here (reset alongside the rest of a run's
// state, see Game.ResetInternal) -- bc->Health is already the HP ledger, no separate one needed.
public static class TankHpRegen
{
    private const float IntervalSeconds = 2f;
    private const float AmountPerTick = 20_000f;
    private const float VarianceFraction = 0.05f; // +/-5%

    private static readonly Dictionary<PartyRole, float> timerByRole = new();
    private static readonly Random rng = new();

    public static void Reset() => timerByRole.Clear();

    public static unsafe void Tick(SimParty party, float deltaSeconds)
    {
        foreach (var role in Enum.GetValues<PartyRole>())
        {
            // IsAlive(), not just a null check -- without this a KO'd character (Health 0/1
            // set by OnKilled) would get "regen'd" back to a contradictory full-HP row.
            var member = party.Get(role);
            if (member is not { } m || !m.IsAlive())
            {
                timerByRole.Remove(role);
                continue;
            }
            var bc = m.BattleCharaPtr;
            if (bc == null || bc->Health >= bc->MaxHealth)
            {
                timerByRole.Remove(role);
                continue;
            }
            var timer = timerByRole.GetValueOrDefault(role) + deltaSeconds;
            while (timer >= IntervalSeconds && bc->Health < bc->MaxHealth)
            {
                timer -= IntervalSeconds;
                var variance = 1f + (float)(rng.NextDouble() * 2.0 - 1.0) * VarianceFraction;
                var gain = (uint)(AmountPerTick * variance);
                var before = bc->Health;
                bc->Health = before + gain > bc->MaxHealth ? bc->MaxHealth : before + gain;
                DiagnosticLog.Info($"[TankHpRegen] {role} regen tick: {before}->{bc->Health} (gain={gain}, max={bc->MaxHealth}).");
            }
            timerByRole[role] = timer;
        }
    }
}
