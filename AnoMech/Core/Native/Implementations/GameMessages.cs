using AnoMech.Core.Native.Interfaces;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace AnoMech.Core.Native.Implementations;

internal sealed unsafe class GameMessages : IGameMessages
{
    public void PrintSystemMessage(string text) => Plugin.ChatGui.Print(new XivChatEntry
    {
        Type = XivChatType.SystemMessage,
        Message = new SeStringBuilder().AddText(text).Build(),
    });

    public void ShowErrorText(string text)
    {
        var ui = UIModule.Instance();
        if (ui != null) ui->ShowErrorText(text, true);
    }
}
