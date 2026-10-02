using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Tests;

// The module off, as when the user hasn't enabled it.
internal sealed class FakeUserActions : IUserActions
{
    public bool Enabled => false;
    public void OnSessionStart() { }
    public void OnScenarioStart() { }
    public void OnSessionEnd() { }
}
