using AnoMech.Core.Game;
using AnoMech.Core.Native.Interfaces;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Lumina.Excel.Sheets;
using System;
using System.Numerics;
using System.Text;

namespace AnoMech.Core.Native.Implementations;

// Static VFX (omens): setting CastInfo manually doesn't trigger Character::StartCast's omen spawn,
// so this is the same StaticVfxCreate / StaticVfxRun / StaticVfxRemove trio VFXEditor and
// FFXIV-RaidsRewritten use.
internal sealed unsafe class VfxFunctions : IVfxFunctions
{
    private static readonly byte[] PoolBytes = Encoding.UTF8.GetBytes("Client.System.Scheduler.Instance.VfxObject\0");

    public IStaticVfxProxy? SpawnStatic(string path, Placement placement, Vector3 scale)
    {
        if (string.IsNullOrEmpty(path) || !PathExists(path)) return null;
        var pathBytes = Encoding.UTF8.GetBytes(path + "\0");
        VfxObject* vfx;
        fixed (byte* pathPtr = pathBytes)
        fixed (byte* poolPtr = PoolBytes)
        {
            vfx = VfxObject.Create(pathPtr, poolPtr);
        }
        if (vfx == null) return null;
        vfx->Position = placement.Position;
        vfx->Rotation = Quaternion.CreateFromYawPitchRoll(placement.Rotation, 0f, 0f);
        vfx->Scale = scale;
        vfx->Flags |= 0x2;          // mark dirty so position/rotation/scale apply
        vfx->SomeFlags &= 0xF7;     // clear flag that sometimes hides the vfx
        vfx->Update(0f, -1);
        return new StaticVfxProxy(vfx);
    }

    public bool PathExists(string path)
    {
        try
        {
            if (Plugin.DataManager.FileExists(path)) return true;
            Plugin.Log.Warning($"VfxFunctions: path not found '{path}'");
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"VfxFunctions: FileExists threw for '{path}': {ex.Message}");
        }
        return false;
    }

    public string? LockonIconName(uint lockonId)
    {
        var sheet = Plugin.DataManager.GetExcelSheet<Lockon>();
        if (!sheet.TryGetRow(lockonId, out var row)) return null;
        var iconName = row.IconName.ExtractText();
        return string.IsNullOrEmpty(iconName) ? null : iconName;
    }
}
