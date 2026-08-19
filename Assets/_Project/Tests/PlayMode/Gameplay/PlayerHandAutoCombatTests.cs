using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MonsterSupergroup.GAS;
using MonsterSupergroup.GAS.Authoring;
using MonsterSupergroup.GAS.Unity;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class PlayerHandAutoCombatTests
    {
        private readonly List<Object> cleanup = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = cleanup.Count - 1; i >= 0; i--)
            {
                if (cleanup[i] != null)
                {
                    Object.DestroyImmediate(cleanup[i]);
                }
            }

            cleanup.Clear();
        }

        [UnityTest]
        public IEnumerator PlayerHand_HasFourSlots_AndEnforcesThreeEquipmentLimit()
        {
            CombatTeamBehaviour owner = CreateCombatant("Player", CombatTeam.Player, Vector2.zero);
            NearestEnemyTargetProvider targetProvider = owner.gameObject.AddComponent<NearestEnemyTargetProvider>();
            targetProvider.Configure(owner);
            Transform attacksRoot = Track(new GameObject("Attacks")).transform;
            attacksRoot.SetParent(owner.transform, false);
            WeaponDefinition definition = CreateWeaponDefinition(speed: 1f, projectileSpeed: 10f);
            var hand = new PlayerHand(attacksRoot, targetProvider, owner, new SeededRandomSource(7));

            Assert.That(hand.Slots.Count, Is.EqualTo(4));
            Assert.That(hand.TryEquipWeapon(0, definition), Is.True);
            Assert.That(hand.GetSlot(0).HasWeapon, Is.True);
            Assert.That(hand.GetSlot(1).HasWeapon, Is.False);

            EquipmentModifierSet first = CreateEquipmentSet(
                new EquipmentDataModifier(
                    new EquipmentModifierID(DamageStatModifier.ModifierIdValue),
                    new DamageStatModifierParameters(0.5f)));
            EquipmentModifierSet second = Track(ScriptableObject.CreateInstance<EquipmentModifierSet>());
            EquipmentModifierSet third = Track(ScriptableObject.CreateInstance<EquipmentModifierSet>());
            EquipmentModifierSet fourth = Track(ScriptableObject.CreateInstance<EquipmentModifierSet>());
            Assert.That(hand.GetSlot(0).TryAddEquipment(first), Is.True);
            Assert.That(hand.GetSlot(0).TryAddEquipment(second), Is.True);
            Assert.That(hand.GetSlot(0).TryAddEquipment(third), Is.True);
            Assert.That(hand.GetSlot(0).TryAddEquipment(fourth), Is.False);
            Assert.That(hand.GetSlot(0).Equipment.Count, Is.EqualTo(3));
            Assert.That(hand.GetSlot(0).Weapon.BeginAttack().Stats.Damage, Is.EqualTo(15));

            hand.Dispose();
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerHand_OneHundredEquipUnequipCycles_DoNotAccumulateState()
        {
            CombatTeamBehaviour owner = CreateCombatant("Player", CombatTeam.Player, Vector2.zero);
            NearestEnemyTargetProvider targetProvider = owner.gameObject.AddComponent<NearestEnemyTargetProvider>();
            targetProvider.Configure(owner);
            Transform attacksRoot = Track(new GameObject("Attacks")).transform;
            attacksRoot.SetParent(owner.transform, false);
            WeaponDefinition definition = CreateWeaponDefinition(speed: 1f, projectileSpeed: 10f);
            var hand = new PlayerHand(attacksRoot, targetProvider, owner, new SeededRandomSource(17));
            int changes = 0;
            hand.SlotChanged += (_, _) => changes++;

            for (int i = 0; i < 100; i++)
            {
                Assert.That(hand.TryEquipWeapon(0, definition), Is.True);
                Assert.That(hand.GetSlot(0).Weapon.ModifierCount, Is.Zero);
                Assert.That(hand.TryUnequipWeapon(0), Is.True);
                Assert.That(hand.GetSlot(0).HasWeapon, Is.False);
            }

            Assert.That(changes, Is.EqualTo(200));
            hand.Dispose();
            yield return null;
        }

        [Test]
        public void NearestTargetProvider_IgnoresFriendlyDeadAndDistantCombatants()
        {
            CombatTeamBehaviour owner = CreateCombatant("Player", CombatTeam.Player, Vector2.zero);
            NearestEnemyTargetProvider provider = owner.gameObject.AddComponent<NearestEnemyTargetProvider>();
            provider.Configure(owner);
            CreateCombatant("Friendly", CombatTeam.Player, Vector2.right);
            CombatTeamBehaviour near = CreateCombatant("NearEnemy", CombatTeam.Enemy, Vector2.right * 2f);
            CreateCombatant("FarEnemy", CombatTeam.Enemy, Vector2.right * 4f);
            CombatTeamBehaviour dead = CreateCombatant("DeadEnemy", CombatTeam.Enemy, Vector2.right * 0.5f);
            dead.Combatant.ReceiveDamage(new DamageInfo(99, 100, false));

            bool found = provider.TryGetNearest(Vector2.zero, 3f, out CombatantBehaviour target, out Vector2 direction);

            Assert.That(found, Is.True);
            Assert.That(target, Is.SameAs(near.Combatant));
            Assert.That(Vector2.Distance(direction, Vector2.right), Is.LessThan(0.0001f));
        }

        [UnityTest]
        public IEnumerator ProjectileAttack_UsesLaunchDirection_AndDamagesEnemyThroughGas()
        {
            CombatTeamBehaviour owner = CreateCombatant("Player", CombatTeam.Player, Vector2.zero);
            NearestEnemyTargetProvider targetProvider = owner.gameObject.AddComponent<NearestEnemyTargetProvider>();
            targetProvider.Configure(owner);
            CombatTeamBehaviour enemy = CreateCombatant("Enemy", CombatTeam.Enemy, Vector2.right * 2f);
            Transform attacksRoot = Track(new GameObject("Attacks")).transform;
            attacksRoot.SetParent(owner.transform, false);
            WeaponDefinition definition = CreateWeaponDefinition(speed: 5f, projectileSpeed: 20f);
            var hand = new PlayerHand(attacksRoot, targetProvider, owner, new SeededRandomSource(11));
            hand.TryEquipWeapon(0, definition);
            hand.ActivateWeapons();

            float timeout = Time.time + 2f;
            while (enemy.Combatant.CurrentHealth == enemy.Combatant.MaxHealth && Time.time < timeout)
            {
                yield return null;
            }

            Assert.That(enemy.Combatant.CurrentHealth, Is.LessThan(enemy.Combatant.MaxHealth));
            Assert.That(enemy.Combatant.DirectDamageTaken, Is.GreaterThanOrEqualTo(10));
            hand.Dispose();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Projectile_DoesNotRetargetAfterEnemyMoves()
        {
            CombatTeamBehaviour owner = CreateCombatant("Player", CombatTeam.Player, Vector2.zero);
            NearestEnemyTargetProvider targetProvider = owner.gameObject.AddComponent<NearestEnemyTargetProvider>();
            targetProvider.Configure(owner);
            CombatTeamBehaviour enemy = CreateCombatant("Enemy", CombatTeam.Enemy, Vector2.right * 4f);
            Transform attacksRoot = Track(new GameObject("Attacks")).transform;
            attacksRoot.SetParent(owner.transform, false);
            WeaponDefinition definition = CreateWeaponDefinition(speed: 1f, projectileSpeed: 1f);
            var hand = new PlayerHand(attacksRoot, targetProvider, owner, new SeededRandomSource(13));
            hand.TryEquipWeapon(0, definition);
            ProjectileAttackBehaviour attack = hand.GetSlot(0).AttackBehaviour;
            hand.ActivateWeapons();
            Assert.That(attack.TryAttack(), Is.True);
            yield return null;

            StraightProjectileBehaviour projectile = null;
            StraightProjectileBehaviour[] projectiles = Object.FindObjectsByType<StraightProjectileBehaviour>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (int i = 0; i < projectiles.Length; i++)
            {
                if (projectiles[i].Direction.sqrMagnitude > 0.5f)
                {
                    projectile = projectiles[i];
                    break;
                }
            }
            Assert.That(projectile, Is.Not.Null);
            Vector2 launchDirection = projectile.Direction;
            enemy.transform.position = Vector2.up * 4f;
            yield return new WaitForFixedUpdate();

            Assert.That(Vector2.Distance(projectile.Direction, launchDirection), Is.LessThan(0.0001f));
            hand.Dispose();
            yield return null;
        }

        [UnityTest]
        public IEnumerator GameplayScene_StartsLocalAutoCombatWithoutMirrorHost()
        {
            yield return SceneManager.LoadSceneAsync("Gameplay", LoadSceneMode.Single);
            yield return null;

            Assert.That(Object.FindFirstObjectByType<MonsterSupergroup.Gameplay.Local.LocalGameplayBootstrap>(),
                Is.Not.Null);
            Assert.That(GameObject.Find("NetworkManager"), Is.Null);

            float timeout = Time.time + 5f;
            bool damageObserved = false;
            while (Time.time < timeout && !damageObserved)
            {
                CombatTeamBehaviour[] combatants = Object.FindObjectsByType<CombatTeamBehaviour>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
                for (int i = 0; i < combatants.Length; i++)
                {
                    if (combatants[i].Team == CombatTeam.Enemy &&
                        combatants[i].Combatant.DirectDamageTaken > 0)
                    {
                        damageObserved = true;
                        break;
                    }
                }

                yield return null;
            }

            Assert.That(damageObserved, Is.True,
                "The local player did not automatically damage an enemy within five seconds.");
        }

        private CombatTeamBehaviour CreateCombatant(string objectName, CombatTeam team, Vector2 position)
        {
            GameObject root = Track(new GameObject(objectName));
            root.transform.position = position;
            Rigidbody2D body = root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.useFullKinematicContacts = true;
            CircleCollider2D collider = root.AddComponent<CircleCollider2D>();
            collider.radius = 0.5f;
            CombatantBehaviour combatant = root.AddComponent<CombatantBehaviour>();
            combatant.Initialize(100);
            CombatTeamBehaviour result = root.AddComponent<CombatTeamBehaviour>();
            result.Configure(team, combatant);
            return result;
        }

        private WeaponDefinition CreateWeaponDefinition(float speed, float projectileSpeed)
        {
            GameObject projectileTemplate = Track(new GameObject("ProjectileTemplate"));
            Rigidbody2D projectileBody = projectileTemplate.AddComponent<Rigidbody2D>();
            projectileBody.bodyType = RigidbodyType2D.Kinematic;
            projectileBody.gravityScale = 0f;
            projectileBody.useFullKinematicContacts = true;
            projectileTemplate.AddComponent<CircleCollider2D>().isTrigger = true;
            StraightProjectileBehaviour projectile = projectileTemplate.AddComponent<StraightProjectileBehaviour>();

            GameObject weaponTemplate = Track(new GameObject("WeaponTemplate"));
            weaponTemplate.SetActive(false);
            WeaponRuntimeBehaviour runtime = weaponTemplate.AddComponent<WeaponRuntimeBehaviour>();
            runtime.InitializeOnAwake = false;
            ProjectileAttackBehaviour weapon = weaponTemplate.AddComponent<ProjectileAttackBehaviour>();

            WeaponDefinition definition = Track(ScriptableObject.CreateInstance<WeaponDefinition>());
            definition.Configure(
                1,
                new AttackStats
                {
                    damage = 10,
                    critMultiplier = 2f,
                    critRate = 0f,
                    speed = speed,
                    size = 1f,
                    duration = 5f,
                    projectileCount = 1,
                    damageType = DamageType.Projectile
                },
                weapon,
                projectile,
                newTargetRange: 20f,
                newProjectileSpeed: projectileSpeed,
                newSpawnRadius: 0f);
            return definition;
        }

        private EquipmentModifierSet CreateEquipmentSet(params EquipmentDataModifier[] modifiers)
        {
            EquipmentModifierSet set = Track(ScriptableObject.CreateInstance<EquipmentModifierSet>());
            FieldInfo field = typeof(EquipmentModifierSet).GetField(
                "modifiers",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(set, new List<EquipmentDataModifier>(modifiers));
            return set;
        }

        private T Track<T>(T value) where T : Object
        {
            cleanup.Add(value);
            return value;
        }
    }
}
