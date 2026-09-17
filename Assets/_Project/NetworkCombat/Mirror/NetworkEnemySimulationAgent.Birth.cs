using System;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.AI;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Spawn attributes only. Current health remains in Combatant/GAS and the canonical ledger.</summary>
    [Serializable]
    public struct EnemyBirthParameters
    {
        public bool Enabled;
        public string SourceEnemy;
        public int Variant, ClipIndex, Health, Damage;
        public float Speed, SpeedMultiplier, Xp, Knockback, Stun, Wind, ContactRadius;
        public double BornAt, ExpiresAt;
        public bool Counted, ResetOnReposition;

        public void Apply(EnemyStats stats)
        {
            stats.Init(new EnemyStatsValues { Health = Health, Damage = Damage, Speed = Speed, XP = Xp,
                KnockBackMultiplier = Knockback, StunTime = Stun, WindMultiplier = Wind });
            stats.SpeedMultiplier = SpeedMultiplier;
        }
    }

    public sealed partial class NetworkEnemySimulationAgent
    {
        [SyncVar] private EnemyBirthParameters birth;
        [SyncVar(hook = nameof(HandleReferenceReset))] private uint referenceResetVersion;
        public uint ReferenceResetVersion => referenceResetVersion;
        private bool birthPrepared;
        [SerializeField] private EnemyDatabase referenceArtDatabase;
        public EnemyBirthParameters Birth => birth;

        public void ConfigureBirth(EnemyBirthParameters value)
        {
            if (netId != 0 || !value.Enabled || value.Health < 1 || value.SpeedMultiplier <= 0)
                throw new InvalidOperationException("Configure valid birth attributes before NetworkServer.Spawn.");
            birth = value;
            PrepareBirthForRegistration();
        }

        // Also called by the combat adapter, before its first registration on either peer.
        internal void PrepareBirthForRegistration()
        {
            if (!birth.Enabled || birthPrepared || enemyController == null) return;
            birth.Apply(enemyController.stats);
            if (referenceResetVersion != 0) enemyController.stats.SpeedMultiplier = 1;
            enemyController.selectedName = birth.SourceEnemy;
            enemyController.allowRubberband = false; // Reference spawner decides; legacy AI must not also reposition.
            enemyController.CombatantBinding.InitializeFromStats(enemyController.stats);
            if (contactDamage == null) contactDamage = GetComponent<EnemyContactDamage>();
            if (contactDamage != null)
            {
                contactDamage.SetContactEnabled(birth.ContactRadius > 0);
                if (contactDamage.DamageInteraction != null)
                    foreach (var circle in contactDamage.DamageInteraction.GetComponentsInChildren<CircleCollider2D>(true))
                    { circle.radius = birth.ContactRadius; circle.offset = Vector2.zero; }
                contactDamage.Bind(enemyController);
            }
            birthPrepared = true;
        }

        private void ApplyBirthAfterReset(EnemyStats stats)
        {
            if (birth.Enabled) { birth.Apply(stats); if (referenceResetVersion != 0) stats.SpeedMultiplier = 1; }
        }

        [Server]
        internal void ResetReferenceCondition()
        {
            if (!birth.Enabled || !birth.ResetOnReposition) return;
            referenceResetVersion++;
            ApplyReferenceReset();
        }
        private void HandleReferenceReset(uint previous, uint current) { if (current != 0) ApplyReferenceReset(); }
        private void ApplyReferenceReset()
        {
            if (!ProductEnemyInitialized) return;
            enemyController.ResetEnemyCondition();
            if (enemyController.attackScript is EnemyAttackDash dash) dash.BindCurrentStats();
            RestoreCanonicalAfterBirthInitialization();
        }

        private void RestoreCanonicalAfterBirthInitialization()
        {
            if (birth.Enabled && referenceArtDatabase != null && enemyController != null &&
                !MonsterSupergroup.Gameplay.Combat.GameplayRuntimeEnvironment.IsDedicatedServer)
                enemyController.enemyAnimator?.Recolor(referenceArtDatabase.GetEnemyData(birth.SourceEnemy, birth.Variant)?.ColorLUT);
            if (birth.Enabled && enemyController != null) enemyController.allowRubberband = false;
            if (!birth.Enabled || combatant == null || NetworkCombatWorld.Instance == null) return;
            var world = NetworkCombatWorld.Instance;
            if (isServer && world.Gateway.Ledger.TryGetState(netId, out var serverState))
                combatant.ApplyCanonicalHealth(serverState.Health, serverState.MaxHealth, serverState.StateVersion);
            else if (world.Replica.TryGetEntity(netId, out var clientState))
                combatant.ApplyCanonicalHealth(clientState.Health, clientState.MaxHealth, clientState.StateVersion);
        }

        private void RefreshReferenceReplicaMovement()
        {
            if (!birth.Enabled || referenceArtDatabase == null || !productEnemyInitialized || !IsCanonicalAlive ||
                authority == null || (!productMovementOnly && !authority.ConsumesSnapshots) || resolvedTarget == null) return;
            if (enemyController.DeathRequested || enemyController.IsInKnockbackState || enemyController.IsNetworkKnockbackActive) return;
            if (hasLatestAttackPresentation)
            {
                var phase = latestAttackPresentation.Checkpoint.Movement.Runtime.Action.PhaseAt(EnemySimulationClock.CombatNow);
                if (phase == EnemyAttackPresentationPhase.Warning || phase == EnemyAttackPresentationPhase.Active ||
                    phase == EnemyAttackPresentationPhase.Recovery) return;
            }
            Vector2 facing = (Vector2)resolvedTarget.position - (Vector2)transform.position;
            if (authority.ConsumesSnapshots) enemyController.Movement?.SetFacingDirection(facing);
            enemyController.enemyAnimator.Movement(facing.x, facing.y);
        }
    }
}
