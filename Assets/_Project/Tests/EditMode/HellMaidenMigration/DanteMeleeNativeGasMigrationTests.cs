using System.Linq;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using MonsterSupergroup.HellMaidenMigration.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.HellMaidenMigration.Tests
{
    public sealed class DanteMeleeNativeGasMigrationTests
    {
        [Test]
        public void SourceWeaponIdentityStatsAndSupportedModifiersArePreserved()
        {
            WeaponData weapon = Weapon();
            Assert.That(weapon.ID, Is.EqualTo(1));
            Assert.That(weapon.BaseStats.damage, Is.EqualTo(19));
            Assert.That(weapon.BaseStats.critRate, Is.EqualTo(0.07f));
            Assert.That(weapon.BaseStats.critMultiplier, Is.EqualTo(1.3f));
            Assert.That(weapon.BaseStats.speed, Is.EqualTo(1f));
            Assert.That(weapon.BaseStats.size, Is.EqualTo(1f));
            Assert.That(weapon.BaseStats.duration, Is.EqualTo(1f));
            Assert.That(weapon.BaseStats.projectileCount, Is.EqualTo(1));
            Assert.That(weapon.BaseStats.knockbackDistance, Is.EqualTo(0.6f));
            Assert.That(weapon.AttackTags, Is.EqualTo(CombatTags.Attack));
            Assert.That((int)weapon.modifierFlags, Is.EqualTo(247));
            Assert.That(weapon.Presentation.Knockback.speedMultiplier, Is.EqualTo(8f));
            Assert.That(weapon.UltimateData, Is.Null, "Ultimate must not silently import its single-player runtime.");
            Assert.DoesNotThrow(weapon.ValidateNativeGas);
        }

        [Test]
        public void RepairedEmitterUsesOriginalHitboxScaleAndAttackClip()
        {
            var emitter = Weapon().WeaponPrefab as MeleeAttackBehaviour;
            Assert.That(emitter, Is.Not.Null);
            Assert.That(emitter.spawnRadius, Is.EqualTo(1f));
            Assert.That(emitter.overrideAnimationLength, Is.False);
            Assert.That(emitter.multiProjectilesInterval, Is.EqualTo(0.1f));
            AnimatedAttack slash = emitter.prefab;
            Assert.That(slash, Is.Not.Null);
            Assert.That(slash.hitbox, Is.TypeOf<PlayerAttackHitBox>());
            Assert.That(slash.hitbox.transform.parent.name, Is.EqualTo("Scale"));
            Assert.That(slash.progressionScaler.sizeTransforms.Count, Is.EqualTo(2));
            Assert.That(slash.RotationTransform, Is.EqualTo(slash.transform.Find("Root")));
            var serialized = new SerializedObject(slash);
            Assert.That(serialized.FindProperty("animancer").objectReferenceValue, Is.Not.Null);
            Assert.That(serialized.FindProperty("attackAnim._Clip").objectReferenceValue, Is.EqualTo(Clip()));
            Assert.That(serialized.FindProperty("attackAnim._NormalizedStartTime").floatValue, Is.Zero,
                "Pooled slashes must replay from their first frame.");
            Assert.That(serialized.FindProperty("attackAnim._FadeDuration").floatValue, Is.Zero,
                "The hitbox enable curve must not blend with an old pooled slash.");
            Assert.That(serialized.FindProperty("attackAnimTransitionAfterFinish").boolValue, Is.True);
        }

        [Test]
        public void OriginalColliderWindowAndNonLoopingDurationArePreserved()
        {
            AnimationClip clip = Clip();
            Assert.That(clip.length, Is.EqualTo(2f / 3f).Within(0.0001f));
            Assert.That(clip.isLooping, Is.False);
            EditorCurveBinding binding = AnimationUtility.GetCurveBindings(clip).Single(value =>
                value.path == "Scale/HitBox" && value.propertyName == "m_Enabled" && value.type == typeof(BoxCollider2D));
            Keyframe[] keys = AnimationUtility.GetEditorCurve(clip, binding).keys;
            Assert.That(keys.Length, Is.EqualTo(3));
            Assert.That(keys.Select(key => key.value), Is.EqualTo(new[] { 0f, 1f, 0f }));
            Assert.That(keys[0].time, Is.Zero);
            Assert.That(keys[1].time, Is.EqualTo(0.06666667f).Within(0.00001f));
            Assert.That(keys[2].time, Is.EqualTo(0.21666667f).Within(0.00001f));
        }

        [Test]
        public void AllSourceParticleLayersSurviveWithoutMissingScriptsOrNetworkIdentity()
        {
            AnimatedAttack slash = ((MeleeAttackBehaviour)Weapon().WeaponPrefab).prefab;
            Assert.That(slash.GetComponentsInChildren<ParticleSystem>(true).Length, Is.EqualTo(10));
            Assert.That(slash.transform.Find("Root/Scale/Main/SlashLight (2)/GameObject"), Is.Not.Null);
            Assert.That(slash.transform.Find("Root/Scale/Main/SlashDark"), Is.Not.Null);
            Assert.That(slash.transform.Find("Root/Scale/Main/Sparks_Turbulence"), Is.Not.Null);
            foreach (Transform child in slash.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject), Is.Zero, child.name);
                Assert.That(child.GetComponents<Component>().Any(component => component.GetType().Name == "NetworkIdentity"),
                    Is.False, child.name);
            }
            // The source has three empty grouping renderers plus a disabled Main renderer that
            // retains its Rubfish material. All four stay disabled; six visual renderers are active.
            Renderer[] renderers = slash.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers.Count(renderer => !renderer.enabled), Is.EqualTo(4));
            Assert.That(renderers.Count(renderer => renderer.enabled), Is.EqualTo(6));
            Assert.That(renderers.Count(renderer => renderer.sharedMaterial == null), Is.EqualTo(3));
            Assert.That(slash.transform.Find("Root/Scale/Main").GetComponent<Renderer>().enabled, Is.False);
            Assert.That(slash.transform.Find("Root/Scale/Main").GetComponent<Renderer>().sharedMaterial, Is.Not.Null);
            foreach (Material material in renderers
                         .Where(renderer => renderer.enabled).SelectMany(renderer => renderer.sharedMaterials))
            {
                Assert.That(material, Is.Not.Null);
                Assert.That(material.shader, Is.Not.Null);
                Assert.That(material.shader.name, Does.StartWith("AllIn1"));
            }
            Assert.DoesNotThrow(DanteMeleeNativeGasMigration.ValidateImportedAssets);
        }

        private static WeaponData Weapon()
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(DanteMeleeNativeGasMigration.WeaponPath);
            Assert.That(weapon, Is.Not.Null, "Run the focused Dante melee importer before this asset validation suite.");
            return weapon;
        }

        private static AnimationClip Clip()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(DanteMeleeNativeGasMigration.ClipPath);
            Assert.That(clip, Is.Not.Null);
            return clip;
        }
    }
}
