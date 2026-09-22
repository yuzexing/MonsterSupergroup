using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationWorld
    {
        [Server]
        public bool ServerRedirectAllure(uint enemyId, uint targetPlayer, uint caster = 0)
        {
            if (BootGameplayNetworkManager.CombatHasEnded || !TryGetEligiblePlayer(targetPlayer, out _) ||
                !enemies.TryGetValue(enemyId, out var enemy) || enemy == null || !IsServerEnemyAlive(enemyId)) return false;
            var state = Registry.SetAggroTarget(enemyId, targetPlayer, caster != 0 ? caster : targetPlayer);
            GetHandoff(enemyId).CancelPending();
            enemy.SetServerTarget(state);
            return true;
        }

        [Server]
        public bool ServerApplyAllureDecoy(uint enemyId, uint caster, ulong cast, Vector2 position, double expires)
        {
            if (BootGameplayNetworkManager.CombatHasEnded || cast == 0 || expires <= NetworkTime.time ||
                !TryGetEligiblePlayer(caster, out _) || !enemies.TryGetValue(enemyId, out var enemy) || enemy == null ||
                !IsServerEnemyAlive(enemyId)) return false;
            enemy.SetServerTarget(Registry.SetDecoyTarget(enemyId, caster, cast, position, expires));
            GetHandoff(enemyId).CancelPending();
            return true;
        }

        /// <summary>Zero cast clears this owner's current decoys; timed expiry always supplies its exact cast.</summary>
        [Server]
        public void ServerClearAllureDecoy(uint caster, ulong cast)
        {
            foreach (var pair in enemies)
            {
                var enemy = pair.Value;
                if (enemy == null || !Registry.TryGetTargetState(pair.Key, out var state) || !state.HasDecoy ||
                    state.DecoyOwnerPlayerId != caster || (cast != 0 && state.DecoyCastId != cast)) continue;
                if (Registry.ClearDecoyTarget(pair.Key, caster, state.DecoyCastId, out var cleared)) enemy.SetServerTarget(cleared);
            }
        }

        [Server]
        public void ServerClearAllureEffects()
        {
            foreach (var pair in enemies)
            {
                var enemy = pair.Value;
                if (enemy == null || !Registry.TryGetTargetState(pair.Key, out var state) || !state.HasDecoy) continue;
                if (Registry.ClearDecoyTarget(pair.Key, state.DecoyOwnerPlayerId, state.DecoyCastId, out var cleared))
                    enemy.SetServerTarget(cleared);
            }
            // R/T persists. Pending transfers still reach their target screen after
            // shutdown instead of stranding the old simulator.
        }

        private void RefreshAllureDecoy(NetworkEnemySimulationAgent enemy, double now)
        {
            var target = enemy.TargetState;
            if (!target.HasDecoy || (target.DecoyExpiresAt > now &&
                NetworkCombatWorld.Instance != null && NetworkCombatWorld.Instance.PrototypesEnabled &&
                TryGetEligiblePlayer(target.DecoyOwnerPlayerId, out _))) return;
            if (Registry.ClearDecoyTarget(enemy.netId, target.DecoyOwnerPlayerId, target.DecoyCastId, out var cleared))
                enemy.SetServerTarget(cleared);
        }

        private bool CanCompleteAllureHandoff(NetworkEnemySimulationAgent enemy) =>
            !enemy.TargetState.HasDecoy && IsInPlayerView(enemy.Assignment.AggroTargetPlayerId, enemy);
    }
}
