using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using AnoMech.Scenarios;

namespace AnoMech.Tests;

// The per-run folder a failing (or replayed) run leaves behind:
// tests/AnoMech.Tests/TestResults/scenario-runs/<Scenario>-s<strat>-<seed>/
// The previous run of the same seed moves to <folder>.prev, so two runs can be diffed.
internal static class ScenarioArtifacts
{
    public static string Write(ScenarioRun run, IReadOnlyList<string> trace, IScenario? scenario, string finalSnapshot)
    {
        var directory = Path.Combine(ProjectDirectory(), "TestResults", "scenario-runs", $"{run.ScenarioType.Name}-s{run.Strat}-{run.Seed}");
        var previous = directory + ".prev";
        if (Directory.Exists(previous))
            Directory.Delete(previous, recursive: true);
        if (Directory.Exists(directory))
            Directory.Move(directory, previous);
        Directory.CreateDirectory(directory);

        var files = new List<string> { "summary.txt", "trace.log", "state.txt", "final.txt" };
        for (var i = 0; i < run.Deaths.Count; i++)
        {
            var death = run.Deaths[i];
            var name = $"death-{i + 1}-{death.Role}.txt";
            files.Add(name);
            File.WriteAllText(Path.Combine(directory, name),
                $"{death.Role} died at t={death.Time:F2}: {death.Cause}{Environment.NewLine}{Environment.NewLine}{death.Snapshot}");
        }

        File.WriteAllLines(Path.Combine(directory, "trace.log"), trace);
        File.WriteAllText(Path.Combine(directory, "state.txt"), DescribeState(scenario));
        File.WriteAllText(Path.Combine(directory, "final.txt"), $"World at t={run.Elapsed:F2}, when the run ended:{Environment.NewLine}{Environment.NewLine}{finalSnapshot}");
        File.WriteAllText(Path.Combine(directory, "summary.txt"), string.Join(Environment.NewLine,
            $"{run.ScenarioType.FullName}, strat {run.Strat}: {(run.Passed ? "passed" : "FAILED")}",
            run.ToString(),
            "",
            "Replay (paste into ScenarioCatalogTests.Replay):",
            "  " + run.ReplayTestCase,
            "Replay from the command line:",
            "  " + run.ReplayCommand,
            "",
            "Files: " + string.Join(", ", files),
            ""));
        return directory;
    }

    private static string ProjectDirectory()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AnoMech.Tests.csproj")))
                return dir.FullName;
        return TestContext.CurrentContext.TestDirectory;
    }

    // The scenario's per-run state object(s), member by member, as they stood when the run ended.
    private static string DescribeState(IScenario? scenario)
    {
        if (scenario is null) return "No scenario.";
        var text = new StringBuilder();
        var states = scenario.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.FieldType.Name.EndsWith("State", StringComparison.Ordinal))
            .Select(f => f.GetValue(scenario))
            .OfType<object>()
            .Distinct(ReferenceEqualityComparer.Instance);
        foreach (var state in states)
        {
            text.AppendLine($"{state.GetType().Name}:");
            foreach (var (name, value) in Members(state))
                text.AppendLine($"  {name} = {Format(value)}");
        }
        return text.Length == 0 ? $"{scenario.GetType().Name} has no *State field." : text.ToString();
    }

    private static IEnumerable<(string Name, object? Value)> Members(object target)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var field in target.GetType().GetFields(flags).Where(f => !f.Name.Contains('<')))
            yield return (field.Name, Read(() => field.GetValue(target)));
        foreach (var property in target.GetType().GetProperties(flags).Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod is not null))
            yield return (property.Name, Read(() => property.GetValue(target)));
    }

    private static object? Read(Func<object?> read)
    {
        try
        {
            return read();
        }
        catch (Exception e)
        {
            return $"<threw {(e is TargetInvocationException { InnerException: { } inner } ? inner.GetType().Name : e.GetType().Name)}>";
        }
    }

    private static string Format(object? value, int depth = 0) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable items when depth < 2 => $"[{string.Join(", ", items.Cast<object?>().Take(32).Select(i => Format(i, depth + 1)))}]",
        _ when value.GetType().GetMethod(nameof(ToString), Type.EmptyTypes)?.DeclaringType != typeof(object) => value.ToString() ?? "",
        _ when depth < 1 => $"{value.GetType().Name} {{ {string.Join(", ", PublicProperties(value).Select(p => $"{p.Name} = {Format(p.Value, depth + 1)}"))} }}",
        _ => $"<{value.GetType().Name}>",
    };

    private static IEnumerable<(string Name, object? Value)> PublicProperties(object target)
        => target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod is not null)
            .Select(p => (p.Name, Read(() => p.GetValue(target))));
}
