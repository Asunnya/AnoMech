using System;

namespace AnoMech.Core.UserActions;

// Seeds the player's resources from JobActions.StartingResources at scenario start.
internal sealed class StartingResourcesHandler : IUserActionHandler
{
    private readonly Random rng = new();

    public void OnScenarioStart()
    {
        var player = Plugin.GameInstance?.Player;
        if (player == null) return;
        JobActions.ApplyStartingResources(player, rng);
    }
}
