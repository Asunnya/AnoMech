using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AnoMech.Tests;

// Simulation code must reach the game only through Core/Native/Interfaces, so scenarios can run
// headless against fakes: a stray native call in a test kills the whole test process.
public sealed class NativeBoundaryTests
{
    // Never run headless, so they may use Core/Native/Implementations directly.
    private static readonly string[] NativeConsumers =
    [
        "Core/Native/Implementations/",
        "Core/UserActions/",
        "Multiplayer/",
        "Windows/",
        "Plugin.cs",
        "Configuration.cs",
    ];

    // Dalamud services that aren't the game; tests install null stand-ins for them.
    private static readonly HashSet<string> AllowedPluginServices = ["Log", "PluginInterface"];

    // FFXIVClientStructs namespaces whose plain value types (GameObjectId, ActionType,
    // CharacterModes, CustomizeData, EventId) the sim API keeps. Their native members all take or
    // return pointers, which the unsafe rule already rejects; every other namespace is off limits.
    private static readonly HashSet<string> AllowedClientStructsNamespaces =
    [
        "FFXIVClientStructs.FFXIV.Client.Game",
        "FFXIVClientStructs.FFXIV.Client.Game.Character",
        "FFXIVClientStructs.FFXIV.Client.Game.Event",
        "FFXIVClientStructs.FFXIV.Client.Game.Object",
    ];

    [Test]
    public void SimulationCodeStaysBehindTheBoundary()
    {
        var violations = SimulationFiles()
            .Select(f => (File: f, Found: Violations(f)))
            .Where(v => v.Found.Count > 0)
            .Select(v => $"{v.File}: {string.Join("; ", v.Found)}")
            .ToList();

        // Joined into the message: NUnit truncates a collection after ten items.
        Assert.That(violations, Is.Empty, "Simulation code touches the game outside Core/Native/Interfaces:\n" + string.Join("\n", violations));
    }

    private static readonly string SourceRoot = Path.Combine(RepoRoot(), "AnoMech");

    private static IEnumerable<string> SimulationFiles()
        => Directory.EnumerateFiles(SourceRoot, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(SourceRoot, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("obj/") && !f.StartsWith("bin/"))
            .Where(f => !NativeConsumers.Any(f.StartsWith))
            .Order();

    private static readonly HashSet<string> GameServices = PluginGameServices();

    private static readonly HashSet<string> ClientStructsNamespaceNames = typeof(GameObjectId).Assembly.GetTypes()
        .Select(t => t.Namespace)
        .OfType<string>()
        .ToHashSet();

    private static List<string> Violations(string relativePath)
    {
        var root = Parse(Path.Combine(SourceRoot, relativePath));
        var found = new List<string>();
        if (root.DescendantTokens().Any(t => t.IsKind(SyntaxKind.UnsafeKeyword)))
            found.Add("unsafe code");
        if (root.DescendantTokens().Any(t => t.IsKind(SyntaxKind.IdentifierToken) && t.ValueText == "Implementations"))
            found.Add("names Core/Native/Implementations");
        var services = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(m => IsPlugin(m.Expression) && GameServices.Contains(m.Name.Identifier.ValueText))
            .Select(m => $"Plugin.{m.Name.Identifier.ValueText}")
            .Distinct().Order().ToList();
        if (services.Count > 0)
            found.Add(string.Join(", ", services));
        var clientStructs = ClientStructsReferences(root)
            .Select(NamespaceOf)
            .Where(ns => !AllowedClientStructsNamespaces.Contains(ns))
            .Distinct().Order().ToList();
        if (clientStructs.Count > 0)
            found.Add(string.Join(", ", clientStructs));
        return found;
    }

    private static bool IsPlugin(ExpressionSyntax e) => e switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText == "Plugin",
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText == "Plugin",
        _ => false,
    };

    // Using directives and fully qualified names that start at FFXIVClientStructs.
    private static IEnumerable<string> ClientStructsReferences(SyntaxNode root)
        => root.DescendantNodes()
            .Where(n => n is QualifiedNameSyntax or MemberAccessExpressionSyntax
                        && n.Parent is not (QualifiedNameSyntax or MemberAccessExpressionSyntax))
            .Select(n => n.ToString())
            .Where(text => text.StartsWith("FFXIVClientStructs."));

    // The longest prefix that is a namespace of the FFXIVClientStructs assembly.
    private static string NamespaceOf(string qualifiedName)
    {
        var name = qualifiedName;
        while (!ClientStructsNamespaceNames.Contains(name) && name.Contains('.'))
            name = name[..name.LastIndexOf('.')];
        return name;
    }

    // Plugin's Dalamud game services, plus its statics typed as a native implementation
    // (e.g. PlayerInputHooks).
    private static HashSet<string> PluginGameServices()
    {
        var implementationTypes = Directory.EnumerateFiles(Path.Combine(SourceRoot, "Core/Native/Implementations"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => Parse(f).DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            .Select(t => t.Identifier.ValueText)
            .ToHashSet();
        return Parse(Path.Combine(SourceRoot, "Plugin.cs")).DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Where(p => p.Modifiers.Any(SyntaxKind.StaticKeyword))
            .Where(p => p.AttributeLists.SelectMany(a => a.Attributes).Any(a => a.Name.ToString() == "PluginService")
                        || implementationTypes.Contains(p.Type.ToString()))
            .Select(p => p.Identifier.ValueText)
            .Where(name => !AllowedPluginServices.Contains(name))
            .ToHashSet();
    }

    // DEBUG on, so code under #if DEBUG is checked rather than skipped as disabled text.
    private static readonly CSharpParseOptions ParseOptions = new(preprocessorSymbols: ["DEBUG"]);

    private static SyntaxNode Parse(string path)
        => CSharpSyntaxTree.ParseText(File.ReadAllText(path), ParseOptions, path).GetRoot();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AnoMech.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("AnoMech.sln not found above the test directory.");
    }
}
