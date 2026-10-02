using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Tests;

// No game files in tests: rows are whatever a test puts here.
internal sealed class FakeGameData : IGameData
{
    public Dictionary<uint, ActionRow> Actions { get; } = new();
    public Dictionary<uint, KnockbackRow> Knockbacks { get; } = new();
    public Dictionary<uint, ClassJobRow> ClassJobs { get; } = new();
    public Dictionary<ushort, string> StatusNames { get; } = new();
    public Dictionary<uint, string> BNpcNames { get; } = new();

    public ActionRow? Action(uint actionId) => Actions.GetValueOrDefault(actionId);
    public KnockbackRow? Knockback(uint knockbackId) => Knockbacks.GetValueOrDefault(knockbackId);
    public ClassJobRow? ClassJob(uint classJobId) => ClassJobs.GetValueOrDefault(classJobId);
    public string? StatusName(ushort statusId) => StatusNames.GetValueOrDefault(statusId);
    public string? BNpcName(uint nameId) => BNpcNames.GetValueOrDefault(nameId);
    public bool FileExists(string path) => true;
}
