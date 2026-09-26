using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.Top.P5Omega;

public enum HelloWorldOrderOption { Auto, Any, First, Second, None }

public enum HelloWorldTypeOption { Auto, Near, Far }

// User-controlled overrides for TopP5OmegaState's randomized fields. Bound by the scenario's
// settings UI; null values leave the field randomized at scenario start. The state ctor consumes
// this directly.
public sealed class TopP5OmegaStateOverrides
{
    // --- Fight-wide: one roll the whole sim shares -------------------------------------
    public OmegaAttack? FirstFAttack { get; set; }
    public OmegaAttack? FirstMAttack { get; set; }
    public OmegaAttack? SecondFAttack { get; set; }
    public OmegaAttack? SecondMAttack { get; set; }
    public bool? FirstWaveCannonFront { get; set; }
    public MonitorSide? MonitorSide { get; set; }
    public Direction? BettleSpawnDirection { get; set; }

    // --- Per player: everyone has their own ---------------------------------------------
    public PerRoleSetting<HelloWorldOrderOption> HelloWorldOrder { get; set; } = new();
    public PerRoleSetting<HelloWorldTypeOption> HelloWorldType { get; set; } = new();
    public PerRoleSetting<bool> ExtraDynamis { get; set; } = new();

    // Order and type together pick out which of the four Hello World slots a seat may take;
    // None keeps it out. Two seats wanting the same slot: the earlier one takes it.
    public (Dictionary<PartyRole, int[]> Slots, Dictionary<PartyRole, bool> Membership) ResolveHelloWorld(PartyRole localPlayerRole)
    {
        var orders = HelloWorldOrder.Resolve(localPlayerRole).ToDictionary(x => x.Role, x => x.Value);
        var types = HelloWorldType.Resolve(localPlayerRole).ToDictionary(x => x.Role, x => x.Value);
        var slots = new Dictionary<PartyRole, int[]>();
        var membership = new Dictionary<PartyRole, bool>();
        foreach (var role in PerRole.All)
        {
            var order = orders.GetValueOrDefault(role, HelloWorldOrderOption.Auto);
            var type = types.GetValueOrDefault(role, HelloWorldTypeOption.Auto);
            if (order == HelloWorldOrderOption.None) { membership[role] = false; continue; }
            int[] indices = (order, type) switch
            {
                (HelloWorldOrderOption.Auto,   HelloWorldTypeOption.Near) => [0, 2],
                (HelloWorldOrderOption.Auto,   HelloWorldTypeOption.Far)  => [1, 3],
                (HelloWorldOrderOption.Any,    HelloWorldTypeOption.Auto) => [0, 1, 2, 3],
                (HelloWorldOrderOption.Any,    HelloWorldTypeOption.Near) => [0, 2],
                (HelloWorldOrderOption.Any,    HelloWorldTypeOption.Far)  => [1, 3],
                (HelloWorldOrderOption.First,  HelloWorldTypeOption.Auto) => [0, 1],
                (HelloWorldOrderOption.First,  HelloWorldTypeOption.Near) => [0],
                (HelloWorldOrderOption.First,  HelloWorldTypeOption.Far)  => [1],
                (HelloWorldOrderOption.Second, HelloWorldTypeOption.Auto) => [2, 3],
                (HelloWorldOrderOption.Second, HelloWorldTypeOption.Near) => [2],
                (HelloWorldOrderOption.Second, HelloWorldTypeOption.Far)  => [3],
                _ => [],
            };
            if (indices.Length > 0) slots[role] = indices;
        }
        return (slots, membership);
    }

    public Dictionary<PartyRole, bool> ResolveExtraDynamis(PartyRole localPlayerRole)
        => ExtraDynamis.Resolve(localPlayerRole).ToDictionary(x => x.Role, x => x.Value);

    // Four Hello World slots (first near, first far, second near, second far) and four extra
    // dynamis stacks. Order and type together can pin a seat to one slot, so two seats pinned
    // the same way collide even though neither rule alone is broken.
    public SettingsConflicts Validate()
    {
        var conflicts = new SettingsConflicts();
        if (!PerRole.SeatsActive) return conflicts;

        var (slots, membership) = ResolveHelloWorld(PartyRole.MainTank);
        conflicts.AtMost(4, slots.Keys.ToList(), "a Hello World tether");
        conflicts.AtLeast(4, membership.Where(m => !m.Value).Select(m => m.Key).ToList(), 8, "Hello World");
        if (!conflicts.Any && !SettingsConflicts.CanPlaceAll(slots, 4))
            conflicts.Add($"{SettingsConflicts.Seats(slots.Keys)} can't all get the Hello World order and type they asked for.");

        conflicts.AtMost(4, PerRole.All.Where(r => ExtraDynamis[r] == true).ToList(), "an extra dynamis stack");
        conflicts.AtLeast(4, PerRole.All.Where(r => ExtraDynamis[r] == false).ToList(), 8, "an extra dynamis stack");
        return conflicts;
    }
}
