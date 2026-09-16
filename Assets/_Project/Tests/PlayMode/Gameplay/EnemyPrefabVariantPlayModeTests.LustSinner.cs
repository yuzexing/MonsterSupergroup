using System.Collections;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class EnemyPrefabVariantPlayModeTests
    {
        [UnityTest]
        public IEnumerator LustSinnerWarningAndActiveKeepSeparateVisualAndDamageWindows()
        {
            var enemy = Spawn("NetworkEnemyLustSinner");
            yield return WaitFor(() => Ready(enemy));
            enemy.Movement.StopMovement();
            Assert.That(enemy.StateMachine, Is.Not.Null);
            Assert.That(enemy.CurrentHealth, Is.EqualTo(15));
            Assert.That(enemy.GetComponent<EnemyContactDamage>().IsActive, Is.False);
            var melee = enemy.GetComponent<EnemyAttackMelee>();
            var player = Owner.GetComponent<PlayerMovement>();
            var binding = Owner.GetComponent<PlayerCombatantBinding>();
            yield return WaitFor(() => !player.IsInvulnerable);
            enemy.Attack();
            yield return WaitFor(() => melee.HasSimulationAttackInstance);
            var area = enemy.GetComponentInChildren<EnemyAttackPrefab>();
            var collider = area.GetComponentInChildren<PolygonCollider2D>(true);
            Assert.That(area.damageInteraction.gameObject.activeInHierarchy, Is.False);
            Assert.That(area.attackWarning.GetComponentsInChildren<Renderer>().All(r => r.enabled), Is.True);
            float locked = area.transform.eulerAngles.z;
            int hp = binding.CurrentHealth;
            yield return new WaitForSeconds(.1f);
            Assert.That(binding.CurrentHealth, Is.EqualTo(hp));
            // Move the target after warning; the active area must keep its original direction.
            var point = collider.transform.TransformPoint(collider.GetPath(0).Aggregate(Vector2.zero, (a,b) => a+b) / collider.GetPath(0).Length);
            var probe = new GameObject("locally owned geometry probe", typeof(CircleCollider2D), typeof(Rigidbody2D), typeof(PlayerHitbox));
            probe.layer = LayerMask.NameToLayer("PlayerHitbox");
            probe.GetComponent<PlayerHitbox>().Configure(binding);
            probe.GetComponent<CircleCollider2D>().radius = .05f;
            probe.GetComponent<CircleCollider2D>().isTrigger = true;
            probe.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            probe.GetComponent<Rigidbody2D>().useFullKinematicContacts = true;
            probe.transform.position = point;
            probe.GetComponent<Rigidbody2D>().position = point;
            yield return WaitFor(() => enemy.GetComponent<MonsterSupergroup.NetworkCombat.NetworkEnemyMeleeReplica>().DamageWindowActive);
            Physics2D.SyncTransforms();
            Assert.That(collider.OverlapPoint(point), Is.True);
            Assert.That(collider.OverlapPoint(point + Vector3.one * 20), Is.False);
            Assert.That(area.transform.eulerAngles.z, Is.EqualTo(locked));
            Assert.That(area.attackWarning.GetComponentsInChildren<Renderer>().All(r => !r.enabled), Is.True);
            Assert.That(area.damageInteraction.gameObject.activeInHierarchy, Is.True);
            Assert.That(collider.attachedRigidbody.simulated, Is.True, "Enemy attack physics disabled.");
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate(); yield return null; yield return null;
            Object.Destroy(probe);
            Assert.That(binding.CurrentHealth, Is.EqualTo(hp - 5), "The real polygon trigger must apply source damage exactly once.");
            area.damageInteraction.Interact(Owner.GetComponentInChildren<PlayerHitbox>());
            yield return null; yield return null;
            Assert.That(binding.CurrentHealth, Is.EqualTo(hp - 5));
            melee.SuspendSimulation();
        }

        [UnityTest]
        public IEnumerator LustSinnerCancelAndPoolReuseDiscardPendingDamage()
        {
            var enemy = Spawn("NetworkEnemyLustSinner");
            yield return WaitFor(() => Ready(enemy));
            enemy.Movement.StopMovement();
            var player = Owner.GetComponent<PlayerMovement>();
            var binding = Owner.GetComponent<PlayerCombatantBinding>();
            yield return WaitFor(() => !player.IsInvulnerable);
            var melee = enemy.GetComponent<EnemyAttackMelee>();
            melee.AttackWarningEnter(); melee.AttackEnter();
            var area = enemy.GetComponentInChildren<EnemyAttackPrefab>();
            int hp = binding.CurrentHealth;
            area.damageInteraction.Interact(Owner.GetComponentInChildren<PlayerHitbox>());
            melee.CancelAttack();
            yield return null; yield return null;
            Assert.That(binding.CurrentHealth, Is.EqualTo(hp));
            Assert.That(melee.HasSimulationAttackInstance, Is.False);
            melee.AttackWarningEnter();
            var reused = enemy.GetComponentInChildren<EnemyAttackPrefab>();
            Assert.That(reused, Is.SameAs(area));
            Assert.That(reused.damageInteraction.gameObject.activeInHierarchy, Is.False);
            Assert.That(reused.attackWarning.GetComponentsInChildren<Renderer>().All(r => r.enabled), Is.True);
            melee.SuspendSimulation();
            enemy.Attack();
            yield return WaitFor(() => enemy.CurrentAttackPresentationPhase == AstralShift.HellMaiden.AI.EnemyAttackPresentationPhase.Active);
            enemy.GetComponentInChildren<EnemyAttackPrefab>().damageInteraction.Interact(Owner.GetComponentInChildren<PlayerHitbox>());
            enemy.Kill(instant: true, dropXp: false);
            yield return null; yield return null;
            Assert.That(binding.CurrentHealth, Is.EqualTo(hp));
            Assert.That(area == null || !area.gameObject.activeInHierarchy, Is.True);
        }

        [UnityTest]
        public IEnumerator LustSinnerDisableReleasesNativeAttackAndPendingCollisions()
        {
            var enemy = Spawn("NetworkEnemyLustSinner");
            yield return WaitFor(() => Ready(enemy));
            enemy.Movement.StopMovement();
            var binding = Owner.GetComponent<PlayerCombatantBinding>();
            yield return WaitFor(() => !Owner.GetComponent<PlayerMovement>().IsInvulnerable);
            enemy.Attack();
            yield return WaitFor(() => enemy.CurrentAttackPresentationPhase == AstralShift.HellMaiden.AI.EnemyAttackPresentationPhase.Active);
            var melee = enemy.GetComponent<EnemyAttackMelee>();
            int hp = binding.CurrentHealth;
            enemy.GetComponentInChildren<EnemyAttackPrefab>().damageInteraction.Interact(Owner.GetComponentInChildren<PlayerHitbox>());
            enemy.gameObject.SetActive(false);
            yield return null; yield return null;
            Assert.That(binding.CurrentHealth, Is.EqualTo(hp));
            Assert.That(melee.HasSimulationAttackInstance, Is.False);
        }

        [UnityTest]
        public IEnumerator LustSinnerInstancesHaveIndependentRuntimeState()
        {
            var a = Spawn("NetworkEnemyLustSinner");
            var b = Spawn("NetworkEnemyLustSinner");
            yield return WaitFor(() => Ready(a) && Ready(b));
            a.Movement.StopMovement(); b.Movement.StopMovement();
            Assert.That(a.stats, Is.Not.SameAs(b.stats));
            Assert.That(a.StateMachine, Is.Not.SameAs(b.StateMachine));
            Assert.That(a.status.Runtime, Is.Not.SameAs(b.status.Runtime));
            a.lastAttackTime = Time.time; a.Target = null;
            a.status.Apply(EnemyStatusID.Slow, .5f, 2);
            a.GetComponent<CombatantBehaviour>().ReceiveDamage(new DamageInfo(1, 7, false));
            Assert.That(a.CurrentHealth, Is.EqualTo(8));
            Assert.That(b.CurrentHealth, Is.EqualTo(15));
            Assert.That(b.Target, Is.Not.Null);
            Assert.That(b.lastAttackTime, Is.Not.EqualTo(a.lastAttackTime));
            Assert.That(b.status.HasAnyStatus(), Is.False);
        }
    }
}
