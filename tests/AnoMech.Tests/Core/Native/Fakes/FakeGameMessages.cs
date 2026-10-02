using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Tests;

internal sealed class FakeGameMessages : IGameMessages
{
    public void PrintSystemMessage(string text) { }
    public void ShowErrorText(string text) { }
}
