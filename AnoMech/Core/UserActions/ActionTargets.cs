using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.SimObjects;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace AnoMech.Core.UserActions;

// Who a player action lands on, read from the Action sheet: CastType 1 is the single
// target, CastType 2 a circle of EffectRange around the caster. The sheet does not say
// which side an action affects (Reprisal and Heart of Light differ only in radius), so
// the caller picks Friendly or Hostile.
internal static class ActionTargets
{
    private const byte SingleTarget = 1;
    private const byte CircleAroundCaster = 2;

    public static IReadOnlyList<SimCharacter> Friendly(ActionContext ctx)
    {
        if (Plugin.GameInstance is not { } game || !TryGetAction(ctx.ActionId, out var action)) return [];
        var members = game.World.Party.ActiveMembers();
        switch (action.CastType)
        {
            case SingleTarget:
                var target = members.FirstOrDefault(m => (ulong)m.GameObjectId == ctx.TargetId);
                if (target != null && !ReferenceEquals(target, ctx.Caster) && action.CanTargetParty) return [target];
                return action.CanTargetSelf ? [ctx.Caster] : [];
            case CircleAroundCaster:
                return members.Where(m => InRange(ctx.Caster, m, action.EffectRange)).ToList();
            default:
                Plugin.Log.Warning($"ActionTargets: action {ctx.ActionId} has unsupported CastType {action.CastType}; applied to the caster only");
                return [ctx.Caster];
        }
    }

    public static IReadOnlyList<SimEnemy> Hostile(ActionContext ctx)
    {
        if (Plugin.GameInstance is not { } game || !TryGetAction(ctx.ActionId, out var action)) return [];
        var enemies = game.World.Children.OfType<SimEnemy>().Where(e => e.IsActive);
        switch (action.CastType)
        {
            case SingleTarget:
                return enemies.Where(e => (ulong)e.GameObjectId == ctx.TargetId).ToList();
            case CircleAroundCaster:
                return enemies.Where(e => InRange(ctx.Caster, e, action.EffectRange)).ToList();
            default:
                Plugin.Log.Warning($"ActionTargets: action {ctx.ActionId} has unsupported CastType {action.CastType}; no enemy hit");
                return [];
        }
    }

    private static bool TryGetAction(uint actionId, out LuminaAction action)
        => Plugin.DataManager.GetExcelSheet<LuminaAction>().TryGetRow(actionId, out action);

    // An AoE reaches a target whose hitbox edge is inside it, not just its centre.
    private static bool InRange(SimCharacter caster, SimCharacter target, float range)
    {
        var dx = target.Position.X - caster.Position.X;
        var dz = target.Position.Z - caster.Position.Z;
        var reach = range + target.HitboxRadius;
        return dx * dx + dz * dz <= reach * reach;
    }
}
