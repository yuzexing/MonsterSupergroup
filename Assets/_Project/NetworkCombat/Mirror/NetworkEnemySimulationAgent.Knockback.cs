using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationAgent
    {
        private readonly EnemyKnockbackCommandHistory knockbackHistory = new EnemyKnockbackCommandHistory();
        private KnockbackSettings activeKnockbackPreset;
        private uint activeKnockbackEpoch;
        public int AppliedUltimateKnockbackCount { get; private set; }
        public int RejectedUltimateKnockbackCount { get; private set; }
        public bool HasActiveNetworkKnockback => enemyController != null && enemyController.IsNetworkKnockbackActive;

        internal bool TryApplyUltimateKnockback(EnemyKnockbackCommand command, uint receiverPlayerId, bool serverLocal)
        {
            bool localSimulator = serverLocal ? isServer : isClient && NetworkClient.localPlayer != null &&
                NetworkClient.localPlayer.netId == receiverPlayerId;
            if (!localSimulator || authority == null || !authority.RunsNavigation || !IsCanonicalAlive ||
                enemyController == null || !productEnemyInitialized ||
                !knockbackHistory.TryAccept(command, assignment, receiverPlayerId, serverLocal, NetworkTime.time))
            {
                RejectedUltimateKnockbackCount++;
                return false;
            }
            // The original Enemy rejects overlapping knockbacks. Consuming its command identity
            // preserves that decision; a duplicate must not apply later after recovery.
            if (HasActiveNetworkKnockback) return false;
            ReleaseNetworkKnockbackPreset();
            var preset = command.Settings.CreateRuntimePreset();
            try
            {
                if (!enemyController.TryApplyNetworkKnockback(command.Origin, preset))
                {
                    Destroy(preset);
                    return false;
                }
                activeKnockbackPreset = preset;
                activeKnockbackEpoch = command.AssignmentEpoch;
                AppliedUltimateKnockbackCount++;
                return true;
            }
            catch { Destroy(preset); throw; }
        }

        private void UpdateNetworkKnockbackState()
        {
            if (!IsCanonicalAlive || authority == null || !authority.RunsNavigation)
            {
                CancelNetworkKnockbackState();
                return;
            }
            if (activeKnockbackPreset != null && !HasActiveNetworkKnockback) ReleaseNetworkKnockbackPreset();
        }

        private void CancelNetworkKnockbackState(bool clearHistory = false)
        {
            enemyController?.CancelNetworkKnockback();
            ReleaseNetworkKnockbackPreset();
            if (clearHistory) knockbackHistory.Clear();
        }

        private void ReleaseNetworkKnockbackPreset()
        {
            activeKnockbackEpoch = 0;
            if (activeKnockbackPreset == null) return;
            if (Application.isPlaying) Destroy(activeKnockbackPreset);
            else DestroyImmediate(activeKnockbackPreset);
            activeKnockbackPreset = null;
        }

        private void OnDisable() => CancelNetworkKnockbackState();
    }
}
