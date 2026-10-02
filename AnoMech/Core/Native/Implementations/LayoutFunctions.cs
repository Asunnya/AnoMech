using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Native.Implementations.Interop;
using AnoMech.Core.Native.Interfaces;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;

namespace AnoMech.Core.Native.Implementations;

// Some zones' client-side load activates every layer at once (there is no real duty director
// selecting the current phase's), so geometry from different phases renders in the same space
// and z-fights; the losing layers are held inactive.
internal sealed unsafe class LayoutFunctions : ILayoutFunctions
{
    private readonly List<nint> suppressedLayerInstances = new();

    public int SuppressLayer(ushort layerKey)
    {
        var instances = LayoutQuery.CollectLayerInstances(layerKey);
        suppressedLayerInstances.AddRange(instances);
        return instances.Count;
    }

    public void ReassertSuppressedLayers()
    {
        foreach (var ptr in suppressedLayerInstances)
            ((ILayoutInstance*)ptr)->SetActive(false);
    }

    public void ClearSuppressedLayers() => suppressedLayerInstances.Clear();

    public int DisableSpawnAreaColliders(Vector3 center, float radius) => DirectorFunctions.DisableSpawnAreaColliders(center, radius);

    public string DescribeActiveLayers() => LayoutQuery.DescribeActiveLayers();

    public string DescribeLiveEffects() => LayoutQuery.DescribeLiveEffects();
}
