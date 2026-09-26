using System;
using System.Collections.Generic;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using InteropGenerator.Runtime;

namespace AnoMech.Core.Native;

// Render-side ground truth: whether an effect was drawn, where, and for how long.
//
// Every VFX the client draws is a Client::Graphics::Scene::VfxObject, made by one of two
// factories: the actor one (lockons, tethers, the cast glow, AddVfx) or VfxObject::Create
// (omens, zone effects, and what a TMB's VFX track instantiates through the scheduler). Both
// are hooked, plus the destructor, which is logged for every VfxObject with the path read from
// its resource handle. The fight's own assets (see Watched) are sampled frame by frame:
// position, yaw, scale, draw/visibility bits, fade flag, alpha, caster/target ids.
//
// Enabled per scenario (Flood). Hooks are created on first use; if the hooking layer refuses
// (trampoline-buffer failures on reloads) the log stays off rather than the plugin dying. Only
// signatures the plugin already depends on are hooked.
internal static unsafe class VfxSpawnLog
{
    private const string ActorCreateSig = "40 53 55 56 57 48 81 EC ?? ?? ?? ?? 0F 29 B4 24 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 0F";
    // VfxObject::~VfxObject(this, freeFlags), the function VfxDataPointers resolves for RemoveActorVfx.
    private const string DtorSig = "48 89 5C 24 ?? 57 48 83 EC 20 48 8D 05 ?? ?? ?? ?? 48 8B D9 48 89 01 8B FA 48 8D 05 ?? ?? ?? ?? 48 89 81 ?? ?? ?? ?? 48 8B 89 ?? ?? ?? ?? 48 85 C9 74 09";
    private delegate VfxObject* ActorCreateDelegate(byte* path, GameObject* caster, GameObject* target, float a4, byte a5, ushort a6, byte a7);
    private delegate VfxObject* SceneCreateDelegate(byte* path, byte* pool);
    private delegate VfxObject* DtorDelegate(VfxObject* thisPtr, byte freeFlags);

    private static Hook<ActorCreateDelegate>? actorCreateHook;
    private static Hook<SceneCreateDelegate>? sceneCreateHook;
    private static Hook<DtorDelegate>? dtorHook;
    private static bool failed;
    private static long frame;

    private sealed class Entry
    {
        public required string Path;
        public required string Kind;      // "actor" or "scene"
        public required long Frame;       // creation frame
        public required uint Caster;
        public required nint Vtable;
        public required bool Watched;
    }

    private static readonly Dictionary<nint, Entry> live = new();

    // Sample frames after creation: dense early, then every half second past the TMB's 90 frames.
    private static readonly HashSet<long> SampleFrames =
        [0, 1, 2, 3, 4, 5, 6, 8, 10, 12, 15, 20, 25, 30, 40, 50, 60, 75, 90, 100, 105, 120, 150, 180];
    private const long ForgetAfterFrames = 3600;

    public static bool Enabled { get; private set; }

    // Stamped on every line, so a Cast line can be correlated with the VFX it produced.
    public static long Frame => frame;

    // Dancing Mad P5's own assets ("z5r2"), gimmick effects, and omens.
    private static bool Watched(string path)
        => path.Contains("z5r2", StringComparison.OrdinalIgnoreCase)
           || path.Contains("gimmick", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("vfx/omen/", StringComparison.OrdinalIgnoreCase);

    public static void Tick()
    {
        frame++;
        if (live.Count == 0) return;
        List<nint>? forget = null;
        foreach (var (ptr, entry) in live)
        {
            var age = frame - entry.Frame;
            if (age > ForgetAfterFrames)
            {
                (forget ??= new()).Add(ptr);
                continue;
            }
            if (!entry.Watched || !SampleFrames.Contains(age)) continue;
            try
            {
                // Logged even when nothing changed: a flat trace is itself the finding.
                DiagnosticLog.Info($"[VfxSpawnLog] ~{entry.Kind} 0x{ptr:X} \"{Leaf(entry.Path)}\" +{age}f: {Describe((VfxObject*)ptr, entry.Vtable)} frame={frame}");
            }
            catch (Exception e)
            {
                DiagnosticLog.Warn($"[VfxSpawnLog] sample failed for 0x{ptr:X}: {e.Message}");
            }
        }
        if (forget != null)
            foreach (var ptr in forget)
            {
                DiagnosticLog.Info($"[VfxSpawnLog] forgetting 0x{ptr:X} \"{Leaf(live[ptr].Path)}\" -- no destructor seen in {ForgetAfterFrames} frames.");
                live.Remove(ptr);
            }
    }

    public static void Enable()
    {
        if (failed) return;
        if (Enabled)
        {
            DiagnosticLog.Info($"[VfxSpawnLog] already enabled ({live.Count} VFX tracked) frame={frame}.");
            return;
        }
        try
        {
            actorCreateHook ??= Plugin.GameInterop.HookFromAddress<ActorCreateDelegate>(Plugin.SigScanner.ScanText(ActorCreateSig), ActorCreateDetour);
            dtorHook ??= Plugin.GameInterop.HookFromAddress<DtorDelegate>(Plugin.SigScanner.ScanText(DtorSig), DtorDetour);
            if (sceneCreateHook == null)
            {
                // Scanned by signature if FFXIVClientStructs' resolver left it empty.
                var address = VfxObject.Addresses.Create.Value;
                if (address == 0) address = Plugin.SigScanner.ScanText(VfxObject.Addresses.Create.String);
                sceneCreateHook = Plugin.GameInterop.HookFromAddress<SceneCreateDelegate>(address, SceneCreateDetour);
            }
            actorCreateHook.Enable();
            sceneCreateHook.Enable();
            dtorHook.Enable();
            Enabled = true;
            DiagnosticLog.Info($"[VfxSpawnLog] enabled -- every VFX create (actor and scene factories) and every VfxObject destructor is logged with a frame stamp; watched VFX are sampled frame by frame. frame={frame}");
        }
        catch (Exception e)
        {
            failed = true;
            DiagnosticLog.Warn($"[VfxSpawnLog] could not hook the VFX functions ({e.GetType().Name}: {e.Message}) -- render logging off.");
        }
    }

    public static void Disable()
    {
        if (!Enabled) return;
        actorCreateHook?.Disable();
        sceneCreateHook?.Disable();
        dtorHook?.Disable();
        Enabled = false;
        DiagnosticLog.Info($"[VfxSpawnLog] disabled -- {live.Count} VFX still alive at this point. frame={frame}");
    }

    public static void Dispose()
    {
        Disable();
        actorCreateHook?.Dispose();
        sceneCreateHook?.Dispose();
        dtorHook?.Dispose();
        actorCreateHook = null;
        sceneCreateHook = null;
        dtorHook = null;
        live.Clear();
    }

    private static VfxObject* ActorCreateDetour(byte* path, GameObject* caster, GameObject* target, float a4, byte a5, ushort a6, byte a7)
    {
        var result = actorCreateHook!.Original(path, caster, target, a4, a5, a6, a7);
        try
        {
            var p = path == null ? "(null)" : ((CStringPointer)path).ToString();
            var casterId = caster == null ? 0u : caster->EntityId;
            var targetId = target == null ? 0u : target->EntityId;
            var casterName = caster == null ? "-" : caster->GetName().ToString();
            var casterPos = caster == null ? "-" : $"({caster->Position.X:F1},{caster->Position.Y:F1},{caster->Position.Z:F1}) rot={caster->Rotation:F3}";
            DiagnosticLog.Info($"[VfxSpawnLog] +actor 0x{(nint)result:X} \"{p}\" caster=0x{casterId:X} \"{casterName}\" at {casterPos} target=0x{targetId:X} a4={a4:F2} a5={a5} a6={a6} a7={a7} frame={frame}");
            Track(result, p, "actor", casterId);
        }
        catch (Exception e)
        {
            DiagnosticLog.Warn($"[VfxSpawnLog] actor-create log failed: {e.Message}");
        }
        return result;
    }

    private static VfxObject* SceneCreateDetour(byte* path, byte* pool)
    {
        var result = sceneCreateHook!.Original(path, pool);
        try
        {
            var p = path == null ? "(null)" : ((CStringPointer)path).ToString();
            var poolName = pool == null ? "(null)" : ((CStringPointer)pool).ToString();
            var state = result == null ? "null" : Describe(result, *(nint*)result);
            DiagnosticLog.Info($"[VfxSpawnLog] +scene 0x{(nint)result:X} \"{p}\" pool=\"{poolName}\" {state} frame={frame}");
            Track(result, p, "scene", 0);
        }
        catch (Exception e)
        {
            DiagnosticLog.Warn($"[VfxSpawnLog] scene-create log failed: {e.Message}");
        }
        return result;
    }

    private static void Track(VfxObject* vfx, string path, string kind, uint caster)
    {
        if (vfx == null) return;
        live[(nint)vfx] = new Entry
        {
            Path = path,
            Kind = kind,
            Frame = frame,
            Caster = caster,
            Vtable = *(nint*)vfx,
            Watched = Watched(path),
        };
    }

    // Runs for every VfxObject the client destroys, most of which this log never saw created.
    // Only objects in `live` are touched, and only for their own fields: an untracked object is
    // mid-teardown with its resource pointers already poisoned, and following those was an
    // access violation that killed the process. The try/catch below cannot help there --
    // .NET Core never delivers AccessViolationException to a managed handler -- so the guard has
    // to be not reading the memory in the first place.
    private static VfxObject* DtorDetour(VfxObject* thisPtr, byte freeFlags)
    {
        try
        {
            if (thisPtr != null && live.Remove((nint)thisPtr, out var entry))
                DiagnosticLog.Info($"[VfxSpawnLog] -destroy 0x{(nint)thisPtr:X} \"{Leaf(entry.Path)}\" ({entry.Kind}, caster=0x{entry.Caster:X}) after {frame - entry.Frame} frames (freeFlags={freeFlags}): {Describe(thisPtr, entry.Vtable)} frame={frame}");
        }
        catch (Exception e)
        {
            DiagnosticLog.Warn($"[VfxSpawnLog] destroy log failed: {e.Message}");
        }
        return dtorHook!.Original(thisPtr, freeFlags);
    }

    // A vtable other than the one it was created with means a freed-and-reused block.
    private static string Describe(VfxObject* vfx, nint expectedVtable)
    {
        var scene = (FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object*)vfx;
        var draw = (DrawObject*)vfx;
        var q = scene->Rotation;
        var yaw = MathF.Atan2(2f * (q.W * q.Y + q.X * q.Z), 1f - 2f * (q.Y * q.Y + q.Z * q.Z));
        var vtable = *(nint*)vfx;
        var same = vtable == expectedVtable ? "" : $" VTABLE-CHANGED(0x{vtable:X})";
        var fading = (vfx->SomeFlags & 0x40) != 0 ? "(fading)" : "";
        return $"pos=({scene->Position.X:F2},{scene->Position.Y:F2},{scene->Position.Z:F2}) yaw={yaw:F3} scale=({scene->Scale.X:F2},{scene->Scale.Y:F2},{scene->Scale.Z:F2}) "
             + $"objFlags=0x{scene->ObjectFlags:X} drawFlags=0x{draw->Flags:X2}(visible={draw->IsVisible}) loadState={draw->LoadState} someFlags=0x{vfx->SomeFlags:X2}{fading} "
             + $"speed={vfx->Speed:F2} alpha={vfx->Color.W:F2} actor={vfx->ActorCaster}/{vfx->ActorTarget} static={vfx->StaticCaster}/{vfx->StaticTarget}{same}";
    }

    private static string Leaf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }
}
