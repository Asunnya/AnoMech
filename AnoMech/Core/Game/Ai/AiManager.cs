using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Core.Game.Ai;

// Drives slot-ordered party movement from a scenario's position functions.
// Owns jitter, run speed, and event scheduling. Position functions return an
// AiMove whose entries are scenario-local XZ coords — same space MoveTo
// consumes, so AiManager forwards them as-is. Eye-spawn flip and slot
// reordering are handled inside the AiMove before it reaches here.
public sealed class AiManager
{
    // Measured in-game.
    private const float RunSpeed = 6.5f;
    private const float SprintSpeed = 8.3f;
    // Same values LocalPlayerInputHooks uses for a real Sprint press.
    private const ushort SprintStatusId = 50;
    private const int SprintStatusParam = 30;
    private const float DefaultJitter = 0.3f;
    // Move's deadline math leaves zero margin, and a move needing speed within a hair of RunSpeed
    // arrives short. Only ever makes arrival earlier.
    private const float MoveDeadlineSafetyMargin = 0.25f;

    private readonly SimWorld world;
    private readonly Random rng = new();

    public AiManager(SimWorld world)
    {
        this.world = world;
    }

    // Schedule a slot-move at `time`; null AiMove entries are skipped.
    // `arrivalTime` set: freeze until the last safe moment, then walk/sprint to
    // land exactly on it. Unset: go now, no sprint consideration. `sprint`
    // (only meaningful without `arrivalTime`) forces SprintSpeed and sizes the
    // Sprint status off distance instead of a deadline.
    //
    // Every prompt MoveTo fires via PromptMoveDelay: a 0-delay entry added during
    // EventScheduler.Tick runs in that same pass.
    private const float PromptMoveDelay = 0.3f;

    public void Move(float time, Func<IAiMove> positions, float jitter = DefaultJitter, float? arrivalTime = null, bool sprint = false)
    {
        world.Events.Add(time, () =>
        {
            var move = positions();
            // Diagnostic: two roles landing on the same spot has coincided with wipes.
            var seenTargets = new List<(string Role, Vector3 Target)>();
            for (int i = 0; i < 8; i++)
            {
                if (move[i] is not { } local) continue;
                var member = world.Party.Get(i);
                if (member == null || !member.IsAlive()) continue;
                var target = Jitter(new Vector3(local.X, 0f, local.Y), jitter);
                var role = (member as ISimPartyMember)?.Role.ToString() ?? $"slot{i}";
                foreach (var (seenRole, seenTarget) in seenTargets)
                {
                    if (Vector3.Distance(target, seenTarget) < 1f)
                        AnoMech.Core.DiagnosticLog.Warn($"[AiManager] Move@{time:F1}: {role} and {seenRole} both targeting ({target.X:F1},{target.Z:F1}) -- collision.");
                }
                seenTargets.Add((role, target));
                var dx = target.X - member.Position.X;
                var dz = target.Z - member.Position.Z;
                var dist = MathF.Sqrt(dx * dx + dz * dz);

                if (arrivalTime is not { } deadline)
                {
                    if (sprint)
                    {
                        member.AddStatus(SprintStatusId, dist / SprintSpeed, SprintStatusParam);
                        AnoMech.Core.DiagnosticLog.Info($"[AiManager] Move@{time:F1}: {role} from ({member.Position.X:F1},{member.Position.Z:F1}) -> ({target.X:F1},{target.Z:F1}) sprinting -- {dist:F1}y.");
                        world.Events.Add(PromptMoveDelay, () => member.MoveTo(target, speed: SprintSpeed));
                    }
                    else
                    {
                        AnoMech.Core.DiagnosticLog.Info($"[AiManager] Move@{time:F1}: {role} from ({member.Position.X:F1},{member.Position.Z:F1}) -> ({target.X:F1},{target.Z:F1}).");
                        world.Events.Add(PromptMoveDelay, () => member.MoveTo(target, speed: RunSpeed));
                    }
                    continue;
                }

                var available = deadline - time - MoveDeadlineSafetyMargin;
                var neededSpeed = available > 0f ? dist / available : float.PositiveInfinity;

                if (neededSpeed > RunSpeed && neededSpeed <= SprintSpeed)
                {
                    member.AddStatus(SprintStatusId, available, SprintStatusParam);
                    AnoMech.Core.DiagnosticLog.Info($"[AiManager] Move@{time:F1}: {role} from ({member.Position.X:F1},{member.Position.Z:F1}) -> ({target.X:F1},{target.Z:F1}) sprinting -- {dist:F1}y in {available:F2}s needs {neededSpeed:F2}y/s.");
                    world.Events.Add(PromptMoveDelay, () => member.MoveTo(target, speed: SprintSpeed));
                    continue;
                }

                var delay = available - dist / RunSpeed;
                if (delay > 0f)
                {
                    AnoMech.Core.DiagnosticLog.Info($"[AiManager] Move@{time:F1}: {role} from ({member.Position.X:F1},{member.Position.Z:F1}) -> ({target.X:F1},{target.Z:F1}) deferred {delay:F2}s (arrive {deadline:F1}).");
                    world.Events.Add(delay, () => member.MoveTo(target, speed: RunSpeed));
                    continue;
                }

                member.AddStatus(SprintStatusId, available, SprintStatusParam);
                AnoMech.Core.DiagnosticLog.Info($"[AiManager] Move@{time:F1}: {role} from ({member.Position.X:F1},{member.Position.Z:F1}) -> ({target.X:F1},{target.Z:F1}) can't make deadline {deadline:F1} even sprinting ({neededSpeed:F2}y/s needed) -- sprinting anyway, leaving now.");
                world.Events.Add(PromptMoveDelay, () => member.MoveTo(target, speed: SprintSpeed));
            }
        });
    }

    // Schedule temporary death-immunity for `role` at scenario-time `time`, e.g.
    // ai.GiveInvuln(28f, PartyRole.OffTank).
    public void GiveInvuln(float time, PartyRole role, float seconds = 10f)
        => world.Events.Add(time, () => world.Party.GiveInvuln(role, seconds));

    public void Automarker(float time, Func<Dictionary<PartyRole, Sign>> mapping)
    {
        world.Events.Add(time, () =>
        {
            Markings.ClearAll();
            var marks = mapping();
            AnoMech.Core.DiagnosticLog.Info($"[AiManager] Automarker@{time:F1}: [{string.Join(", ", marks.Select(kv => $"{kv.Key}={kv.Value}"))}].");
            foreach (var (role, sign) in marks)
                if (world.Party.Get(role) is { } member && member.IsAlive())
                    Markings.Set(sign, member.GameObjectId);
        });
    }

    private Vector3 Jitter(Vector3 target, float radius)
    {
        var theta = rng.NextDouble() * 2.0 * Math.PI;
        var r = radius * MathF.Sqrt((float)rng.NextDouble());
        return new Vector3(
            target.X + r * MathF.Cos((float)theta),
            target.Y,
            target.Z + r * MathF.Sin((float)theta));
    }
}
