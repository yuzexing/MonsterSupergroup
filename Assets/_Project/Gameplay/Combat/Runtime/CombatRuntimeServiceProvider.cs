using System;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    /// <summary>
    /// Shared owner-side combat services for both native GAS weapons and the
    /// HellMaiden compatibility layer. Network code configures this component;
    /// offline play lazily receives a local, non-replicating service set.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatRuntimeServiceProvider : MonoBehaviour
    {
        [SerializeField, Min(1)] private uint offlinePlayerId = 1;

        private CombatRuntimeServices services;

        public event Action<CombatRuntimeServices> ServicesChanged;

        public bool IsNetworkConfigured { get; private set; }

        public CombatRuntimeServices Services => services ?? CreateOfflineServices();

        public void Configure(CombatRuntimeServices runtimeServices)
        {
            services = runtimeServices ??
                throw new ArgumentNullException(nameof(runtimeServices));
            IsNetworkConfigured = true;
            ServicesChanged?.Invoke(services);
        }

        private CombatRuntimeServices CreateOfflineServices()
        {
            uint entityId = unchecked((uint)GetInstanceID());
            if (entityId == 0u)
            {
                entityId = 1u;
            }

            ushort sourceSlot = unchecked((ushort)entityId);
            if (sourceSlot == 0)
            {
                sourceSlot = 1;
            }

            services = new CombatRuntimeServices(
                Math.Max(1u, offlinePlayerId),
                entityId,
                new SequentialCombatEventIdSource(sourceSlot),
                NullCombatEventSink.Instance);
            ServicesChanged?.Invoke(services);
            return services;
        }

        private void OnDestroy()
        {
            ServicesChanged = null;
            services = null;
        }
    }
}
