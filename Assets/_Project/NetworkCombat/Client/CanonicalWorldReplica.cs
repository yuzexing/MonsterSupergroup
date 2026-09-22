using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Client-side queryable replica of server canonical shared facts.</summary>
    public sealed partial class CanonicalWorldReplica
    {
        private readonly Dictionary<uint, CanonicalEntityState> entities =
            new Dictionary<uint, CanonicalEntityState>();
        private readonly Dictionary<uint, StatusController> statusControllers =
            new Dictionary<uint, StatusController>();
        private readonly Dictionary<StatusInstanceId, uint> statusTargets =
            new Dictionary<StatusInstanceId, uint>();
        private readonly Dictionary<StatusInstanceId, CanonicalStatusState> canonicalStatuses =
            new Dictionary<StatusInstanceId, CanonicalStatusState>();
        private readonly HashSet<(uint Target, uint Version)> confirmedKills = new HashSet<(uint, uint)>();

        public event Action<CanonicalEntityState> EntityChanged;
        public event Action<CanonicalStatusState> StatusChanged;
        public event Action<ConfirmedKill> KillConfirmed;

        public void RegisterStatusController(uint entityId, StatusController controller)
        {
            if (entityId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityId));
            }

            if (controller == null) throw new ArgumentNullException(nameof(controller));
            using var evidence = CombatEvidence.Enabled ? CombatEvidence.Begin(this, "replica", "RegisterStatusController",
                new object[] { entityId, controller.CaptureReplayState() }, o => ((CanonicalWorldReplica)o).CaptureReplayState()) : default;
            statusControllers[entityId] = controller;
            CombatEvidence.Bind(controller, this, "controller." + entityId + ".", "replica", o => ((CanonicalWorldReplica)o).CaptureReplayState());

            // A scene object may register after the initial/late-join snapshot was
            // received. Rehydrate its queryable StatusController from retained
            // canonical facts instead of waiting for the next mutation.
            foreach (CanonicalStatusState state in canonicalStatuses.Values)
            {
                if (!state.Removed && state.TargetEntityId == entityId)
                {
                    controller.UpsertCanonical(state.ToStatusInstance());
                }
            }
            evidence.Complete();
        }

        public bool UnregisterStatusController(uint entityId, StatusController controller)
        {
            bool matches = statusControllers.TryGetValue(entityId, out StatusController registered) && ReferenceEquals(registered, controller);
            using var evidence = CombatEvidence.Enabled ? CombatEvidence.Begin(this, "replica", "UnregisterStatusController",
                new object[] { entityId, matches }, o => ((CanonicalWorldReplica)o).CaptureReplayState()) : default;
            bool result = matches && statusControllers.Remove(entityId);
            if (result) CombatEvidence.Unbind(controller);
            evidence.Complete(result); return result;
        }

        private void EvidenceCore_ForgetEntity(uint entityId)
        {
            entities.Remove(entityId);
            var removed = new List<StatusInstanceId>();
            foreach (var entry in statusTargets)
                if (entry.Value == entityId) removed.Add(entry.Key);
            foreach (var id in removed)
            {
                statusTargets.Remove(id);
                canonicalStatuses.Remove(id);
            }
        }

        public bool TryGetEntity(uint entityId, out CanonicalEntityState state)
        {
            return entities.TryGetValue(entityId, out state);
        }

        public bool HasStatus(uint targetEntityId, EnemyStatusID definitionId)
        {
            return statusControllers.TryGetValue(targetEntityId, out StatusController controller) &&
                controller.Has(definitionId);
        }

        /// <summary>Detached canonical records, including stacks locally predicted away.</summary>
        public IReadOnlyList<StatusInstance> ReadStatuses(uint targetEntityId)
        {
            var result = new List<StatusInstance>();
            foreach (CanonicalStatusState state in canonicalStatuses.Values)
                if (!state.Removed && state.TargetEntityId == targetEntityId) result.Add(state.ToStatusInstance());
            return result;
        }

        private void EvidenceCore_Apply(CanonicalWorldBatch batch)
        {
            CanonicalEntityState[] entityStates =
                batch.Entities ?? Array.Empty<CanonicalEntityState>();
            for (int i = 0; i < entityStates.Length; i++)
            {
                CanonicalEntityState incoming = entityStates[i];
                if (entities.TryGetValue(incoming.EntityId, out CanonicalEntityState current) &&
                    current.StateVersion > incoming.StateVersion)
                {
                    if (CombatEvidence.Enabled) CombatEvidence.Event("Replica", "replica.entity", "Ignored", "StaleStateVersion",
                        target: incoming.EntityId, input: incoming, before: current, after: current, server: batch.ServerSequence);
                    continue;
                }

                entities[incoming.EntityId] = incoming;
                if (CombatEvidence.Enabled) CombatEvidence.Event("Replica", "replica.entity", "Applied", "None",
                    target: incoming.EntityId, input: incoming, before: current, after: incoming, server: batch.ServerSequence);
                EntityChanged?.Invoke(incoming);
            }

            CanonicalStatusState[] statuses =
                batch.Statuses ?? Array.Empty<CanonicalStatusState>();
            for (int i = 0; i < statuses.Length; i++)
            {
                CanonicalStatusState state = statuses[i];
                var instanceId = new StatusInstanceId(state.InstanceId);
                if (canonicalStatuses.TryGetValue(instanceId, out var previous) &&
                    (state.ApplicationRevision < previous.ApplicationRevision ||
                     (state.ApplicationRevision == previous.ApplicationRevision &&
                      (state.Version < previous.Version ||
                       (state.Version == previous.Version && (previous.Removed ||
                        (!state.Removed && state.CompletedTicks <= previous.CompletedTicks)))))))
                {
                    if (CombatEvidence.Enabled) CombatEvidence.Event("Replica", "replica.status", "Ignored",
                        state.ApplicationRevision < previous.ApplicationRevision ? "OlderApplication" : state.Version < previous.Version ? "OlderStatusVersion" : previous.Removed ? "RemovalWatermark" : "TickAlreadyObserved",
                        state.SourceEventId, state.SourcePlayerId, state.TargetEntityId, state, previous, previous, state.RootEventId, state.ParentEventId, server: batch.ServerSequence);
                    continue;
                }
                if (state.Removed)
                {
                    if (statusTargets.TryGetValue(instanceId, out uint targetId) &&
                        statusControllers.TryGetValue(targetId, out StatusController controller))
                    {
                        controller.RemoveCanonical(instanceId, state.Version, state.ApplicationRevision);
                    }

                    // Keep the removal watermark until this entity/world is retired.
                    if (state.TargetEntityId != 0) statusTargets[instanceId] = state.TargetEntityId;
                    canonicalStatuses[instanceId] = state;
                }
                else
                {
                    StatusInstance instance = state.ToStatusInstance();
                    statusTargets[instance.InstanceId] = instance.TargetEntityId;
                    canonicalStatuses[instance.InstanceId] = state;
                    if (statusControllers.TryGetValue(
                        instance.TargetEntityId,
                        out StatusController controller))
                    {
                        controller.UpsertCanonical(instance);
                    }
                }

                if (CombatEvidence.Enabled) CombatEvidence.Event("Replica", "replica.status", "Applied", state.Removed ? "Removed" : "Upserted",
                    state.SourceEventId, state.SourcePlayerId, state.TargetEntityId, state, previous, state, state.RootEventId, state.ParentEventId, server: batch.ServerSequence);
                StatusChanged?.Invoke(state);
            }

            ConfirmedKill[] kills = batch.ConfirmedKills ?? Array.Empty<ConfirmedKill>();
            for (int i = 0; i < kills.Length; i++)
            {
                if (confirmedKills.Add((kills[i].TargetEntityId, kills[i].TargetStateVersion)))
                    KillConfirmed?.Invoke(kills[i]);
            }
        }

        private void EvidenceCore_Clear()
        {
            entities.Clear();
            foreach (var controller in statusControllers.Values) CombatEvidence.Unbind(controller);
            statusControllers.Clear();
            statusTargets.Clear();
            canonicalStatuses.Clear();
            confirmedKills.Clear();
            EntityChanged = null;
            StatusChanged = null;
            KillConfirmed = null;
        }
    }
}
