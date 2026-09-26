using System;
using System.Collections.Generic;
using AnoMech.Core.Game;

namespace AnoMech.Scenarios.Umad.P5Exaflares;

// Per-run randomization: resolves each side's ExaFlareOrder into a concrete length-6 list of line
// indices (1-6) in launch order - the three waves fire pairs ([0],[1]) then ([2],[3]) then ([4],[5]).
public sealed class UmadP5ExaflaresState
{
    public IReadOnlyList<int> LeftOrder { get; }
    public IReadOnlyList<int> RightOrder { get; }

    // Handoff to the bot strat. `Timeline` is the scenario's unscaled clock (bots schedule dodges on
    // it, not the EventTimeScale-scaled AiManager, so they stay frame-locked). `SpreadTick` is the
    // per-frame relaxation step the strat registers; the scenario's Tick drives it. Both unused in solo.
    public EventScheduler Timeline { get; }
    public Action<float>? SpreadTick { get; set; }

    private readonly Rng rng = new();

    public UmadP5ExaflaresState(UmadP5ExaflaresStateOverrides overrides, EventScheduler timeline)
    {
        Timeline = timeline;
        LeftOrder = Resolve(overrides.LeftOrder);
        RightOrder = Resolve(overrides.RightOrder);
    }

    // Network-replay constructor: reconstructs the full state (LeftOrder/RightOrder
    // are its entire meaningful surface) from values the host already rolled and
    // broadcast, instead of drawing fresh RNG. Used exclusively by a peer's local
    // "debug: bot controls my character" mode (see MultiplayerManager) so its AI
    // choreography matches what a host-side bot would actually do. Unlike the
    // other Umad scenarios' replay factories, `timeline` here is NOT the host's --
    // it must be a fresh EventScheduler the caller drives itself every frame (see
    // MultiplayerManager.Tick's P5-specific branch), since UmadP5ExaflaresAi
    // schedules its dodges onto it directly and nothing else would ever advance
    // one on a peer (a peer never runs IScenario.Tick, which is what drives the
    // real scenario's own timeline -- see UmadP5ExaflaresScenario.Tick).
    private UmadP5ExaflaresState(IReadOnlyList<int> leftOrder, IReadOnlyList<int> rightOrder, EventScheduler timeline)
    {
        Timeline = timeline;
        LeftOrder = leftOrder;
        RightOrder = rightOrder;
    }

    public static UmadP5ExaflaresState FromNetworkReplay(IReadOnlyList<int> leftOrder, IReadOnlyList<int> rightOrder, EventScheduler timeline)
        => new(leftOrder, rightOrder, timeline);

    private IReadOnlyList<int> Resolve(ExaFlareOrder order)
    {
        if (order == ExaFlareOrder.Random)
            order = rng.NextObj(
                ExaFlareOrder.Line14_25_36, ExaFlareOrder.Line14_36_25,
                ExaFlareOrder.Line25_14_36, ExaFlareOrder.Line25_36_14,
                ExaFlareOrder.Line36_14_25, ExaFlareOrder.Line36_25_14);

        return order switch
        {
            ExaFlareOrder.Line14_25_36 => [1, 4, 2, 5, 3, 6],
            ExaFlareOrder.Line14_36_25 => [1, 4, 3, 6, 2, 5],
            ExaFlareOrder.Line25_14_36 => [2, 5, 1, 4, 3, 6],
            ExaFlareOrder.Line25_36_14 => [2, 5, 3, 6, 1, 4],
            ExaFlareOrder.Line36_14_25 => [3, 6, 1, 4, 2, 5],
            ExaFlareOrder.Line36_25_14 => [3, 6, 2, 5, 1, 4],
            _ => [1, 4, 2, 5, 3, 6],
        };
    }
}
