using System.Numerics;
using AnoMech.Core;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;

namespace AnoMech.Tests;

// A run where the player (seated in the role under test) breaks the strat on purpose, and the
// mechanic must kill exactly the expected roles:
//
//     Negative<TopP2PartySynergyScenario>(PartyRole.MeleeDpsA)
//         .Overrides<TopP2PartySynergyStateOverrides>(o => o.AttackM = OmegaAttack.Shield)
//         .TeleportAt(12f, to: new(0, 0))
//         .ShouldKill(ActionId.BeyondStrength, PartyRole.MeleeDpsA);
internal static class NegativeRun
{
    public const uint ArenaWall = 0;

    public static NegativeRun<TScenario> Negative<TScenario>(PartyRole role, int strat = 0) where TScenario : IScenario
        => new(role, strat);
}

internal sealed class NegativeRun<TScenario>(PartyRole role, int strat) where TScenario : IScenario
{
    private ScenarioRunOptions options = new() { PlayerRole = role, WriteArtifactsOnFailure = false };
    private int? seed;

    public NegativeRun<TScenario> Overrides<TOverrides>(Action<TOverrides> set)
    {
        options = options with { Overrides = o => set((TOverrides)o) };
        return this;
    }

    public NegativeRun<TScenario> TeleportAt(float time, Vector2 to)
    {
        options = options with { Takeover = new PlayerTakeover(time, to) };
        return this;
    }

    public NegativeRun<TScenario> Seed(int value)
    {
        seed = value;
        return this;
    }

    // Only the first lethal moment is judged: whatever dies after it is a consequence (a stack one
    // short, a tether partner left alone), not what the test broke. `actionId` 0 = the arena wall.
    public void ShouldKill(uint actionId, params PartyRole[] roles)
    {
        var runSeed = seed ?? Random.Shared.Next();
        var run = ScenarioRun.Execute(typeof(TScenario), strat, runSeed, options);
        var cause = actionId == NegativeRun.ArenaWall ? ArenaWallCause : ActionLookup.Name(actionId);
        if (Mismatch(run, cause, roles) is not { } problem) return;

        // Deterministic, so a rerun reproduces the failure with its artifacts on disk.
        var detailed = ScenarioRun.Execute(typeof(TScenario), strat, runSeed, options with { AlwaysWriteArtifacts = true });
        Assert.Fail($"{problem}{Environment.NewLine}{detailed}{Environment.NewLine}  replay: .Seed({runSeed})");
    }

    private const string ArenaWallCause = "Walked out of arena";

    private static string? Mismatch(ScenarioRun run, string cause, PartyRole[] roles)
    {
        var expected = $"expected {string.Join(", ", roles)} to die to {cause}";
        if (run.Failure is not null) return $"{expected}, but the run failed.";
        if (run.Deaths.Count == 0) return $"{expected}, but nobody died.";
        var firstTime = run.Deaths.Min(d => d.Time);
        var first = run.Deaths.Where(d => d.Time <= firstTime + ScenarioRun.FrameSeconds / 2).ToList();
        var wrong = first.Where(d => !roles.Contains(d.Role) || !d.Cause.Contains(cause, StringComparison.Ordinal)).ToList();
        if (wrong.Count > 0) return $"{expected}, but {string.Join("; ", wrong.Select(d => $"{d.Role} died to \"{d.Cause}\""))}.";
        var survived = roles.Where(r => first.All(d => d.Role != r)).ToList();
        if (survived.Count > 0) return $"{expected}, but {string.Join(", ", survived)} did not die with them.";
        return null;
    }
}
