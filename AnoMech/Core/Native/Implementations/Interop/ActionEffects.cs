using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace AnoMech.Core.Native.Implementations.Interop;

// Synthesizes an ActionEffectN through the game's own handler — the same path the
// client runs on a server ActionEffect packet — so effects the sim firewall blocks
// are replayed rather than poked into internals.
internal static unsafe class ActionEffects
{
    // Synthetic monotonically-increasing GlobalSequence (server's per-action id). The
    // local player's own effect correlates by SourceSequence, so this is only for
    // faithfulness; it just needs to be non-zero and unique per fire.
    private static uint globalSequence = 1;

    // Replays the combo signal (Type-0x1B: Value = used action, Param0 = 0 starter /
    // 1 continuation) delivered to the caster's target, so the client advances
    // ActionManager.Combo itself and lights up the next action. Packet shape from replay
    // data — see reference-combo-mechanism. SourceSequence must be the action's own
    // sequence so the client treats it as that action resolving. Delivers to `target`
    // (falling back to self if it's absent or not a registered BattleChara).
    public static void FireCombo(Character* caster, uint actionId, ushort sourceSequence, byte comboFlag,
        GameObjectId target = default)
    {
        if (caster == null) return;
        var deliverTo = caster->GetGameObjectId();
        if (target.ObjectId != 0 && target.ObjectId != 0xE0000000)
        {
            var cm = CharacterManager.Instance();
            if (cm != null && cm->LookupBattleCharaByEntityId(target.ObjectId) != null)
                deliverTo = target;
        }

        var header = new ActionEffectHandler.Header
        {
            AnimationTargetId = deliverTo,
            ActionId = actionId,
            GlobalSequence = globalSequence++,
            AnimationLock = 0f,
            SourceSequence = sourceSequence,
            RotationInt = MathUtil.QuantizeRotation(caster->Rotation),
            SpellId = (ushort)actionId,
            AnimationVariation = 0,
            ActionType = (byte)ActionType.Action,
            Flags = 0,
            NumTargets = 1,
        };
        var effects = new ActionEffectHandler.TargetEffects();
        effects.Effects[0] = new ActionEffectHandler.Effect
        {
            Type = 0x1B,
            Param0 = comboFlag,
            Param4 = 0x80,
            Value = (ushort)actionId,
        };
        var p = caster->Position;
        var pos = new System.Numerics.Vector3(p.X, p.Y, p.Z); // Receive wants System.Numerics
        ActionEffectHandler.Receive(caster->EntityId, caster, &pos, &header, &effects, &deliverTo);
    }

    // One auto-attack swing, shaped like the server's: SourceSequence 0 (the server swings on
    // its own timer), a 0.1s lock, and the variation cycling 0-2.
    public static void FireAutoAttack(Character* caster, uint actionId, GameObjectId target, byte animationVariation)
    {
        if (caster == null) return;
        var header = new ActionEffectHandler.Header
        {
            AnimationTargetId = target,
            ActionId = actionId,
            GlobalSequence = globalSequence++,
            AnimationLock = 0.1f,
            BallistaEntityId = 0xE0000000,
            SourceSequence = 0,
            RotationInt = MathUtil.QuantizeRotation(caster->Rotation),
            SpellId = (ushort)actionId,
            AnimationVariation = animationVariation,
            ActionType = (byte)ActionType.Action,
            Flags = 0,
            NumTargets = 1,
        };
        var effects = new ActionEffectHandler.TargetEffects();
        var p = caster->Position;
        var pos = new System.Numerics.Vector3(p.X, p.Y, p.Z);
        ActionEffectHandler.Receive(caster->EntityId, caster, &pos, &header, &effects, &target);
    }
}
