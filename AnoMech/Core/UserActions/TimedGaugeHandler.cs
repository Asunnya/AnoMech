using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AnoMech.Core.UserActions;

// A gauge with time-based behavior (on top of action-driven writes), ticked each frame
// while the player is on `Job`:
//  - regen (decay=false): +Amount every Seconds, capped at the gauge Max (WHM Lily, SGE Addersgall).
//  - decay (decay=true):  reset to 0 Seconds after the last action write (GNB combo timer).
// Both are server-driven in the real game (the ActorGauge packet the sim firewalls), so we
// run them client-side. `writeTimer` (optional) is the gauge's own countdown field
// (WhiteMage.LilyTimer, Sage.AddersgallTimer) — the visible fill ring; kept in sync as ms.
internal sealed unsafe class TimedGauge(JobId job, ResourceGauge gauge, float seconds, bool decay, int amount = 1, GaugeWriter? writeTimer = null)
{
    public JobId Job => job;
    public ResourceGauge Gauge => gauge;
    public float Seconds => seconds;
    public bool Decay => decay;
    public int Amount => amount;
    public GaugeWriter? WriteTimer => writeTimer;
}

internal sealed unsafe class TimedGaugeHandler : IUserActionHandler
{
    private sealed class Tracker { public float Timer; public int LastValue = -1; }
    private readonly Dictionary<TimedGauge, Tracker> trackers = new();

    public void OnTick(float deltaSeconds)
    {
        var job = PlayerJob.Current;
        var jgm = JobGaugeManager.Instance();
        if (jgm == null) return;

        foreach (var tg in JobActions.TimedGauges)
        {
            if (tg.Job != job) continue;
            if (!trackers.TryGetValue(tg, out var tr)) trackers[tg] = tr = new Tracker();
            if (tg.Decay) TickDecay(tg, tr, jgm, deltaSeconds);
            else TickRegen(tg, tr, jgm, deltaSeconds);
        }
    }

    private static void TickRegen(TimedGauge tg, Tracker tr, JobGaugeManager* jgm, float dt)
    {
        if (tg.Gauge.Read(jgm) >= tg.Gauge.Max)   // full — the fill pauses (empty ring)
        {
            tr.Timer = 0f;
            tg.WriteTimer?.Invoke(jgm, 0);
            return;
        }
        tr.Timer += dt;
        if (tr.Timer >= tg.Seconds) { tr.Timer -= tg.Seconds; tg.Gauge.Add(jgm, tg.Amount); }
        tg.WriteTimer?.Invoke(jgm, (int)(tr.Timer * 1000f));   // elapsed ms toward the next tick
    }

    private static void TickDecay(TimedGauge tg, Tracker tr, JobGaugeManager* jgm, float dt)
    {
        var value = tg.Gauge.Read(jgm);
        if (value != tr.LastValue)          // an action wrote the gauge → refresh the window
        {
            if (value > 0) tr.Timer = tg.Seconds;
            tr.LastValue = value;
        }
        if (value <= 0) return;
        tr.Timer -= dt;
        tg.WriteTimer?.Invoke(jgm, (int)(tr.Timer * 1000f));
        if (tr.Timer <= 0f) { tg.Gauge.Write(jgm, 0); tr.LastValue = 0; }
    }
}
