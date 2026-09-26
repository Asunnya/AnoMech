using System.Collections.Generic;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.Scheduler;

namespace AnoMech.Core.Native;

// Preloads "mon_sp/gimmick" ActionTimeline rows (LoadType 0: neither resident like idle/run nor
// part of a monster's own animation set) through the client's own entry point, so a helper's
// action VFX timeline has its resources up before the action fires. Without it a first play on
// a fresh actor ran with the base slot stuck at 0.00 for a few frames and then dropped (Flood's
// waves, Limit Cut's clones). Both spellings the engine might want are tried, the sheet key and
// the full .tmb path.
public static unsafe class ActionTimelinePreload
{
    public static void Preload(IEnumerable<(ushort Id, string Key)> rows, string tag)
    {
        var manager = ActionTimelineManager.Instance();
        if (manager == null)
        {
            DiagnosticLog.Warn($"[{tag}] ActionTimelineManager.Instance() is null -- no timeline preload.");
            return;
        }
        foreach (var (id, key) in rows)
        {
            foreach (var candidate in new[] { key, $"chara/action/{key}.tmb" })
            {
                var bytes = Encoding.UTF8.GetBytes(candidate + "\0");
                fixed (byte* keyPtr = bytes)
                {
                    var info = new ActionTimelineManager.PreloadActionTmbInfo { Key = keyPtr, Index = id };
                    var accepted = manager->PreloadActionTmb(&info);
                    DiagnosticLog.Info($"[{tag}] PreloadActionTmb({id}, \"{candidate}\") -> {accepted}.");
                }
            }
        }
    }
}
