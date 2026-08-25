using System;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    public enum CombatTeam
    {
        Neutral = 0,
        Player = 1,
        Enemy = 2
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(CombatantBehaviour))]
    public sealed class CombatTeamBehaviour : MonoBehaviour
    {
        private static readonly List<CombatTeamBehaviour> activeTeams =
            new List<CombatTeamBehaviour>();
        [SerializeField] private CombatTeam team = CombatTeam.Neutral;
        [SerializeField] private CombatantBehaviour combatant;

        public CombatTeam Team => team;

        public static IReadOnlyList<CombatTeamBehaviour> ActiveTeams => activeTeams;

        public CombatantBehaviour Combatant
        {
            get
            {
                if (combatant == null)
                {
                    combatant = GetComponent<CombatantBehaviour>();
                }

                return combatant;
            }
        }

        public void Configure(CombatTeam newTeam, CombatantBehaviour newCombatant)
        {
            if (newTeam == CombatTeam.Neutral)
            {
                throw new ArgumentOutOfRangeException(nameof(newTeam));
            }

            team = newTeam;
            combatant = newCombatant ?? throw new ArgumentNullException(nameof(newCombatant));
        }

        private void OnEnable()
        {
            if (!activeTeams.Contains(this))
            {
                activeTeams.Add(this);
            }
        }

        private void OnDisable()
        {
            activeTeams.Remove(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry()
        {
            activeTeams.Clear();
        }
    }
}
