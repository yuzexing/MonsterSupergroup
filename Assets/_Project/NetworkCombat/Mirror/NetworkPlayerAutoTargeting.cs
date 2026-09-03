using System;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(PlayerMovement))]
    [RequireComponent(typeof(CombatTeamBehaviour))]
    public sealed class NetworkPlayerAutoTargeting : NetworkBehaviour
    {
        [SerializeField] private PlayerMovement playerMovement;
        [SerializeField] private CombatTeamBehaviour ownerTeam;
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
            if (TryGetNearest(
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
            if (ownerTeam == null)
            {
                ownerTeam = GetComponent<CombatTeamBehaviour>();
            }
        }

        private bool TryGetNearest(
            Vector2 origin,
            float range,
            out CombatantBehaviour target,
            out Vector2 direction)
        {
            if (ownerTeam == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(NetworkPlayerAutoTargeting)} requires an owner team.");
            }

            if (range <= 0f || float.IsNaN(range) || float.IsInfinity(range))
            {
                throw new ArgumentOutOfRangeException(nameof(range));
            }

            var candidates = CombatTeamBehaviour.ActiveTeams;
            float bestDistanceSquared = range * range;
            int bestInstanceId = int.MaxValue;
            target = null;

            for (int i = 0; i < candidates.Count; i++)
            {
                CombatTeamBehaviour candidate = candidates[i];
                if (candidate == null || candidate == ownerTeam ||
                    candidate.Team == CombatTeam.Neutral ||
                    candidate.Team == ownerTeam.Team)
                {
                    continue;
                }

                CombatantBehaviour combatant = candidate.Combatant;
                if (combatant == null || !combatant.IsAlive)
                {
                    continue;
                }

                float distanceSquared =
                    ((Vector2)candidate.transform.position - origin).sqrMagnitude;
                int instanceId = candidate.GetInstanceID();
                if (distanceSquared > bestDistanceSquared ||
                    (Mathf.Approximately(distanceSquared, bestDistanceSquared) &&
                     instanceId >= bestInstanceId))
                {
                    continue;
                }

                bestDistanceSquared = distanceSquared;
                bestInstanceId = instanceId;
                target = combatant;
            }

            if (target == null)
            {
                direction = Vector2.zero;
                return false;
            }

            direction = (Vector2)target.transform.position - origin;
            direction = direction.sqrMagnitude > 0.000001f
                ? direction.normalized
                : Vector2.right;
            return true;
        }
    }
}
