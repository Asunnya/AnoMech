namespace AnoMech.Core.Native.Interfaces;

// Zone objects (the duty Exit portal, scenery) hidden for a scenario.
public interface IHiddenObjects
{
    // Hides the first object with this BaseId; returns its object index, or null when the zone
    // has none.
    ushort? Hide(uint baseId);

    // No-op when the index now holds a different object.
    void Restore(ushort objectIndex, uint baseId);
}
