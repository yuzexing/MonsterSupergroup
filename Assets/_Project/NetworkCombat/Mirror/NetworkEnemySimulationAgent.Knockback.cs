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
        private readonly EnemyPredictedKnockbackHistory predictedKnockbacks = new EnemyPredictedKnockbackHistory();
        private KnockbackSettings activeKnockbackPreset;
        private uint activeKnockbackEpoch;
        private ulong activeKnockbackCommandId, activeKnockbackDamageEventId;
        public int AppliedUltimateKnockbackCount { get; private set; }
        public int RejectedUltimateKnockbackCount { get; private set; }
        public int RequestedOrdinaryKnockbackCount { get; private set; }
        public int AppliedOrdinaryKnockbackCount { get; private set; }
        public int RejectedOrdinaryKnockbackCount { get; private set; }
        public bool HasActiveNetworkKnockback => enemyController != null && enemyController.IsNetworkKnockbackActive;

        internal bool TryApplyKnockback(EnemyKnockbackCommand command, uint receiverPlayerId, bool serverLocal)
        {
#if MONSTER_ENEMY_HANDOFF_VALIDATION
            Debug.Log($"[EnemyHandoffImpulse] apply enemy={netId} role={authority?.Role} product={productEnemyInitialized} epoch={assignment.Epoch}/{command.AssignmentEpoch} last={knockbackHistory.LastCommandId} command={command.CommandId} alive={IsCanonicalAlive}/{enemyController?.IsAlive} immune={enemyController?.IsImmune} active={HasActiveNetworkKnockback}");
#endif
            bool localSimulator = serverLocal ? isServer : isClient && NetworkClient.localPlayer != null &&
                NetworkClient.localPlayer.netId == receiverPlayerId;
            if (!isActiveAndEnabled || !localSimulator || authority == null || !authority.RunsNavigation ||
                enemyController == null || !productEnemyInitialized ||
                !knockbackHistory.TryAccept(command, assignment, receiverPlayerId, serverLocal, EnemySimulationClock.Now))
            {
                if (command.Kind == EnemyKnockbackKind.OrdinaryHit) RejectedOrdinaryKnockbackCount++;
                else RejectedUltimateKnockbackCount++;
                return false;
            }
            if (command.Kind == EnemyKnockbackKind.OrdinaryHit) predictedKnockbacks.Acknowledge(command.DamageEventId);
            if (command.Kind == EnemyKnockbackKind.OrdinaryHit &&
                !ordinaryHitHistory.MarkProcessed(command.DamageEventId, EnemySimulationClock.Now))
            {
                if (activeKnockbackDamageEventId == command.DamageEventId) activeKnockbackCommandId = command.CommandId;
                RejectedOrdinaryKnockbackCount++; return false;
            }
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
#if MONSTER_ENEMY_HANDOFF_VALIDATION
                Debug.Log($"[EnemyHandoffImpulse] native result={applied} active={HasActiveNetworkKnockback} movement={enemyController.Movement != null} inKnockback={enemyController.IsInKnockbackState} override={enemyController.attackScript?.OverrideKnockback}");
#endif
                if (!applied)
                {
                    Destroy(preset);
                    return false;
                }
                activeKnockbackPreset = preset;
                activeKnockbackEpoch = command.AssignmentEpoch;
                activeKnockbackCommandId = command.CommandId;
                activeKnockbackDamageEventId = command.DamageEventId;
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
            if (clearHistory) { knockbackHistory.Clear(); ordinaryHitHistory.Clear(); predictedKnockbacks.Clear(); }
        }

        private void ReleaseNetworkKnockbackPreset()
        {
            activeKnockbackEpoch = 0;
            activeKnockbackCommandId = activeKnockbackDamageEventId = 0;
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
                HitNetworkTime = EnemySimulationClock.Now, MultiplierSum = hit.Attack.Stats.KnockbackMultiplierSum
            };
            var collector = owner.GetComponent<MirrorNetworkCombatBridge>()?.Collector;
            if (collector == null || !collector.TryAttachKnockback(context.EventId.Value, netId, report)) return;
            RequestedOrdinaryKnockbackCount++;

            // The assigned simulator never waits for its own result to cross Mirror, including a Host server body.
            bool localAssignment = assignment.Host == EnemySimulationHost.ClientPlayer
                ? assignment.SimulationOwnerPlayerId == owner.netId
                : isServer && (assignment.Host == EnemySimulationHost.ServerFallback || assignment.Host == EnemySimulationHost.ServerAuthoritative);
            if (!localAssignment || authority == null || !authority.RunsNavigation || !productEnemyInitialized) return;
            // At capacity leave execution to the confirmed command rather than lose a dedup receipt.
            if (!predictedKnockbacks.TryRemember(context.EventId.Value, EnemySimulationClock.Now)) return;
            if (!ordinaryHitHistory.MarkProcessed(context.EventId.Value, EnemySimulationClock.Now))
            { predictedKnockbacks.Acknowledge(context.EventId.Value); return; }
            if (HasActiveNetworkKnockback) return;
            ReleaseNetworkKnockbackPreset();
            var predictedPreset = EnemyKnockbackSettings.From(hit.KnockbackPresentation).CreateRuntimePreset();
            if (!enemyController.TryApplyNetworkHitKnockback(hit.AttackPosition, predictedPreset, report.MultiplierSum))
            { Destroy(predictedPreset); return; }
            activeKnockbackPreset = predictedPreset;
            activeKnockbackEpoch = assignment.Epoch;
            activeKnockbackDamageEventId = context.EventId.Value;
            AppliedOrdinaryKnockbackCount++;
            NetworkEnemySimulationWorld.Instance?.NotifyRuntimeChanged(this);
        }
    }
}
