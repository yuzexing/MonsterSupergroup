using System.Collections;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class EnemyPrefabVariantPlayModeTests
    {
        private EnemyProjectileLaunch ImpLaunch(EnemyController enemy, ulong action) => new EnemyProjectileLaunch {
            Key = new EnemyProjectileKey { EnemyEntityId = enemy.GetComponent<NetworkIdentity>().netId, ActionId = action },
            EnemyPrefabAssetId = enemy.GetComponent<NetworkIdentity>().assetId, AssignmentEpoch = 1,
            Origin = enemy.transform.position, Direction = Vector2.right, Speed = 6, Lifetime = 5, Damage = 5
        };

        [UnityTest]
        public IEnumerator ImpPredictsOnceAndHandoffDoesNotRefire()
        {
            var enemy = Spawn("NetworkEnemyImp"); yield return WaitFor(() => Ready(enemy));
            var world = NetworkEnemySimulationWorld.Instance;
            EnemyProjectileLaunch sent = default; BulletProjectile bullet = null;
            world.EnemyProjectilePresented += (value, instance) => { sent = value; bullet = instance; };
            enemy.Attack();
            Assert.That(world.PresentedProjectileCount, Is.Zero, "Warning cannot fire.");
            yield return WaitFor(() => bullet != null);
            enemy.attackDistance = 0;
            Assert.That(bullet.GetComponent<NetworkIdentity>(), Is.Null);
            yield return WaitFor(() => world.AcceptedProjectileCount == 1);
            Assert.That(world.PresentedProjectileCount, Is.EqualTo(1));
            Assert.That(world.PresentEnemyProjectile(sent), Is.False);
            Assert.That(world.TryAcceptEnemyProjectile(Owner.netId, sent), Is.False, "Duplicate submission.");
            var candidate = sent;
            candidate.Key.ActionId++;
            candidate.Checkpoint.Movement.Runtime.Action.ActionId = candidate.Key.ActionId;
            var invalid = candidate; invalid.AssignmentEpoch++;
            Assert.That(world.TryAcceptEnemyProjectile(Owner.netId, invalid), Is.False, "Old or future epoch.");
            invalid = candidate; invalid.Speed++;
            Assert.That(world.TryAcceptEnemyProjectile(Owner.netId, invalid), Is.False, "Wrong bullet configuration.");
            invalid = candidate; invalid.Damage++;
            Assert.That(world.TryAcceptEnemyProjectile(Owner.netId, invalid), Is.False, "Wrong damage configuration.");
            invalid = candidate; invalid.Origin.x = float.NaN;
            Assert.That(world.TryAcceptEnemyProjectile(Owner.netId, invalid), Is.False, "Non-finite launch.");
            Assert.That(world.TryAcceptEnemyProjectile(uint.MaxValue, candidate), Is.False, "Wrong owner.");
            Assert.That(world.AcceptedProjectileCount, Is.EqualTo(1));
            var state = enemy.CaptureSimulationAction(EnemySimulationClock.Now);
            Assert.That(state.ProjectileEmitted, Is.True);
            enemy.RestoreSimulationAction(state, EnemySimulationClock.Now);
            Assert.That(world.PresentedProjectileCount, Is.EqualTo(1));
            enemy.gameObject.SetActive(false);
            Assert.That(bullet.gameObject.activeInHierarchy, Is.True);
            var start = bullet.transform.position;
            yield return new WaitForSeconds(.15f);
            Assert.That(Vector3.Distance(start, bullet.transform.position), Is.GreaterThan(.5));
            Object.Destroy(bullet.gameObject);
            yield return null; yield return null;
            Assert.That(world.ActiveEnemyProjectileCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ImpHitConsumesOnceAndTerminalBeforeLaunchPreventsResurrection()
        {
            var enemy = Spawn("NetworkEnemyImp"); yield return WaitFor(() => Ready(enemy)); enemy.attackDistance = 0;
            var world = NetworkEnemySimulationWorld.Instance; BulletProjectile bullet = null;
            world.EnemyProjectilePresented += (_, instance) => bullet = instance;
            var binding = Owner.GetComponent<PlayerCombatantBinding>();
            yield return WaitFor(() => !Owner.GetComponent<PlayerMovement>().IsInvulnerable);
            int hp = binding.CurrentHealth;
            var launch = ImpLaunch(enemy, 9001);
            Assert.That(world.PresentEnemyProjectile(launch), Is.True);
            var hitbox = Owner.GetComponentInChildren<PlayerHitbox>();
            bullet.damageInteraction.Interact(hitbox); bullet.damageInteraction.Interact(hitbox);
            Assert.That(binding.CurrentHealth, Is.EqualTo(hp - 5));
            Assert.That(bullet.gameObject.activeInHierarchy, Is.False);
            Assert.That(world.PresentEnemyProjectile(launch), Is.False);
            var next = ImpLaunch(enemy, 9002);
            Assert.That(world.ApplyEnemyProjectileTermination(new EnemyProjectileTermination { Key = next.Key }), Is.True);
            Assert.That(world.PresentEnemyProjectile(next), Is.False);
            Assert.That(world.ApplyEnemyProjectileTermination(new EnemyProjectileTermination { Key = next.Key }), Is.False);
            var reuse = ImpLaunch(enemy, 9003); var old = bullet;
            Assert.That(world.PresentEnemyProjectile(reuse), Is.True);
            Assert.That(bullet, Is.SameAs(old));
            Assert.That(bullet.fired, Is.True); Assert.That(bullet.pierced, Is.Zero);
            world.ApplyEnemyProjectileTermination(new EnemyProjectileTermination { Key = reuse.Key });
        }

        [UnityTest]
        public IEnumerator ImpExpiryHasLocalLifetimeAndBroadcastsOneTermination()
        {
            var enemy = Spawn("NetworkEnemyImp"); yield return WaitFor(() => Ready(enemy)); enemy.attackDistance = 0;
            var world = NetworkEnemySimulationWorld.Instance; var launch = ImpLaunch(enemy, 9004); launch.Lifetime = .2f;
            int ends = 0; world.EnemyProjectileTerminated += _ => ends++;
            Assert.That(world.PresentEnemyProjectile(launch), Is.True);
            yield return new WaitForSeconds(.3f);
            Assert.That(ends, Is.EqualTo(1)); Assert.That(world.ActiveEnemyProjectileCount, Is.Zero);
            Assert.That(world.PresentEnemyProjectile(launch), Is.False);
        }

        [UnityTest]
        public IEnumerator ImpPhysicsTriggerIgnoresUnownedHitboxAndHitsLocalPlayerOnce()
        {
            var enemy = Spawn("NetworkEnemyImp"); yield return WaitFor(() => Ready(enemy)); enemy.attackDistance = 0;
            var world = NetworkEnemySimulationWorld.Instance; var launch = ImpLaunch(enemy, 9010); BulletProjectile bullet = null;
            world.EnemyProjectilePresented += (_, instance) => bullet = instance;
            var binding = Owner.GetComponent<PlayerCombatantBinding>();
            yield return WaitFor(() => !Owner.GetComponent<PlayerMovement>().IsInvulnerable);
            int hp = binding.CurrentHealth;
            world.PresentEnemyProjectile(launch);
            var unowned = new GameObject("unowned hitbox", typeof(PlayerHitbox));
            bullet.damageInteraction.Interact(unowned.GetComponent<PlayerHitbox>());
            Assert.That(bullet.gameObject.activeInHierarchy, Is.True);
            Object.Destroy(unowned);
            var probe = new GameObject("local projectile physics probe", typeof(CircleCollider2D), typeof(Rigidbody2D), typeof(PlayerHitbox));
            probe.layer = LayerMask.NameToLayer("PlayerHitbox"); probe.GetComponent<PlayerHitbox>().Configure(binding);
            probe.GetComponent<CircleCollider2D>().radius = .1f; probe.GetComponent<CircleCollider2D>().isTrigger = true;
            var body = probe.GetComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic; body.useFullKinematicContacts = true;
            probe.transform.position = launch.Origin + Vector3.right * .5f; body.position = probe.transform.position;
            Physics2D.SyncTransforms(); yield return new WaitForSeconds(.15f);
            Object.Destroy(probe);
            Assert.That(binding.CurrentHealth, Is.EqualTo(hp - 5)); Assert.That(bullet.gameObject.activeInHierarchy, Is.False);
        }

        [UnityTest]
        public IEnumerator ImpLateLaunchSurvivesMissingShooterAndInvulnerableHitStillTerminates()
        {
            var enemy = Spawn("NetworkEnemyImp"); yield return WaitFor(() => Ready(enemy)); enemy.attackDistance = 0;
            var launch = ImpLaunch(enemy, 9011); var world = NetworkEnemySimulationWorld.Instance; BulletProjectile bullet = null;
            world.EnemyProjectilePresented += (_, instance) => bullet = instance;
            NetworkServer.Destroy(enemy.gameObject); yield return null;
            Assert.That(world.PresentEnemyProjectile(launch), Is.True, "The registered prefab must resolve after the shooter is destroyed.");
            var binding = Owner.GetComponent<PlayerCombatantBinding>(); int hp = binding.CurrentHealth;
            binding.Combatant.SetUpgradeSelectionInvulnerable(true);
            bullet.damageInteraction.Interact(Owner.GetComponentInChildren<PlayerHitbox>());
            binding.Combatant.SetUpgradeSelectionInvulnerable(false);
            Assert.That(binding.CurrentHealth, Is.EqualTo(hp)); Assert.That(bullet.gameObject.activeInHierarchy, Is.False);
        }
    }
}
