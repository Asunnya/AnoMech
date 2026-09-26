// Native game types are unavailable in the transport-only test process. Shapes match the
// plugin's, so messages serialize here as they do in game.
namespace AnoMech.Core { internal static class DiagnosticLog { public static void Info(string s) { } public static void Warn(string s) { } public static void Debug(string s) { } } }
namespace AnoMech.Core.SimObjects { public enum EnemyListMode { Always, OnlyWhenVisible, Never, Manual } public enum TimelineHoldKind { None, Loop, Base } public enum PropBeatMode { ActorControl, PlayAnimation, SetSharedTimelineState } }
namespace AnoMech.Scenarios.Top { public sealed record OmegaAttack(byte AttributeFlags, uint ActionId); }
namespace AnoMech.Scenarios.Umad.P1TeleTrouncing { public enum TelePortentDirection { Up, Down, Left, Right } }
namespace AnoMech.Scenarios.Umad.P2Forsaken { public sealed record EndAttack(uint CastBarAction, uint KefkaResolveAction, uint CloneResolveAction, uint AllThingsEnding, float RotationOverride); }
namespace AnoMech.Scenarios.Umad.P3BlackHole { public enum ThunderIIIAssignment { MtInvulnsBoth, OtInvulnsBoth, ShareMtFirst, ShareOtFirst } }
namespace Dalamud.Configuration { public interface IPluginConfiguration { int Version { get; set; } } }
namespace AnoMech { internal static class Plugin { public static readonly ConfigSaver PluginInterface = new(); } internal sealed class ConfigSaver { public void SavePluginConfig(object value) { } } }
