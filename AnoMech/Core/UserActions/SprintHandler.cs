using AnoMech.Core.SimObjects;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AnoMech.Core.UserActions;

// Sprint's status, which the server would grant on use, plus clearing its cooldown at
// scenario start. Resolved unconditionally — unlike the other handlers, it runs even
// when the module is disabled (UserActions keeps it separate from the gated list).
internal sealed unsafe class SprintHandler : IUserActionHandler
{
    private const uint SprintActionId = 3;
    private const ushort SprintStatusId = 50;
    private const float SprintDuration = 10f;
    private const ushort SprintStatusParam = 30; // speed magnitude, carried in Status.Param

    public void OnAction(ActionType actionType, uint actionId)
    {
        // Effect lands on the scenario's SimPlayer (null off-scenario, so this no-ops).
        if (actionType == ActionType.Action && actionId == SprintActionId)
            if (Plugin.GameInstance?.Player is { } player) Apply(player, SprintDuration);
    }

    public static void Apply(SimCharacter character, float duration)
        => character.AddStatus(SprintStatusId, duration, SprintStatusParam);

    // Sprint goes on cooldown when the player presses it inside a scenario
    // (LocalPlayerInputHooks lets Original run so the recast starts). Clear it so each
    // scenario starts with Sprint ready, regardless of a press just before Start.
    public void OnScenarioStart()
    {
        var am = ActionManager.Instance();
        if (am == null) return;
        var group = am->GetRecastGroup((int)ActionType.Action, SprintActionId);
        if (group < 0) return;
        var detail = am->GetRecastGroupDetail(group);
        if (detail == null) return;
        detail->IsActive = false;
        detail->Elapsed = 0f;
    }
}
