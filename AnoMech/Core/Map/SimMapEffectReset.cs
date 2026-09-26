using System.Collections.Generic;
using AnoMech.Core.SimObjects;

namespace AnoMech.Core.Map;

// Puts touched map effects away on world teardown, so props don't outlive a stopped run.
internal sealed class SimMapEffectReset(MapController map) : ISimObject
{
    private readonly Dictionary<byte, uint> offFlags = [];

    public bool IsActive => true;

    public void Record(byte index, uint flags) => offFlags[index] = flags;

    public void Tick(float deltaSeconds) { }

    public void Despawn()
    {
        foreach (var (index, flags) in offFlags) map.AddEffect(flags, index);
        offFlags.Clear();
    }
}
