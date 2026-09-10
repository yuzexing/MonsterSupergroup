using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationAgent
    {
        private readonly EnemyKnockbackCommandHistory knockbackHistory = new EnemyKnockbackCommandHistory();
        private readonly ProcessedEventCache ordinaryHitHistory = new ProcessedEventCache(4096, 120);
        private KnockbackSettings activeKnockbackPreset;
        private uint activeKnockbackEpoch;
        public int AppliedUltimateKnockbackCount { get; private set; }
        public int RejectedUltimateKnockbackCount { get; private set; }
        public int RequestedOrdinaryKnockbackCount { get; private set; }
        public int AppliedOrdinaryKnockbackCount { get; private set; }
        public int RejectedOrdinaryKnockbackCount { get; private set; }
        public bool HasActiveNetworkKnockback => enemyController != null && enemyController.IsNetworkKnockbackActive;

        internal bool TryApplyKnockback(EnemyKnockbackCommand command, uint receiverPlayerId, bool serverLocal)
        {
            bool localSimulator = serverLocal ? isServer : isClient && NetworkClient.localPlayer != null &&
                NetworkClient.localPlayer.netId == receiverPlayerId;
            if (!isActiveAndEnabled || !localSimulator || authority == null || !authority.RunsNavigation ||
                enemyController == null || !productEnemyInitialized ||
                !knockbackHistory.TryAccept(command, assignment, receiverPlayerId, serverLocal, NetworkTime.time))
            {
                if (command.Kind == EnemyKnockbackKind.OrdinaryHit) RejectedOrdinaryKnockbackCount++;
                else RejectedUltimateKnockbackCount++;
                return false;
            }
            if (command.Kind == EnemyKnockbackKind.OrdinaryHit &&
                !ordinaryHitHistory.MarkProcessed(command.DamageEventId, NetworkTime.time))
            { RejectedOrdinaryKnockbackCount++; return false; }
            // The original Enemy rejects overlapping knockbacks. Consuming its command identity
            // preserves that decision; a duplicate must not apply later after recovery.
            if (!IsCanonicalAlive || HasActiveNetworkKnockback) return false;
            ReleaseNetworkKnockbackPreset();
            var preset = command.Settings.CreateRuntimePreset();
            try
            {
                bool applied = command.Kind == EnemyKnockbackKind.OrdinaryHit
                    ? enemyController.TryApplyNetworkHitKnockback(command.Origin, preset, command.MultiplierSum)
                    : enemyController.TryApplyNetworkKnockback(command.Origin, preset);
                if (!applied)
                {
                    Destroy(preset);
                    return false;
                }
                activeKnockbackPreset = preset;
                activeKnockbackEpoch = command.AssignmentEpoch;
                if (command.Kind == EnemyKnockbackKind.OrdinaryHit) AppliedOrdinaryKnockbackCount++;
                else AppliedUltimateKnockbackCount++;
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
            if (activeKnockbackEpoch != 0 && !HasActiveNetworkKnockback) ReleaseNetworkKnockbackPreset();
        }

        private void CancelNetworkKnockbackState(bool clearHistory = false)
        {
            enemyController?.CancelNetworkKnockback();
            ReleaseNetworkKnockbackPreset();
            if (clearHistory) { knockbackHistory.Clear(); ordinaryHitHistory.Clear(); }
        }

        private void ReleaseNetworkKnockbackPreset()
        {
            activeKnockbackEpoch = 0;
            if (activeKnockbackPreset == null) return;
            if (Application.isPlaying) Destroy(activeKnockbackPreset);
            else DestroyImmediate(activeKnockbackPreset);
            activeKnockbackPreset = null;
        }

        private void OnEnable()
        {
            if (enemyController != null) enemyController.NativeHitKnockbackRequested += HandleNativeHitKnockback;
        }

        private void OnDisable()
        {
            if (enemyController != null) enemyController.NativeHitKnockbackRequested -= HandleNativeHitKnockback;
            CancelNetworkKnockbackState();
            NetworkEnemySimulationWorld.Instance?.ForgetPendingKnockback(netId);
        }

        private void HandleNativeHitKnockback(NativeGasHit hit, CombatResolution resolution)
        {
            var owner = NetworkClient.localPlayer;
            var context = resolution.DamageContext;
            if (!isClient || owner == null || context.SourcePlayerId != owner.netId || context.TargetEntityId != netId ||
                context.AbilityId == 0 || (context.AbilityId & 0x80000000u) != 0 || resolution.IsPredictedLethal ||
                resolution.PredictedAppliedDamage.Value <= 0 || !IsCanonicalAlive || assignment.Host == EnemySimulationHost.Frozen ||
                hit.KnockbackPresentation == null || (!hit.KnockbackPresentation.HasKnockback && !hit.KnockbackPresentation.Staggers)) return;
            var report = new OrdinaryHitKnockback
            {
                Requested = true, AssignmentEpoch = assignment.Epoch, Origin = hit.AttackPosition,
                HitNetworkTime = NetworkTime.time, MultiplierSum = hit.Attack.Stats.KnockbackMultiplierSum
            };
            var collector = owner.GetComponent<MirrorNetworkCombatBridge>()?.Collector;
            if (collector == null || !collector.TryAttachKnockback(context.EventId.Value, netId, report)) return;
            RequestedOrdinaryKnockbackCount++;

            // The assigned simulator never waits for its own result to cross Mirror, including a Host server body.
            bool localAssignment = assignment.Host == EnemySimulationHost.ClientPlayer
                ? assignment.SimulationOwnerPlayerId == owner.netId
                : isServer && (assignment.Host == EnemySimulationHost.ServerFallback || assignment.Host == EnemySimulationHost.ServerAuthoritative);
            if (!localAssignment || authority == null || !authority.RunsNavigation || !productEnemyInitialized ||
                !ordinaryHitHistory.MarkProcessed(context.EventId.Value, NetworkTime.time)) return;
            if (HasActiveNetworkKnockback) return;
            ReleaseNetworkKnockbackPreset();
            if (!enemyController.TryApplyNetworkHitKnockback(hit.AttackPosition, hit.KnockbackPresentation, report.MultiplierSum)) return;
            activeKnockbackEpoch = assignment.Epoch;
            AppliedOrdinaryKnockbackCount++;
        }
    }
}
