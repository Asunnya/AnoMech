using AnoMech.Core.Native.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.SimObjects;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Scenarios.Uwu.UwuConstants;

namespace AnoMech.Scenarios.Uwu;

public enum LandslideType
{
    Normal,
    Awaken,
    Ultima
}

public class UwuUtils(SimWorld world)
{
    private readonly SimWorld world = world;

    public void UpdateArena(byte value)
    {
        byte[] unionData = [value];
        Natives.Director.SetDirectorData(1, 0, unionData, true);
    }

    public void Awaken(SimEnemy? enemy, bool isUltima)
    {
        enemy?.AddStatusParam(StatusId.Woken, isUltima ? 97 : 0);
        enemy?.SetAnimationState(0, 1);
    }

    public void ResolveSnapshot(IReadOnlyList<SimCharacter> snapshot, string dieCause)
    {
        foreach (var character in snapshot)
        {
            if (character.HasStatus(StatusId.Fetters))
            {
                continue;
            }

            character.Die(dieCause);
        }
    }

    public void Cast(Func<SimEnemy?> getEnemy,
        float castOffset, UwuUtilsRecords castInfo, float effectOffset, ActionEffectInfo actionEffectInfo,
        DynamicInfo dynamicInfo,
        float resolveDelay = 0, Action<IReadOnlyList<SimCharacter>>? resolveAction = null)
    {
        world.Events.Add(castOffset, () =>
        {
            var enemy = getEnemy();

            enemy?.NativeCast(
                castInfo.ActionId,
                castInfo.ActionType,
                castInfo.OmenDelay,
                castInfo.CastTime,
                castInfo.Interruptible,
                dynamicInfo.GetValue(dynamicInfo.CastRotation),
                dynamicInfo.GetValue(dynamicInfo.CastPosition),
                dynamicInfo.GetValue(dynamicInfo.CastTarget),
                dynamicInfo.GetValue(dynamicInfo.CastBallistaTarget)
                );
        });

        IReadOnlyList<SimCharacter> snapshot = null!;

        if (resolveAction != null)
        {
            world.Events.Add(castOffset + castInfo.CastTime, () =>
            {
                var enemy = getEnemy();
                Placement placement;

                if (dynamicInfo.CastRotation != null && dynamicInfo.CastPosition != null)
                {
                    placement = new(dynamicInfo.CastPosition(), dynamicInfo.CastRotation());
                }
                else if (dynamicInfo.CastRotation != null)
                {
                    placement = new(enemy!.Position, dynamicInfo.CastRotation());
                }
                else if (dynamicInfo.CastPosition != null)
                {
                    placement = new(dynamicInfo.CastPosition(), enemy!.Rotation);
                }
                else
                {
                    placement = enemy!.Placement();
                }

                snapshot = world.Party.Find.InsideActionAoe(castInfo.ActionId, placement);
            });
        }

        world.Events.Add(effectOffset, () =>
        {
            var enemy = getEnemy();

            enemy?.NativeActionEffect(
                actionEffectInfo.ActionId,
                actionEffectInfo.AnimationLock,
                actionEffectInfo.SpellId,
                actionEffectInfo.AnimationVariaton,
                actionEffectInfo.ActionType,
                actionEffectInfo.Flags,
                dynamicInfo.GetValue(dynamicInfo.ActionEffectRotation),
                dynamicInfo.GetValue(dynamicInfo.ActionEffectPosition),
                dynamicInfo.GetValue(dynamicInfo.ActionEffectAnimationTarget),
                dynamicInfo.GetValue(dynamicInfo.ActionEffectActionTarget),
                dynamicInfo.GetValue(dynamicInfo.ActionEffectBallistaTarget)
                );
        });

        if (resolveAction != null)
        {
            world.Events.Add(effectOffset + resolveDelay, () => resolveAction(snapshot));
        }
    }

    public void FeatherRain(Func<SimEnemy?>[] getDummies, float snapshotOffset, float castOffset, float effectOffset)
    {
        var positions = new List<Vector3>();

        world.Events.Add(snapshotOffset, () => positions.AddRange(
            RoleList.Random(world.Rng, world.Party, getDummies.Length).List
            .Select(x => world.Party.Get(x)!.Position)));

        var castInfo = new UwuUtilsRecords
        {
            ActionId = ActionId.FeatherRain,
            ActionType = ActionType.Action,
            CastTime = 0.7f
        };

        var actionEffectInfo = new ActionEffectInfo
        {
            ActionId = ActionId.FeatherRain,
            AnimationLock = 1.1f,
            SpellId = (ushort)ActionId.FeatherRain,
            ActionType = ActionType.Action
        };

        for (int i = 0; i < getDummies.Length; i++)
        {
            var getDummy = getDummies[i];

            var dynamicInfo = new DynamicInfo
            {
                ActionEffectPosition = () => getDummy()!.Position
            };

            // if "i" is used directly, then the value will be 5 when the Action is executed
            var index = i;

            world.Events.Add(castOffset, () =>
            {
                var dummy = getDummy();

                dummy?.SetPosition(
                    new Placement(
                        positions[index],
                        0
                        ));
            });

            Cast(getDummy, castOffset, castInfo, effectOffset, actionEffectInfo, dynamicInfo, 0.2f, snapshot => ResolveSnapshot(snapshot, "Feather Rain"));
        }
    }

    public void EruptionPuddle(Func<SimEnemy?> getDummy, Func<SimCharacter?> getBait, float castOffset, float effectOffset)
    {
        var baitPosition = Vector3.Zero;

        var getBaitPosition = () => baitPosition;
        var getPi = () => float.Pi;

        world.Events.Add(castOffset, () =>
        {
            baitPosition = getBait()!.Position;
        });

        var castInfo = new UwuUtilsRecords
        {
            ActionId = ActionId.EruptionPuddle,
            ActionType = ActionType.Action,
            OmenDelay = 0f,
            CastTime = 2.7f,
            Interruptible = false
        };

        var actionEffectInfo = new ActionEffectInfo
        {
            ActionId = ActionId.EruptionPuddle,
            AnimationLock = 0.1f,
            SpellId = (ushort)ActionId.EruptionPuddle,
            AnimationVariaton = 0,
            ActionType = ActionType.Action,
            Flags = 0
        };

        var dynamicInfo = new DynamicInfo
        {
            CastRotation = getPi,
            CastPosition = getBaitPosition,
            ActionEffectRotation = getPi,
            ActionEffectPosition = getBaitPosition,
        };

        Cast(getDummy, castOffset, castInfo, effectOffset, actionEffectInfo, dynamicInfo, 0.66f, snapshot => ResolveSnapshot(snapshot, "Eruption"));
    }

    public void LandslideLines(Func<SimEnemy?> getEnemy, Func<SimEnemy?>[] getDummies, float castOffset, float effectOffset, LandslideType type)
    {
        float[] rotationOffsets;
        uint actionId;
        float castTime;
        float animationLock;

        switch (type)
        {
            case LandslideType.Normal:
                rotationOffsets = Geometry.TitanLandslideOffsets.ToArray();
                actionId = ActionId.LandslideLine;
                castTime = 1.9f;
                animationLock = 2.1f;
                break;
            case LandslideType.Awaken:
                rotationOffsets = Geometry.TitanLandslideAwakenOffsets.ToArray();
                actionId = ActionId.LandslideAwaken;
                castTime = 1.7f;
                animationLock = 1.1f;
                break;
            case LandslideType.Ultima:
                rotationOffsets = Geometry.UltimaLandslideOffsets.ToArray();
                actionId = ActionId.LandslideLineUltima;
                castTime = 1.9f;
                animationLock = 1.1f;
                break;
            default:
                throw new Exception($"[UltimatePredationScenario.Landslide] Unsupported LandslideType {type}");
        }

        var castInfo = new UwuUtilsRecords
        {
            ActionId = actionId,
            ActionType = ActionType.Action,
            CastTime = castTime
        };

        var actionEffectInfo = new ActionEffectInfo
        {
            ActionId = actionId,
            AnimationLock = animationLock,
            SpellId = (ushort)actionId,
            ActionType = ActionType.Action
        };

        for (int i = 0; i < getDummies.Length; i++)
        {
            var getDummy = getDummies[i];

            var dynamicInfo = new DynamicInfo
            {
                CastTarget = getDummy,
                ActionEffectAnimationTarget = getDummy
            };

            // if "i" is used directly, then the value will be 5 when the Action is executed
            var index = i;

            world.Events.Add(castOffset, () =>
            {
                var enemy = getEnemy();
                var dummy = getDummy();

                dummy?.SetPosition(
                    new Placement(
                        enemy!.Position,
                        enemy.Rotation + rotationOffsets[index]
                        ));
            });

            Cast(getDummy, castOffset, castInfo, effectOffset, actionEffectInfo, dynamicInfo, 0.73f, snapshot =>
            {
                foreach (var character in snapshot)
                {
                    // TODO: "30" and "50" are from Titan EX, but doubled. Need to find the proper UWU values.
                    (character as ISimPartyMember)?.Knockback(getEnemy()!.Position, 30, 50);
                }
            });
        }
    }
}
