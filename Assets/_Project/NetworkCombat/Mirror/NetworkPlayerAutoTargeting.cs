using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(PlayerMovement))]
    [RequireComponent(typeof(NearestEnemyTargetProvider))]
    public sealed class NetworkPlayerAutoTargeting : NetworkBehaviour
    {
        [SerializeField] private PlayerMovement playerMovement;
        [SerializeField] private NearestEnemyTargetProvider targetProvider;
        [SerializeField, Min(0.1f)] private float targetRange = 30f;

        public CombatantBehaviour CurrentTarget { get; private set; }

        public float TargetRange => targetRange;

        private void Awake()
        {
            ResolveReferences();
            enabled = false;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            enabled = isOwned;
        }

        public override void OnStartAuthority()
        {
            base.OnStartAuthority();
            enabled = true;
        }

        public override void OnStopAuthority()
        {
            CurrentTarget = null;
            enabled = false;
            base.OnStopAuthority();
        }

        private void Update()
        {
            if (!isOwned)
            {
                return;
            }

            ResolveReferences();
            if (targetProvider.TryGetNearest(
                transform.position,
                targetRange,
                out CombatantBehaviour target,
                out Vector2 direction))
            {
                CurrentTarget = target;
                playerMovement.SetRuntimeAimDirection(direction);
            }
            else
            {
                CurrentTarget = null;
            }
        }

        private void ResolveReferences()
        {
            if (playerMovement == null)
            {
                playerMovement = GetComponent<PlayerMovement>();
            }
            if (targetProvider == null)
            {
                targetProvider = GetComponent<NearestEnemyTargetProvider>();
            }
            if (targetProvider.Owner == null)
            {
                targetProvider.Configure(GetComponent<CombatTeamBehaviour>());
            }
        }
    }
}
