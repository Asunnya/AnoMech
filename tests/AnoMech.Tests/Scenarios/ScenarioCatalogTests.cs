using AnoMech.Core.Game;
using AnoMech.Scenarios;

namespace AnoMech.Tests;

public class ScenarioCatalogTests
{
    private static IEnumerable<TestCaseData> ScenarioStrats()
        => from scenario in ScenarioCatalog.Create()
           from strat in scenario.AiStrats.Select((ai, index) => (ai, index))
           select new TestCaseData(scenario.GetType(), strat.index)
               .SetName($"{Game.FullName(scenario)} [{strat.ai.Name}]");

    [TestCaseSource(nameof(ScenarioStrats))]
    public void SunnyDay(Type scenarioType, int strat)
    {
        var seeds = TestContext.Parameters.Get("SunnySeeds", 1);
        var failures = Enumerable.Range(0, seeds)
            .Select(_ => ScenarioRun.Execute(scenarioType, strat, Random.Shared.Next()))
            .Where(run => !run.Passed)
            .ToList();

        Assert.That(failures, Is.Empty, () => $"{failures.Count}/{seeds} seeds failed:{Environment.NewLine}" + string.Join(Environment.NewLine,
            failures.Select(run => $"{run}{Environment.NewLine}  replay: [TestCase(typeof(global::{scenarioType.FullName}), {strat}, {run.Seed})]")));
    }

    // Paste the replay line of a failing seed here to debug it.
    [Explicit]
    [TestCase(typeof(global::AnoMech.Scenarios.Umad.P3BlackHole.UmadP3BlackHoleScenario), 1, 1795528490)]
    public void Replay(Type scenarioType, int strat, int seed)
    {
        var run = ScenarioRun.Execute(scenarioType, strat, seed);
        Assert.That(run.Passed, run.ToString);
    }
}
