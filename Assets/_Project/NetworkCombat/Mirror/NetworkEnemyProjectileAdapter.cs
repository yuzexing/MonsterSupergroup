using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyProjectileAttack), typeof(NetworkEnemySimulationAgent))]
    public sealed class NetworkEnemyProjectileAdapter : MonoBehaviour, IEnemyProjectileExecution
    {
        private EnemyProjectileAttack attack;
        private EnemyController controller;
        private NetworkEnemySimulationAgent agent;
        private BulletProjectile charge;
        public EnemyProjectileAttack Attack => attack != null ? attack : GetComponent<EnemyProjectileAttack>();

        private void Awake()
        {
            attack = GetComponent<EnemyProjectileAttack>(); controller = GetComponent<EnemyController>();
            agent = GetComponent<NetworkEnemySimulationAgent>(); attack.NetworkExecution = this;
        }
        private void OnEnable()
        {
            if (agent == null) Awake();
            agent.AttackPresentationChanged += OnRemotePhase;
        }
        private void OnRemotePhase(EnemyAttackPresentationEdge edge)
        {
            if (agent.Authority == null || !agent.Authority.ConsumesSnapshots) return;
            if (edge.Phase == EnemyAttackPresentationPhase.Warning && !edge.IsExpiredAt(EnemySimulationClock.Now))
            {
                attack.AlignProjectileOrigin(edge.Facing);
                ShowCharge();
            }
            else CancelCharge();
        }
        public void ShowCharge()
        {
            CancelCharge();
            if (!NetworkClient.active || NetworkEnemySimulationWorld.Instance == null) return;
            charge = NetworkEnemySimulationWorld.Instance.BorrowEnemyBullet(agent.netIdentity.assetId, attack.bulletPrefab);
            if (charge != null)
            {
                charge.transform.position = attack.bulletPosition.position;
                charge.gameObject.SetActive(true);
                SetChargeCollision(false);
            }
        }
        private void SetChargeCollision(bool active)
        {
            foreach (var c in charge.GetComponentsInChildren<Collider2D>(true)) c.enabled = active;
            if (charge.damageInteraction != null) charge.damageInteraction.enabled = active;
        }
        private void LateUpdate()
        {
            if (charge == null) return;
            if (!agent.IsCanonicalAlive || !isActiveAndEnabled) { CancelCharge(); return; }
            charge.transform.position = attack.bulletPosition.position;
        }
        public void Launch(Vector2 direction)
        {
            CancelCharge();
            if (agent.Authority == null || !agent.Authority.RunsCombatDecisions || !agent.IsCanonicalAlive) return;
            var checkpoint = agent.CaptureCurrentCheckpoint();
            // AttackEnter runs before the controller publishes its Active edge.
            checkpoint.Movement.Runtime.Action.Phase = EnemyAttackPresentationPhase.Active;
            var launch = new EnemyProjectileLaunch {
                Key = new EnemyProjectileKey { EnemyEntityId = agent.netId, ActionId = checkpoint.Movement.Runtime.Action.ActionId },
                AssignmentEpoch = agent.Assignment.Epoch, EnemyPrefabAssetId = agent.netIdentity.assetId,
                Origin = attack.bulletPosition.position, Direction = direction.normalized,
                Speed = attack.bulletPrefab.speed, Lifetime = attack.bulletPrefab.duration,
                Damage = controller.stats.Damage, StunTime = controller.stats.StunTime, Checkpoint = checkpoint
            };
            NetworkEnemySimulationWorld.Instance?.EmitEnemyProjectile(agent, launch);
        }
        public void CancelCharge()
        {
            if (charge == null) return;
            var released = charge; charge = null;
            if (NetworkEnemySimulationWorld.Instance != null) NetworkEnemySimulationWorld.Instance.ReturnEnemyBullet(released);
            else Destroy(released.gameObject);
        }
        private void OnDisable()
        {
            if (agent != null) agent.AttackPresentationChanged -= OnRemotePhase;
            CancelCharge();
        }
        private void OnDestroy() { if (attack != null && ReferenceEquals(attack.NetworkExecution, this)) attack.NetworkExecution = null; }
    }
}
