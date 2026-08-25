using System;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    [DisallowMultipleComponent]
    public sealed class NearestEnemyTargetProvider : MonoBehaviour
    {
        [SerializeField] private CombatTeamBehaviour owner;

        public CombatTeamBehaviour Owner => owner;

        public void Configure(CombatTeamBehaviour newOwner)
        {
            owner = newOwner ?? throw new ArgumentNullException(nameof(newOwner));
        }

        public bool TryGetNearest(
            Vector2 origin,
            float range,
            out CombatantBehaviour target,
            out Vector2 direction)
        {
            if (owner == null)
            {
                throw new InvalidOperationException("NearestEnemyTargetProvider requires an owner team.");
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
                if (candidate == null || candidate == owner ||
                    candidate.Team == CombatTeam.Neutral || candidate.Team == owner.Team)
                {
                    continue;
                }

                CombatantBehaviour combatant = candidate.Combatant;
                if (combatant == null || !combatant.IsAlive)
                {
                    continue;
                }

                float distanceSquared = ((Vector2)candidate.transform.position - origin).sqrMagnitude;
                int instanceId = candidate.GetInstanceID();
                if (distanceSquared > bestDistanceSquared ||
                    (Mathf.Approximately(distanceSquared, bestDistanceSquared) && instanceId >= bestInstanceId))
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
            direction = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector2.right;
            return true;
        }
    }
}
