using System.Reflection;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using Dalamud.Plugin.Services;

namespace AnoMech.Tests;

internal sealed record Death(PartyRole Role, string Cause, float Time);

// One headless scenario run: every seat (the player's included) on the strat's AI, on a fresh
// fake game, ticked at a fixed rate until it ends.
internal sealed record ScenarioRun(int Seed, PartyRole PlayerRole, float Elapsed, IReadOnlyList<Death> Deaths, string? Failure, IReadOnlyList<string> Warnings)
{
    public const float FrameSeconds = 1f / 60f;
    public const float TimeoutSeconds = 600f;

    public bool Passed => Failure is null && Deaths.Count == 0;

    public static ScenarioRun Execute(Type scenarioType, int strat, int seed)
    {
        var fake = FakeGame.Install();
        var log = RecordingLog.Create();
        var role = (PartyRole)new Rng(seed).Fork("player-seat").Next(8);
        // A real player's job always fits their seat; job-keyed actions (tank invulns) read it.
        fake.BattleCharas.Player.ClassJob = PartyPresets.Standard[(int)role].ClassJob;
        var deaths = new List<Death>();
        var elapsed = 0f;
        string? failure = null;

        DalamudServices.Install(nameof(Plugin.Log), log);
        DalamudServices.Install(nameof(Plugin.Config), new Configuration());
        DebugBotControl.Enabled = true;
        var game = new Game();
        DalamudServices.Install(nameof(Plugin.GameInstance), game);
        try
        {
            var scenario = game.Scenarios.Single(s => s.GetType() == scenarioType);
            game.PartyMemberKilled += (r, cause) => deaths.Add(new Death(r, cause, elapsed));
            game.RunScenario(new RunScenarioParams(scenario, role, strat, 0, seed));
            fake.Frame(FrameSeconds, game.Tick);
            if (!game.IsScenarioActive)
                failure = "did not start";

            while (failure is null && !game.HasScenarioSucceeded && !game.Paused)
            {
                if (elapsed >= TimeoutSeconds)
                {
                    failure = $"not finished after {TimeoutSeconds} s";
                    break;
                }
                fake.Frame(FrameSeconds, game.Tick);
                elapsed += FrameSeconds;
            }
        }
        catch (Exception e)
        {
            failure = $"threw at t={elapsed:F2}: {e}";
        }
        finally
        {
            DebugBotControl.Enabled = false;
            game.Dispose();
        }
        return new ScenarioRun(seed, role, elapsed, deaths, failure, log.Warnings);
    }

    public override string ToString()
    {
        var lines = new List<string> { $"seed {Seed} (player {PlayerRole}, t={Elapsed:F1}):" };
        if (Failure is not null) lines.Add($"  {Failure}");
        lines.AddRange(Deaths.Select(d => $"  {d.Role} died at t={d.Time:F2}: {d.Cause}"));
        lines.AddRange(Warnings.Distinct().Select(w => $"  warning: {w}"));
        return string.Join(Environment.NewLine, lines);
    }

    // Keeps the run's warnings and errors for the failure message; everything else is dropped.
    public class RecordingLog : DispatchProxy
    {
        public List<string> Warnings { get; } = [];

        public static RecordingLog Create() => (RecordingLog)(object)Create<IPluginLog, RecordingLog>();

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name is nameof(IPluginLog.Warning) or nameof(IPluginLog.Error) or nameof(IPluginLog.Fatal))
                Warnings.Add(string.Join(" ", args?.Where(a => a is string or Exception) ?? []));
            return method is { ReturnType.IsValueType: true } && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }
}
