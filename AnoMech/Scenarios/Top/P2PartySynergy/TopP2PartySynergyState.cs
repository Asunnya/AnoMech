using System;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Top.P2PartySynergy;

public class TopP2PartySynergyState
{
    private readonly Rng rng = new();

    public Direction NewNorthA { get; }
    public Direction NewNorthB { get; }
    public Direction AttackDir { get; }
    public RoleList Order { get; }
    public RoleList Stacks { get; }
    public GlitchType Glitch { get; }
    public OmegaAttack AttackM { get; }
    public OmegaAttack AttackF { get; }

    public TopP2PartySynergyState(SimParty party, TopP2PartySynergyStateOverrides overrides)
    {
        NewNorthA = overrides.NewNorthA ?? rng.NextDirection();
        NewNorthB = overrides.NewNorthB ?? rng.NextDirection();
        Order = RoleList.Random(party);
        Stacks = RoleList.Random(party, 2);
        Glitch = overrides.Glitch ?? rng.NextObj(GlitchType.Far, GlitchType.Mid);
        AttackM = overrides.AttackM ?? rng.NextObj(OmegaAttack.Sword, OmegaAttack.Shield);
        AttackF = overrides.AttackF ?? rng.NextObj(OmegaAttack.Staff, OmegaAttack.Legs);
        AttackDir = rng.NextDirection().RotateRad(MathF.Tau / 16);
    }

    // Network-replay constructor: reconstructs the fields TopP2PartySynergyAi reads -- all of
    // them here. GlitchType/OmegaAttack aren't JSON-serializable (identified only by reference
    // equality to a static instance), so the wire message carries which named instance was
    // chosen.
    private TopP2PartySynergyState(
        SimParty party, PartyRole[] order, PartyRole[] stacks, float newNorthARadians, float newNorthBRadians,
        float attackDirRadians, bool glitchIsFar, bool attackMIsSword, bool attackFIsStaff)
    {
        NewNorthA = new Direction(newNorthARadians);
        NewNorthB = new Direction(newNorthBRadians);
        Order = new RoleList(party, order);
        Stacks = new RoleList(party, stacks);
        Glitch = glitchIsFar ? GlitchType.Far : GlitchType.Mid;
        AttackM = attackMIsSword ? OmegaAttack.Sword : OmegaAttack.Shield;
        AttackF = attackFIsStaff ? OmegaAttack.Staff : OmegaAttack.Legs;
        AttackDir = new Direction(attackDirRadians);
    }

    public static TopP2PartySynergyState FromNetworkReplay(
        SimParty party, PartyRole[] order, PartyRole[] stacks, float newNorthARadians, float newNorthBRadians,
        float attackDirRadians, bool glitchIsFar, bool attackMIsSword, bool attackFIsStaff)
        => new(party, order, stacks, newNorthARadians, newNorthBRadians, attackDirRadians, glitchIsFar, attackMIsSword, attackFIsStaff);
}
