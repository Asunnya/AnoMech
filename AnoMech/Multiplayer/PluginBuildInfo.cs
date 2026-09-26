using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using AnoMech.Core;

namespace AnoMech.Multiplayer;

// Checksum hashes the DLL itself: a local dev build shares its version number with the
// release it was built from, and a build mismatch means differing scenario/protocol logic.
internal static class PluginBuildInfo
{
    public static string Version { get; } = ComputeVersion();
    public static string Checksum { get; } = ComputeChecksum();
    public static string ShortChecksum { get; } = Checksum.Length >= 6 ? Checksum[..6] : Checksum;

    private static string ComputeVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "unknown" : $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
    }

    private static string ComputeChecksum()
    {
        try
        {
            // Assembly.Location is empty under Dalamud (plugins load from bytes);
            // AssemblyLocation is the real path.
            var path = Plugin.PluginInterface.AssemblyLocation.FullName;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "unknown";
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream))[..16];
        }
        catch (Exception e)
        {
            DiagnosticLog.Warn($"[Multiplayer] Failed to checksum plugin DLL: {e.Message}");
            return "unknown";
        }
    }
}
