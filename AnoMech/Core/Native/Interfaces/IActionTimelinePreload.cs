using System.Collections.Generic;

namespace AnoMech.Core.Native.Interfaces;

public interface IActionTimelinePreload
{
    // Loads ActionTimeline resources ahead of first use: one played on a fresh actor before its
    // resource is resident can drop.
    void Preload(IEnumerable<(ushort Id, string Key)> rows, string tag);
}
