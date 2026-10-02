using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace AnoMech.Core.Native.Interfaces;

// Party head-marker signs, written client-side only (no party broadcast).
public interface IMarkings
{
    void Set(Sign sign, GameObjectId target);
    void Clear(Sign sign);
    void ClearAll();
}
