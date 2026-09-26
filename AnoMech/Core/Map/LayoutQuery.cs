using System;
using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Group;
using LuminaEObj = Lumina.Excel.Sheets.EObj;
using LuminaExportedSG = Lumina.Excel.Sheets.ExportedSG;

namespace AnoMech.Core.Map;

// Look up SharedGroup ILayoutInstances baked into the active zone's LGB layout.
// EObj scenery (arena tiles, telegraph markers, Exit portal, etc.) is loaded by
// the engine at zone-init as SharedGroupLayoutInstances under
// LayoutWorld.ActiveLayout->Layers->Instances. We never allocate these — we
// look up the engine's existing instances and drive their state. Same iteration
// pattern as DirectorFunctions.DisableSpawnAreaColliders.
internal static unsafe class LayoutQuery
{
    // Walks every SharedGroup instance in the active layout. Visitor is
    // invoked with a non-null pointer for each. Returns the visit count.
    public static int EnumerateAll(Action<nint> visitor)
    {
        var lw = LayoutWorld.Instance();
        if (lw == null || lw->ActiveLayout == null) return 0;

        int count = 0;
        foreach (var layerKv in lw->ActiveLayout->Layers)
        {
            var layer = layerKv.Item2.Value;
            if (layer == null) continue;
            foreach (var instKv in layer->Instances)
            {
                var inst = instKv.Item2.Value;
                if (inst == null) continue;
                if (inst->Id.Type != InstanceType.SharedGroup) continue;
                visitor((nint)inst);
                count++;
            }
        }
        return count;
    }

    // Pointers to every layout instance (any type, not just SharedGroup) belonging to one LGB
    // layer key, for MapController.SuppressLayer — a client-side zone load activates every
    // layer at once (there's no real duty director picking the current phase's), so competing
    // floor geometry from different phases can end up occupying the same space and z-fight.
    public static List<nint> CollectLayerInstances(ushort layerKey)
    {
        var lw = LayoutWorld.Instance();
        var result = new List<nint>();
        if (lw == null || lw->ActiveLayout == null) return result;
        foreach (var layerKv in lw->ActiveLayout->Layers)
        {
            var layer = layerKv.Item2.Value;
            if (layer == null || layerKv.Item1 != layerKey) continue;
            foreach (var instKv in layer->Instances)
            {
                var inst = instKv.Item2.Value;
                if (inst != null) result.Add((nint)inst);
            }
        }
        return result;
    }

    public static SharedGroupLayoutInstance* FindBySgbPath(string sgbPath)
    {
        if (string.IsNullOrEmpty(sgbPath)) return null;
        SharedGroupLayoutInstance* hit = null;
        EnumerateAll(p =>
        {
            if (hit != null) return;
            var sg = (SharedGroupLayoutInstance*)p;
            var path = GetSgbPath(sg);
            if (path != null && string.Equals(path, sgbPath, StringComparison.OrdinalIgnoreCase))
                hit = sg;
        });
        return hit;
    }

    public static List<nint> FindAllBySgbPath(string sgbPath)
    {
        var hits = new List<nint>();
        EnumerateAll(p =>
        {
            var path = GetSgbPath((SharedGroupLayoutInstance*)p);
            if (path != null && string.Equals(path, sgbPath, StringComparison.OrdinalIgnoreCase))
                hits.Add(p);
        });
        return hits;
    }

    public static SharedGroupLayoutInstance* FindByEObjRow(uint eObjRowId)
    {
        var path = ResolveEObjSgbPath(eObjRowId);
        return path == null ? null : FindBySgbPath(path);
    }

    // Position match is on the XZ plane (Y is height; arena scenery sits flat).
    // `pathContains` disambiguates instances that share the same EObj row but
    // appear at multiple positions (e.g. the 12 Sigma ring spokes) — pass a
    // fragment of the .sgb path to narrow.
    public static SharedGroupLayoutInstance* FindByPosition(Vector3 world, float radius, string? pathContains = null)
    {
        var r2 = radius * radius;
        SharedGroupLayoutInstance* best = null;
        var bestDist = float.MaxValue;
        EnumerateAll(p =>
        {
            var sg = (SharedGroupLayoutInstance*)p;
            var pos = sg->Transform.Translation;
            var dx = pos.X - world.X;
            var dz = pos.Z - world.Z;
            var d2 = dx * dx + dz * dz;
            if (d2 > r2) return;
            if (pathContains != null)
            {
                var path = GetSgbPath(sg);
                if (path == null || path.IndexOf(pathContains, StringComparison.OrdinalIgnoreCase) < 0) return;
            }
            if (d2 < bestDist) { bestDist = d2; best = sg; }
        });
        return best;
    }

    // Returns the .sgb path stored on the SharedGroup's ResourceHandle, or null
    // if the handle / name isn't yet populated (zone-load is async — caller
    // retries each frame as MapController does for the barrier-drop helper).
    public static string? GetSgbPath(SharedGroupLayoutInstance* sg)
    {
        if (sg == null) return null;
        var rh = sg->ResourceHandle;
        if (rh == null) return null;
        return rh->FileName.ToString();
    }

    private static string? ResolveEObjSgbPath(uint eObjRowId)
    {
        var eobjSheet = Plugin.DataManager.GetExcelSheet<LuminaEObj>();
        if (!eobjSheet.TryGetRow(eObjRowId, out var eobj)) return null;
        var sgRow = eobj.SgbPath.RowId;
        if (sgRow == 0) return null;
        var sgSheet = Plugin.DataManager.GetExcelSheet<LuminaExportedSG>();
        if (!sgSheet.TryGetRow(sgRow, out var sg)) return null;
        var path = sg.SgbPath.ToString();
        return string.IsNullOrEmpty(path) ? null : path;
    }
}
