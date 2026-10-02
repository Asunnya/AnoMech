namespace AnoMech.Core.Native.Interfaces;

public interface IGameMessages
{
    // A system message in chat, verbatim.
    void PrintSystemMessage(string text);

    // The large red text at the top of the screen.
    void ShowErrorText(string text);
}
