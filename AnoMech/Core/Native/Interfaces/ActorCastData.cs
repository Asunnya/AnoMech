using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace AnoMech.Core.Native.Interfaces;

// The server's ActorCast packet: starts the cast bar and the omen. World space.
public readonly record struct ActorCastData(
    uint ActionId,
    ActionType ActionType,
    float CastTime,
    float OmenDelay,
    bool Interruptible,
    float Rotation,
    Vector3 Position,
    GameObjectId? Target = null,
    GameObjectId? Ballista = null);
