using MonsterSupergroup.Gameplay.Combat;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MirrorNetworkCombatBridge))]
    public sealed class NetworkWeaponCombatAdapter : NetworkBehaviour
    {
        [SerializeField] private WeaponRuntimeBehaviour weapon;
        [SerializeField] private PlayerHandBehaviour playerHand;
        [SerializeField] private MirrorNetworkCombatBridge bridge;
        [SerializeField] private CombatRuntimeServiceProvider serviceProvider;

        private void Awake()
        {
            if (bridge == null)
            {
                bridge = GetComponent<MirrorNetworkCombatBridge>();
            }

            if (weapon == null)
            {
                weapon = GetComponentInChildren<WeaponRuntimeBehaviour>(true);
            }

            if (playerHand == null)
            {
                playerHand = GetComponentInChildren<PlayerHandBehaviour>(true);
            }

            if (serviceProvider == null)
            {
                serviceProvider = GetComponent<CombatRuntimeServiceProvider>();
            }

            if (serviceProvider == null)
            {
                serviceProvider = gameObject.AddComponent<CombatRuntimeServiceProvider>();
            }
        }

        public override void OnStartAuthority()
        {
            base.OnStartAuthority();
            bridge.OwnerCollectorReady += HandleCollectorReady;
            if (bridge.Collector != null)
            {
                HandleCollectorReady(bridge.Collector, bridge.EventIds);
            }
        }

        public override void OnStopAuthority()
        {
            if (bridge != null)
            {
                bridge.OwnerCollectorReady -= HandleCollectorReady;
            }

            base.OnStopAuthority();
        }

        private void HandleCollectorReady(
            ClientCombatCollector collector,
            MonsterSupergroup.GAS.ICombatEventIdSource eventIds)
        {
            var services = new CombatRuntimeServices(
                bridge.OwnerPlayerId,
                bridge.SourceEntityId,
                eventIds,
                collector);
            serviceProvider.Configure(services);
            if (playerHand != null)
            {
                playerHand.ConfigureCombatRuntimeServices(services);
            }

            if (weapon == null)
            {
                if (playerHand == null)
                {
                    Debug.LogError(
                        "NetworkWeaponCombatAdapter requires a WeaponRuntimeBehaviour or PlayerHandBehaviour.",
                        this);
                }

                return;
            }

            services.Configure(weapon);
            weapon.Initialize(
                randomSource: null,
                eventIdSource: eventIds,
                eventSink: collector,
                triggerGuard: services.TriggerGuard,
                timeSource: services.TimeSource);
        }
    }
}
