using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace AnoMech.Core.Native.Interfaces;

// The server's ActionEffect packet with no effect entries: plays the action's release animation
// and VFX. A null ActionTarget sends no target; some actions only animate with one. World space.
public readonly record struct ActionEffectData(
    uint ActionId,
    ActionType ActionType,
    float AnimationLock,
    ushort SpellId,
    byte AnimationVariation,
    byte Flags,
    float Rotation,
    Vector3 Position,
    GameObjectId? AnimationTarget = null,
    GameObjectId? ActionTarget = null,
    GameObjectId? Ballista = null);
