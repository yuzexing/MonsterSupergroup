using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Client-side queryable replica of server canonical shared facts.</summary>
    public sealed class CanonicalWorldReplica
    {
        private readonly Dictionary<uint, CanonicalEntityState> entities =
            new Dictionary<uint, CanonicalEntityState>();
        private readonly Dictionary<uint, StatusController> statusControllers =
            new Dictionary<uint, StatusController>();
        private readonly Dictionary<StatusInstanceId, uint> statusTargets =
            new Dictionary<StatusInstanceId, uint>();
        private readonly Dictionary<StatusInstanceId, CanonicalStatusState> canonicalStatuses =
            new Dictionary<StatusInstanceId, CanonicalStatusState>();

        public event Action<CanonicalEntityState> EntityChanged;
        public event Action<CanonicalStatusState> StatusChanged;
        public event Action<ConfirmedKill> KillConfirmed;

        public void RegisterStatusController(uint entityId, StatusController controller)
        {
            if (entityId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityId));
            }

            statusControllers[entityId] = controller ??
                throw new ArgumentNullException(nameof(controller));

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
        }

        public bool UnregisterStatusController(uint entityId, StatusController controller)
        {
            return statusControllers.TryGetValue(entityId, out StatusController registered) &&
                ReferenceEquals(registered, controller) &&
                statusControllers.Remove(entityId);
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

        public void Apply(CanonicalWorldBatch batch)
        {
            CanonicalEntityState[] entityStates =
                batch.Entities ?? Array.Empty<CanonicalEntityState>();
            for (int i = 0; i < entityStates.Length; i++)
            {
                CanonicalEntityState incoming = entityStates[i];
                if (entities.TryGetValue(incoming.EntityId, out CanonicalEntityState current) &&
                    current.StateVersion > incoming.StateVersion)
                {
                    continue;
                }

                entities[incoming.EntityId] = incoming;
                EntityChanged?.Invoke(incoming);
            }

            CanonicalStatusState[] statuses =
                batch.Statuses ?? Array.Empty<CanonicalStatusState>();
            for (int i = 0; i < statuses.Length; i++)
            {
                CanonicalStatusState state = statuses[i];
                var instanceId = new StatusInstanceId(state.InstanceId);
                if (state.Removed)
                {
                    if (statusTargets.TryGetValue(instanceId, out uint targetId) &&
                        statusControllers.TryGetValue(targetId, out StatusController controller))
                    {
                        controller.RemoveCanonical(instanceId, state.Version);
                    }

                    statusTargets.Remove(instanceId);
                    canonicalStatuses.Remove(instanceId);
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

                StatusChanged?.Invoke(state);
            }

            ConfirmedKill[] kills = batch.ConfirmedKills ?? Array.Empty<ConfirmedKill>();
            for (int i = 0; i < kills.Length; i++)
            {
                KillConfirmed?.Invoke(kills[i]);
            }
        }

        public void Clear()
        {
            entities.Clear();
            statusControllers.Clear();
            statusTargets.Clear();
            canonicalStatuses.Clear();
            EntityChanged = null;
            StatusChanged = null;
            KillConfirmed = null;
        }
    }
}
