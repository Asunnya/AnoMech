using System.Numerics;

namespace AnoMech.Core.Native.Interfaces;

// The zone's LGB layout instances.
public interface ILayoutFunctions
{
    // A one-shot SetActive(false) is reconciled back by the engine within a frame or two, so the
    // layer's instances are held inactive by ReassertSuppressedLayers each tick until cleared.
    // Returns how many instances the layer has.
    int SuppressLayer(ushort layerKey);
    void ReassertSuppressedLayers();
    void ClearSuppressedLayers();

    // Drops the colliders of SharedGroups near the world-space center; returns how many it found
    // (0 while the zone is still streaming in).
    int DisableSpawnAreaColliders(Vector3 center, float radius);

    // Every zone SharedGroup loaded from that SGB plays the timeline, where it has one at that index.
    void PlaySharedGroupTimeline(string sgbPath, uint index);

    string DescribeActiveLayers();
    string DescribeLiveEffects();
}
