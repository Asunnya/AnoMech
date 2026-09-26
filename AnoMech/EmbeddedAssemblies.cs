using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace AnoMech;

// Dependencies ship inside AnoMech.dll and are loaded into the plugin's own load context before
// any code runs; a load context finds an assembly it already holds before asking Dalamud's
// resolver, which only looks in the plugin folder. No type may implement or derive from an
// embedded assembly's types: Dalamud's GetTypes() loads every type before this runs.
internal static class EmbeddedAssemblies
{
    private const string Prefix = "AnoMech.Embedded.";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Load()
    {
        var self = typeof(EmbeddedAssemblies).Assembly;
        var context = AssemblyLoadContext.GetLoadContext(self);
        if (context == null) return;
        foreach (var name in self.GetManifestResourceNames())
        {
            if (!name.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            using var stream = self.GetManifestResourceStream(name);
            if (stream == null) continue;
            try
            {
                context.LoadFromStream(stream);
            }
            catch (FileLoadException)
            {
                // Already loaded into this context.
            }
        }
    }
}
