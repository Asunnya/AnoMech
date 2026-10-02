using System;
using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Group;
using LuminaEObj = Lumina.Excel.Sheets.EObj;
using LuminaExportedSG = Lumina.Excel.Sheets.ExportedSG;

namespace AnoMech.Core.Native.Implementations.Interop;

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

    public static string DescribeLiveEffects()
    {
        var lw = LayoutWorld.Instance();
        if (lw == null || lw->ActiveLayout == null) return "no active layout";
        var counts = new Dictionary<string, (int Count, SortedSet<string> Where)>();
        void Add(string effect, string where)
        {
            if (!counts.TryGetValue(effect, out var entry)) entry = (0, new SortedSet<string>());
            entry.Where.Add(where);
            counts[effect] = (entry.Count + 1, entry.Where);
        }
        foreach (var layerKv in lw->ActiveLayout->Layers)
        {
            var layer = layerKv.Item2.Value;
            if (layer == null) continue;
            foreach (var instKv in layer->Instances)
            {
                var inst = instKv.Item2.Value;
                if (inst == null) continue;
                var found = new List<string>();
                LayoutInstanceDiagnostics.CollectLiveEffects(inst, 3, found);
                foreach (var effect in found) Add(effect, $"layer 0x{layerKv.Item1:X}");
            }
        }
        var eobjs = EventObjectManager.Instance();
        if (eobjs != null)
        {
            for (var i = 0; i < 40; i++)
            {
                var go = eobjs->EventObjects[i].Value;
                if (go == null || go->SharedGroupLayoutInstance == null) continue;
                var found = new List<string>();
                LayoutInstanceDiagnostics.CollectLiveEffects((ILayoutInstance*)go->SharedGroupLayoutInstance, 3, found);
                foreach (var effect in found) Add(effect, $"EObj 0x{go->BaseId:X}");
            }
        }
        var parts = new List<string>();
        foreach (var (effect, entry) in counts) parts.Add($"{entry.Count}x {effect} in {string.Join(",", entry.Where)}");
        parts.Sort(StringComparer.Ordinal);
        return $"{parts.Count} distinct -- " + string.Join(" | ", parts);
    }

    // Which layers the engine actually brought up has no other signal.
    public static string DescribeActiveLayers()
    {
        var lw = LayoutWorld.Instance();
        if (lw == null || lw->ActiveLayout == null) return "no active layout";
        var lm = lw->ActiveLayout;
        // Layers are filtered by (TerritoryTypeId, CfcId), and a client-side load never sets CfcId.
        var filters = new List<string>();
        foreach (var kv in lm->Filters)
        {
            var filter = kv.Item2.Value;
            if (filter != null) filters.Add($"{filter->Key}=>terr{filter->TerritoryTypeId}/cfc{filter->CfcId}");
        }
        var header = $"init={lm->InitState} terr={lm->TerritoryTypeId} cfc={lm->CfcId} filterKey={lm->LayerFilterKey}"
            + $" filters[{filters.Count}]={string.Join(",", filters)} -- ";
        var parts = new List<string>();
        foreach (var layerKv in lw->ActiveLayout->Layers)
        {
            var layer = layerKv.Item2.Value;
            if (layer == null) continue;
            int total = 0, active = 0;
            foreach (var instKv in layer->Instances)
            {
                var inst = instKv.Item2.Value;
                if (inst == null) continue;
                total++;
                if (inst->IsActive) active++;
            }
            parts.Add($"0x{layerKv.Item1:X}:{active}/{total}");
        }
        parts.Sort(StringComparer.Ordinal);
        return header + $"{parts.Count} layers -- " + string.Join(" ", parts);
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
