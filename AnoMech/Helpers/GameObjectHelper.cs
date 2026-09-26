using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System;

namespace AnoMech.Helpers;

public static unsafe class GameObjectHelper
{
    // UTF-8 into the 64-byte Name field (63 + terminator), trimmed on a character boundary so a
    // peer's display name with non-ASCII letters is never cut mid-sequence.
    public static void WriteName(GameObject* obj, string name)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(name);
        while (bytes.Length > 63)
        {
            name = name[..^1];
            bytes = System.Text.Encoding.UTF8.GetBytes(name);
        }
        for (int i = 0; i < bytes.Length; i++) obj->Name[i] = bytes[i];
        obj->Name[bytes.Length] = 0;
    }
}
