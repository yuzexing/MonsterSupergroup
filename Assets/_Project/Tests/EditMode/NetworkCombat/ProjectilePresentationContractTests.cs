using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player.Attacks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class ProjectilePresentationContractTests
    {
        private const string PlayerPrefabPath =
            "Assets/_Project/Content/NetworkCombat/NetworkPlayer.prefab";
        private const string DanteWeaponPath =
            "Assets/MonoBehaviour/WeaponData_Dante_SlowProjectile.asset";

        [Test]
        public void Sequence_RejectsDuplicateAndStaleValues_AndSupportsWrap()
        {
            Assert.That(ProjectilePresentationSequence.IsNewer(1u, 0u), Is.True);
            Assert.That(ProjectilePresentationSequence.IsNewer(2u, 1u), Is.True);
            Assert.That(ProjectilePresentationSequence.IsNewer(1u, 1u), Is.False);
            Assert.That(ProjectilePresentationSequence.IsNewer(1u, 2u), Is.False);
            Assert.That(
                ProjectilePresentationSequence.IsNewer(1u, uint.MaxValue),
                Is.True);
            Assert.That(
                ProjectilePresentationSequence.IsNewer(uint.MaxValue, 1u),
                Is.False);
        }

        [Test]
        public void BatchValidation_RequiresOwnedSourceFiniteDataAndKnownPhase()
        {
            NetworkProjectilePresentationEdge spawn = ValidSpawn();
            var termination = new NetworkProjectilePresentationEdge
            {
                SourcePlayerId = 17u,
                WeaponId = 2u,
                AttackEventId = 101UL,
                ProjectileIndex = 0,
                EventNetworkTime = 5.25d,
                Phase = ProjectilePresentationPhase.Hit,
                Position = new Vector3(3f, 4f, 0f)
            };
            var batch = new NetworkProjectilePresentationBatch
            {
                BatchSequence = 1u,
                Edges = new[] { spawn, termination }
            };

            Assert.That(
                NetworkWeaponCombatAdapter.IsValidPresentationBatch(
                    batch,
                    17u,
                    0u,
                    32),
                Is.True);
            Assert.That(
                NetworkWeaponCombatAdapter.IsValidPresentationBatch(
                    batch,
                    18u,
                    0u,
                    32),
                Is.False,
                "A player must not submit another player's presentation.");
            Assert.That(
                NetworkWeaponCombatAdapter.IsValidPresentationBatch(
                    batch,
                    17u,
                    1u,
                    32),
                Is.False,
                "A duplicate reliable batch must not replay.");
            Assert.That(
                NetworkWeaponCombatAdapter.IsValidPresentationBatch(
                    batch,
                    17u,
                    0u,
                    1),
                Is.False,
                "The per-frame batch bound is part of the wire contract.");

            spawn.Direction = new Vector2(float.NaN, 0f);
            batch.Edges = new[] { spawn };
            batch.BatchSequence = 2u;
            Assert.That(
                NetworkWeaponCombatAdapter.IsValidPresentationBatch(
                    batch,
                    17u,
                    1u,
                    32),
                Is.False);

            spawn = ValidSpawn();
            spawn.Phase = (ProjectilePresentationPhase)byte.MaxValue;
            batch.Edges = new[] { spawn };
            Assert.That(
                NetworkWeaponCombatAdapter.IsValidPresentationBatch(
                    batch,
                    17u,
                    1u,
                    32),
                Is.False);
        }

        [Test]
        public void DanteContent_RemainsLinearAndUsesTheExistingPlayerAdapter()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(
                PlayerPrefabPath);
            WeaponData weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(
                DanteWeaponPath);

            Assert.That(player, Is.Not.Null, PlayerPrefabPath);
            Assert.That(weapon, Is.Not.Null, DanteWeaponPath);
            Assert.That(
                player.GetComponents<NetworkWeaponCombatAdapter>().Length,
                Is.EqualTo(1));
            Assert.That(weapon.ID, Is.EqualTo(2u));
            Assert.That(
                weapon.WeaponPrefab,
                Is.TypeOf<ProjectileAttackBehaviour>());

            var serialized = new SerializedObject(weapon.WeaponPrefab);
            SerializedProperty variants = serialized.FindProperty("variants");
            Assert.That(variants, Is.Not.Null);
            AssertLinearVariant(variants, "defaultPrefab");
            AssertLinearVariant(variants, "poisonPrefab");
            AssertLinearVariant(variants, "firePrefab");
        }

        private static NetworkProjectilePresentationEdge ValidSpawn()
        {
            return new NetworkProjectilePresentationEdge
            {
                SourcePlayerId = 17u,
                WeaponId = 2u,
                AttackEventId = 101UL,
                ProjectileIndex = 0,
                EventNetworkTime = 5d,
                Phase = ProjectilePresentationPhase.Spawn,
                Position = new Vector3(1f, 2f, 0f),
                Direction = Vector2.right,
                Element = AttackElement.Default,
                RotateToMovement = true,
                Stats = new ProjectilePresentationStats
                {
                    DamageMultiplierSum = 0f,
                    SpeedMultiplierSum = 0f,
                    SizeMultiplierSum = 0f,
                    DurationMultiplierSum = 0f,
                    EffectiveSpeed = 5f,
                    Duration = 1f,
                    ProjectileCount = 1,
                    BaseProjectileCount = 1
                }
            };
        }

        private static void AssertLinearVariant(
            SerializedProperty variants,
            string propertyName)
        {
            SerializedProperty property =
                variants.FindPropertyRelative(propertyName);
            var projectile = property.objectReferenceValue as ProjectileAttack;
            Assert.That(projectile, Is.Not.Null, propertyName);
            Assert.That(
                projectile.projectileMovement,
                Is.Null,
                $"Dante {propertyName} no longer satisfies linear replay.");
        }
    }
}
