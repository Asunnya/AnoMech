using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;

namespace AnoMech.Tests;

internal sealed record Death(PartyRole Role, string Cause, float Time, string Snapshot);

internal sealed record ScenarioRunOptions
{
    // Ends the run at this scenario time, as a pass unless something already failed.
    public float? StopAt { get; init; }

    // Called after every frame; see ScenarioProbe.
    public Action<ScenarioProbe>? Probe { get; init; }

    // Writes the artifact folder even for a passing run.
    public bool AlwaysWriteArtifacts { get; init; }

    // A run expected to end in deaths only writes artifacts when asked to.
    public bool WriteArtifactsOnFailure { get; init; } = true;

    // Seats the player here instead of the seed's pick, so per-role overrides (PerRoleSetting.Mine) reach it.
    public PartyRole? PlayerRole { get; init; }

    // Edits the scenario's SettingsOverrides before the run starts. Runs act as a host, so per-role
    // settings go in their seats, not in Mine.
    public Action<object>? Overrides { get; init; }

    public PlayerTakeover? Takeover { get; init; }
}

// From `At` on the player stops following the AI, as a human who froze there would; knockbacks and
// forced moves still apply. `TeleportTo` (scenario-local XZ) is where they are put at that moment.
internal sealed record PlayerTakeover(float At, Vector2? TeleportTo);

// One headless scenario run: every seat (the player's included) on the strat's AI, on a fresh
// fake game, ticked at a fixed rate until it ends.
internal sealed record ScenarioRun(
    Type ScenarioType, int Strat, int Seed, PartyRole PlayerRole, float Elapsed, IReadOnlyList<Death> Deaths,
    string? Failure, IReadOnlyList<string> Warnings, string? ArtifactDirectory)
{
    public const float FrameSeconds = 1f / 60f;
    public const float TimeoutSeconds = 600f;
    private const float AoeCheckWindowSeconds = 1f;

    public bool Passed => Failure is null && Deaths.Count == 0;

    public static ScenarioRun Execute(Type scenarioType, int strat, int seed, ScenarioRunOptions? options = null)
    {
        options ??= new ScenarioRunOptions();
        var fake = FakeGame.Install();
        Game? game = null;
        var log = TraceLog.Create(() => game?.World.Events.Elapsed ?? 0f);
        var role = options.PlayerRole ?? (PartyRole)new Rng(seed).Fork("player-seat").Next(8);
        // A real player's job always fits their seat; job-keyed actions (tank invulns) read it.
        fake.BattleCharas.Player.ClassJob = PartyPresets.Standard[(int)role].ClassJob;
        var deaths = new List<Death>();
        var aoeChecks = new List<AoeCheck>();
        var elapsed = 0f;
        string? failure = null;
        IScenario? scenario = null;

        void OnAoeEvaluated(AoeQuery query)
        {
            var now = game?.World.Events.Elapsed ?? 0f;
            aoeChecks.RemoveAll(check => check.Time < now - AoeCheckWindowSeconds);
            var positions = game is null
                ? []
                : WorldSnapshot.Members(game.World).Where(m => m.Member.IsAlive()).Select(m => (m.Role, m.Member.Position)).ToList();
            aoeChecks.Add(new AoeCheck(now, query, positions));
        }

        IEnumerable<AoeCheck> RecentAoeChecks()
        {
            var now = game?.World.Events.Elapsed ?? 0f;
            return aoeChecks.Where(check => check.Time >= now - AoeCheckWindowSeconds);
        }

        DalamudServices.Install(nameof(Plugin.Log), log);
        DalamudServices.Install(nameof(Plugin.Config), new Configuration());
        DebugBotControl.Enabled = true;
        PerRole.ForceSeats = true;
        AoeQuery.Evaluated += OnAoeEvaluated;
        game = new Game();
        DalamudServices.Install(nameof(Plugin.GameInstance), game);
        var probe = new ScenarioProbe(game, log, RecentAoeChecks);
        try
        {
            scenario = game.Scenarios.Single(s => s.GetType() == scenarioType);
            game.PartyMemberKilled += (r, cause) =>
            {
                var snapshot = WorldSnapshot.Describe(game.World, RecentAoeChecks(), game.World.Party.Get(r), includeHidden: true);
                deaths.Add(new Death(r, cause, game.World.Events.Elapsed, snapshot));
            };
            if (options.Overrides is { } setOverrides)
                setOverrides(scenario.SettingsOverrides
                             ?? throw new InvalidOperationException($"{scenarioType.Name} has no settings overrides."));
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
                if (options.StopAt is { } stopAt && game.World.Events.Elapsed >= stopAt)
                    break;
                if (options.Takeover is { } takeover && DebugBotControl.Enabled && game.World.Events.Elapsed >= takeover.At)
                    TakeOverPlayer(game.World, takeover, log);
                fake.Frame(FrameSeconds, game.Tick);
                elapsed += FrameSeconds;
                if (options.Probe is { } probeAction)
                {
                    probeAction(probe);
                    probe.EndFrame();
                }
            }
        }
        catch (Exception e)
        {
            failure = $"threw at t={game.World.Events.Elapsed:F2}: {e}";
        }

        var endTime = game.World.Events.Elapsed;
        string? artifacts = null;
        try
        {
            var run = new ScenarioRun(scenarioType, strat, seed, role, endTime, deaths, failure, log.Warnings, null);
            if ((!run.Passed && options.WriteArtifactsOnFailure) || options.AlwaysWriteArtifacts)
            {
                var final = WorldSnapshot.Describe(game.World, RecentAoeChecks());
                artifacts = ScenarioArtifacts.Write(run, log.Lines, scenario, final);
            }
        }
        finally
        {
            AoeQuery.Evaluated -= OnAoeEvaluated;
            DebugBotControl.Enabled = false;
            PerRole.ForceSeats = false;
            game.Dispose();
        }
        return new ScenarioRun(scenarioType, strat, seed, role, endTime, deaths, failure, log.Warnings, artifacts);
    }

    private static void TakeOverPlayer(SimWorld world, PlayerTakeover takeover, TraceLog log)
    {
        DebugBotControl.Enabled = false;
        if (world.Party.Player is not { } player) return;
        player.StopMoving();
        if (takeover.TeleportTo is { } to)
            player.SetPosition(new Vector3(to.X, 0f, to.Y));
        log.Add("TEST", $"player {world.Party.PlayerRole} taken over{(takeover.TeleportTo is { } p ? $", teleported to ({p.X:F1},{p.Y:F1})" : "")}");
    }

    public string ReplayTestCase => $"[TestCase(typeof(global::{ScenarioType.FullName}), {Strat}, {Seed})]";

    // Bash quoting: PowerShell 5.1 strips the inner double quotes when calling a native exe.
    public string ReplayCommand
        => "dotnet test tests/AnoMech.Tests --filter FullyQualifiedName~ScenarioCatalogTests.ReplayFromParameters -- "
           + $"'TestRunParameters.Parameter(name=\"Scenario\", value=\"{ScenarioType.FullName}\")' "
           + $"'TestRunParameters.Parameter(name=\"Strat\", value=\"{Strat}\")' "
           + $"'TestRunParameters.Parameter(name=\"Seed\", value=\"{Seed}\")'";

    public override string ToString()
    {
        var lines = new List<string> { $"seed {Seed} (player {PlayerRole}, t={Elapsed:F1}):" };
        if (Failure is not null) lines.Add($"  {Failure}");
        lines.AddRange(Deaths.Select(d => $"  {d.Role} died at t={d.Time:F2}: {d.Cause}"));
        lines.AddRange(Warnings.Distinct().Select(w => $"  warning: {w}"));
        if (ArtifactDirectory is not null) lines.Add($"  details: {ArtifactDirectory}");
        return string.Join(Environment.NewLine, lines);
    }
}

// What a probe sees each frame. Probes answer one-off questions about a run from the test side,
// instead of temporary log lines in production code; their output lands in trace.log.
internal sealed class ScenarioProbe(Game game, TraceLog log, Func<IEnumerable<AoeCheck>> recentAoeChecks)
{
    private float previousTime = float.NegativeInfinity;

    public Game Game => game;
    public SimWorld World => game.World;
    public float Time => game.World.Events.Elapsed;

    public SimCharacter? Member(PartyRole role) => World.Party.Get(role);

    // True on the one frame the scenario clock passes `time`.
    public bool Crossed(float time) => previousTime < time && Time >= time;

    public void Log(string message) => log.Add("PROBE", message);

    public void Snapshot(string label)
        => log.Add("PROBE", $"=== {label} ==={Environment.NewLine}{WorldSnapshot.Describe(World, recentAoeChecks())}=== end {label} ===");

    internal void EndFrame() => previousTime = Time;
}
