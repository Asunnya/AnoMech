using System.Reflection;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AnoMech.Tests;

// Code under test logs through Plugin.Log and saves through Plugin.PluginInterface, which only
// Dalamud fills in. Null-object stand-ins let that code run outside the game.
[SetUpFixture]
public sealed class DalamudServices
{
    [OneTimeSetUp]
    public void InstallNullServices()
    {
        Install(nameof(Plugin.Log), DispatchProxy.Create<IPluginLog, NullService>());
        Install(nameof(Plugin.PluginInterface), DispatchProxy.Create<IDalamudPluginInterface, NullService>());
    }

    private static void Install(string property, object service)
        => typeof(Plugin).GetProperty(property, BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, service);

    public class NullService : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method is { ReturnType.IsValueType: true } && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
    }
}
