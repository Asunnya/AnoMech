using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Group;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Node;

namespace AnoMech.Core.Native;

// Whether a native SharedGroup (MapEffects' arena pieces, SimEventObject's EObj scenery) has
// actually finished streaming in on this client, and native activation of it.
internal static unsafe class LayoutInstanceDiagnostics
{
    // HavePrimary/IsPrimaryLoaded/IsPrimaryReady only say the object loaded; IsActive/
    // WantToBeActive are the activation state, and GetGraphics() null despite IsPrimaryReady
    // means the renderable scene object was never created.
    public static string Describe(uint layoutId)
    {
        var world = LayoutWorld.Instance();
        if (world == null) return "LayoutWorld null";
        var instance = world->GetLayoutInstance(InstanceType.SharedGroup, layoutId);
        if (instance == null) return "GetLayoutInstance returned null (no such SharedGroup instance on this client)";
        return Describe((SharedGroupLayoutInstance*)instance);
    }

    // For an instance already held by pointer (an EObj's own actor+0x108, which has no layout id).
    public static string Describe(SharedGroupLayoutInstance* sg)
    {
        if (sg == null) return "SharedGroupLayoutInstance null (SG context not attached to the actor yet)";
        var instance = (ILayoutInstance*)sg;
        var layer = instance->Layer;
        var layerDesc = layer == null ? "Layer=null" : $"Layer=0x{layer->LayerGroupId:X}(flags=0x{layer->Flags:X})";
        var idDesc = $"Id={instance->Id.Type}/{instance->Id.LayerKey:X}:{instance->Id.InstanceKey:X} SubId=0x{instance->SubId:X} Flags1-3=0x{instance->Flags1:X}/0x{instance->Flags2:X}/0x{instance->Flags3:X}";
        var graphics = instance->GetGraphics();
        var graphics2 = instance->GetGraphics2();
        var timelinePlaying = sg->IsTimelinePlaying(sg->PlayingTimelineIndex);
        // The parent's Graphics can read null while it renders fine: the mesh usually belongs to
        // child instances (a prefab's BgParts), so children are listed too, capped at 16 (the
        // engine's FixedSizeArray16) and one level deep. A timeline-driven SGB (the statue
        // props) with children active but nothing visible points at the timelines.
        var timelineCount = sg->TimeLineContainer.Instances.Count;
        var playing = new List<int>();
        for (var i = 0; i < 16; i++)
            if (sg->IsTimelineIndexValid((uint)i) && sg->IsTimelinePlaying((uint)i)) playing.Add(i);
        var timelineDesc = $"Timelines={timelineCount} playing=[{string.Join(",", playing)}]";
        var childCount = sg->Instances.Instances.Count;
        var childSummaries = new List<string>();
        for (var i = 0; i < childCount && i < 16; i++)
        {
            var child = (ChildNodeInstance*)sg->Instances.Instances[i].Value;
            var childInstance = child != null ? child->Instance : null;
            childSummaries.Add($"[{i}]={FormatChild(childInstance, depth: 1)}");
        }
        return $"{layerDesc} {idDesc} HavePrimary={instance->HavePrimary()} IsPrimaryLoaded={instance->IsPrimaryLoaded()} IsPrimaryReady={instance->IsPrimaryReady()} "
             + $"IsActive={instance->IsActive} WantToBeActive={instance->WantToBeActive()} Graphics=0x{(nint)graphics:X} Graphics2=0x{(nint)graphics2:X} "
             + $"TimelineObject=0x{(nint)sg->TimelineObject:X} PlayingTimelineIndex=0x{sg->PlayingTimelineIndex:X} IsTimelinePlaying={timelinePlaying} {timelineDesc} "
             + $"PrefabFlags1=0x{sg->PrefabFlags1:X} PrefabFlags2=0x{sg->PrefabFlags2:X} ChildCount={childCount} Children=[{string.Join("; ", childSummaries)}]";
    }

    // Sets a SharedGroup and, optionally, every child (BgPart, Vfx and Sound) to `active`;
    // ProcessMapEffect's hide flag only blanks the BgParts. False until the SGB has streamed
    // in. Nothing persists: leaving the sim reloads the territory.
    public static bool SetSharedGroupActive(uint layoutId, bool active, bool recurseChildren)
    {
        var world = LayoutWorld.Instance();
        if (world == null) return false;
        var instance = world->GetLayoutInstance(InstanceType.SharedGroup, layoutId);
        if (instance == null) return false;
        SetActiveRecursive(instance, active, recurseChildren ? 3 : 0);
        return true;
    }

    // IsActive of a SharedGroup and its children in SetActiveRecursive's order: what a
    // recursive SetSharedGroupActive overwrites, since the children don't all share one state.
    public static bool[]? CaptureActiveStates(uint layoutId)
    {
        var world = LayoutWorld.Instance();
        if (world == null) return null;
        var instance = world->GetLayoutInstance(InstanceType.SharedGroup, layoutId);
        if (instance == null) return null;
        var states = new List<bool>();
        CaptureRecursive(instance, 3, states);
        return states.ToArray();
    }

    // Writes CaptureActiveStates' result back. A tree that changed shape since only gets its
    // SharedGroup re-enabled, and returns false.
    public static bool RestoreActiveStates(uint layoutId, bool[] states)
    {
        var world = LayoutWorld.Instance();
        if (world == null) return false;
        var instance = world->GetLayoutInstance(InstanceType.SharedGroup, layoutId);
        if (instance == null) return false;
        var current = new List<bool>(states.Length);
        CaptureRecursive(instance, 3, current);
        if (current.Count != states.Length)
        {
            instance->SetActive(true);
            return false;
        }
        var next = 0;
        RestoreRecursive(instance, 3, states, ref next);
        return true;
    }

    private static void CaptureRecursive(ILayoutInstance* inst, int depth, List<bool> states)
    {
        if (inst == null) return;
        states.Add(inst->IsActive);
        if (depth <= 0 || inst->Id.Type != InstanceType.SharedGroup) return;
        var sg = (SharedGroupLayoutInstance*)inst;
        var count = sg->Instances.Instances.Count;
        for (var i = 0; i < count && i < 16; i++)
        {
            var child = (ChildNodeInstance*)sg->Instances.Instances[i].Value;
            if (child != null && child->Instance != null)
                CaptureRecursive(child->Instance, depth - 1, states);
        }
    }

    private static void RestoreRecursive(ILayoutInstance* inst, int depth, bool[] states, ref int next)
    {
        if (inst == null) return;
        inst->SetActive(states[next++]);
        if (depth <= 0 || inst->Id.Type != InstanceType.SharedGroup) return;
        var sg = (SharedGroupLayoutInstance*)inst;
        var count = sg->Instances.Instances.Count;
        for (var i = 0; i < count && i < 16; i++)
        {
            var child = (ChildNodeInstance*)sg->Instances.Instances[i].Value;
            if (child != null && child->Instance != null)
                RestoreRecursive(child->Instance, depth - 1, states, ref next);
        }
    }

    // True if either table the engine keeps for a spawn packet's LayoutId has an instance --
    // EObj spawn packets refer to EventObject placements, MapEffect slots to SharedGroups.
    public static bool Exists(uint layoutId)
    {
        var world = LayoutWorld.Instance();
        if (world == null) return false;
        return world->GetLayoutInstance(InstanceType.SharedGroup, layoutId) != null
            || world->GetLayoutInstance(InstanceType.EventObject, layoutId) != null;
    }

    // Debug aid for EObj-created for_bg SharedGroups the engine leaves at WantToBeActive=True/
    // IsActive=False forever. Returns true if it had to act.
    public static bool ForceActive(SharedGroupLayoutInstance* sg)
    {
        if (sg == null || ((ILayoutInstance*)sg)->IsActive) return false;
        SetActiveRecursive((ILayoutInstance*)sg, true, 3);
        return true;
    }

    public static string DescribeLive(uint layoutId)
    {
        var world = LayoutWorld.Instance();
        if (world == null || layoutId == 0) return "no SharedGroup";
        var instance = world->GetLayoutInstance(InstanceType.SharedGroup, layoutId);
        if (instance == null) return "SharedGroup not loaded";
        var live = new List<string>();
        CollectLive(instance, 3, live);
        return $"SG active={instance->IsActive} \"{PrimaryPath(instance)}\" live[{live.Count}]=[{string.Join("; ", live)}]";
    }

    public static void CollectLiveEffects(ILayoutInstance* inst, int depth, List<string> into)
    {
        if (inst == null || !inst->IsActive) return;
        if (inst->Id.Type == InstanceType.Vfx) { into.Add(DescribeVfx(inst)); return; }
        if (inst->Id.Type == InstanceType.Light) { into.Add(DescribeLight(inst)); return; }
        if (depth <= 0 || inst->Id.Type != InstanceType.SharedGroup) return;
        var sg = (SharedGroupLayoutInstance*)inst;
        var count = sg->Instances.Instances.Count;
        for (var i = 0; i < count && i < 16; i++)
        {
            var child = (ChildNodeInstance*)sg->Instances.Instances[i].Value;
            if (child != null) CollectLiveEffects(child->Instance, depth - 1, into);
        }
    }

    private static void CollectLive(ILayoutInstance* sgInstance, int depth, List<string> into)
    {
        var sg = (SharedGroupLayoutInstance*)sgInstance;
        var count = sg->Instances.Instances.Count;
        for (var i = 0; i < count && i < 16; i++)
        {
            var child = (ChildNodeInstance*)sg->Instances.Instances[i].Value;
            var inst = child != null ? child->Instance : null;
            if (inst == null || !inst->IsActive) continue;
            if (inst->Id.Type == InstanceType.SharedGroup && depth > 0) { CollectLive(inst, depth - 1, into); continue; }
            into.Add(inst->Id.Type switch
            {
                InstanceType.Vfx => DescribeVfx(inst),
                InstanceType.Light => DescribeLight(inst),
                _ => $"{inst->Id.Type} \"{PrimaryPath(inst)}\"",
            });
        }
    }

    private static string DescribeVfx(ILayoutInstance* inst)
    {
        var vfx = (FFXIVClientStructs.FFXIV.Client.LayoutEngine.Layer.VfxLayoutInstance*)inst;
        var c = vfx->Color;
        return $"Vfx \"{PrimaryPath(inst)}\" rgba=({c.R},{c.G},{c.B},{c.A}) drawn={vfx->GraphicsObject != null}";
    }

    private static string DescribeLight(ILayoutInstance* inst)
    {
        var light = (FFXIVClientStructs.FFXIV.Client.LayoutEngine.Layer.LightLayoutInstance*)inst;
        var c = light->Color;
        return $"Light {light->LightType} rgb=({c.X:F2},{c.Y:F2},{c.Z:F2}) w={c.W:F2} drawn={light->GraphicsObject != null}";
    }

    private static string PrimaryPath(ILayoutInstance* inst)
    {
        var path = inst->GetPrimaryPath();
        return path.HasValue ? path.ToString() : "";
    }

    // Children too: a Vfx child keeps rendering with its SharedGroup inactive.
    public static bool Deactivate(SharedGroupLayoutInstance* sg)
    {
        if (sg == null) return false;
        var wasOn = ((ILayoutInstance*)sg)->IsActive;
        var count = sg->Instances.Instances.Count;
        for (var i = 0; i < count && i < 16; i++)
        {
            var child = (ChildNodeInstance*)sg->Instances.Instances[i].Value;
            if (child != null && child->Instance != null && child->Instance->IsActive) wasOn = true;
        }
        SetActiveRecursive((ILayoutInstance*)sg, false, 3);
        return wasOn;
    }

    private static void SetActiveRecursive(ILayoutInstance* inst, bool active, int depth)
    {
        if (inst == null) return;
        inst->SetActive(active);
        if (depth <= 0 || inst->Id.Type != InstanceType.SharedGroup) return;
        var sg = (SharedGroupLayoutInstance*)inst;
        var count = sg->Instances.Instances.Count;
        for (var i = 0; i < count && i < 16; i++)
        {
            var child = (ChildNodeInstance*)sg->Instances.Instances[i].Value;
            if (child != null && child->Instance != null)
                SetActiveRecursive(child->Instance, active, depth - 1);
        }
    }

    // For sequencing beats one timeline at a time.
    public static bool IsAnyTimelinePlaying(SharedGroupLayoutInstance* sg)
    {
        if (sg == null) return false;
        for (var i = 0; i < 16; i++)
            if (sg->IsTimelineIndexValid((uint)i) && sg->IsTimelinePlaying((uint)i)) return true;
        return false;
    }

    // Deactivates only the Sound children. An SGB's timeline re-activates them as it advances,
    // so this runs every frame; the native call is skipped unless one is active again.
    public static bool SilenceSounds(SharedGroupLayoutInstance* sg)
        => sg != null && SilenceSoundsRecursive(sg, depth: 3);

    private static bool SilenceSoundsRecursive(SharedGroupLayoutInstance* sg, int depth)
    {
        var changed = false;
        var count = sg->Instances.Instances.Count;
        for (var i = 0; i < count && i < 16; i++)
        {
            var child = (ChildNodeInstance*)sg->Instances.Instances[i].Value;
            var inst = child != null ? child->Instance : null;
            if (inst == null) continue;
            if (inst->Id.Type is InstanceType.Sound or InstanceType.SoundEnvSet)
            {
                if (inst->IsActive) { inst->SetActive(false); changed = true; }
            }
            else if (inst->Id.Type == InstanceType.SharedGroup && depth > 0)
            {
                changed |= SilenceSoundsRecursive((SharedGroupLayoutInstance*)inst, depth - 1);
            }
        }
        return changed;
    }

    public static bool KeepSuppressed(uint layoutId)
    {
        var world = LayoutWorld.Instance();
        if (world == null) return false;
        var instance = world->GetLayoutInstance(InstanceType.SharedGroup, layoutId);
        if (instance == null) return false;
        var reactivated = instance->IsActive;
        if (reactivated) SetActiveRecursive(instance, false, 3);
        SilenceSoundsRecursive((SharedGroupLayoutInstance*)instance, depth: 3);
        return reactivated;
    }

    // The instance and all direct children report fully loaded.
    public static bool IsFullyLoaded(uint layoutId)
    {
        var world = LayoutWorld.Instance();
        if (world == null) return false;
        var instance = world->GetLayoutInstance(InstanceType.SharedGroup, layoutId);
        if (instance == null) return false;
        if (!instance->HavePrimary() || !instance->IsPrimaryLoaded()) return false;
        var sg = (SharedGroupLayoutInstance*)instance;
        var childCount = sg->Instances.Instances.Count;
        for (var i = 0; i < childCount && i < 16; i++)
        {
            var child = (ChildNodeInstance*)sg->Instances.Instances[i].Value;
            var childInstance = child != null ? child->Instance : null;
            if (childInstance != null && childInstance->HavePrimary() && !childInstance->IsPrimaryLoaded())
                return false;
        }
        return true;
    }

    private static string FormatChild(ILayoutInstance* childInstance, int depth)
    {
        if (childInstance == null) return "null";
        var childGraphics = childInstance->GetGraphics();
        var summary = $"Type={childInstance->Id.Type} HavePrimary={childInstance->HavePrimary()} "
            + $"IsPrimaryLoaded={childInstance->IsPrimaryLoaded()} IsActive={childInstance->IsActive} Graphics=0x{(nint)childGraphics:X}";
        // The BgPart's model path is what identifies an arena piece; nothing else on the struct
        // says what loaded.
        if (childInstance->Id.Type == InstanceType.BgPart && childGraphics != null)
        {
            var bgObject = (BgObject*)childGraphics;
            var handle = bgObject->ModelResourceHandle;
            summary += handle != null ? $" ModelPath=\"{handle->FileName}\"" : " ModelPath=(no ModelResourceHandle)";
        }
        if (depth <= 0 || childInstance->Id.Type != InstanceType.SharedGroup) return summary;
        var nested = (SharedGroupLayoutInstance*)childInstance;
        var nestedCount = nested->Instances.Instances.Count;
        var nestedSummaries = new List<string>();
        for (var i = 0; i < nestedCount && i < 16; i++)
        {
            var nestedChild = (ChildNodeInstance*)nested->Instances.Instances[i].Value;
            var nestedInstance = nestedChild != null ? nestedChild->Instance : null;
            nestedSummaries.Add($"[{i}]={FormatChild(nestedInstance, depth: depth - 1)}");
        }
        return $"{summary} NestedChildCount={nestedCount} NestedChildren=[{string.Join("; ", nestedSummaries)}]";
    }
}
