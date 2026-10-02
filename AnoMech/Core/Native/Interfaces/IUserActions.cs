namespace AnoMech.Core.Native.Interfaces;

// The player's own presses resolved client-side (Core/UserActions), as far as a run drives it.
public interface IUserActions
{
    bool Enabled { get; }

    // A true zone entry: snapshots the real job gauge for OnSessionEnd to restore.
    void OnSessionStart();
    void OnScenarioStart();
    void OnSessionEnd();
}
