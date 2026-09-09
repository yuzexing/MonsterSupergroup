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
    public sealed class DanteBeamNativeGasMigrationTests
    {
        [Test]
        public void SourceWeaponIdentityStatsAndModifierCompatibilityArePreserved()
        {
            WeaponData weapon = Weapon();
            Assert.That(weapon.ID, Is.EqualTo(3));
            Assert.That(weapon.BaseStats.damage, Is.EqualTo(5));
            Assert.That(weapon.BaseStats.critRate, Is.EqualTo(0.03f));
            Assert.That(weapon.BaseStats.critMultiplier, Is.EqualTo(1.3f));
            Assert.That(weapon.BaseStats.speed, Is.EqualTo(3f));
            Assert.That(weapon.BaseStats.size, Is.EqualTo(1f));
            Assert.That(weapon.BaseStats.duration, Is.EqualTo(2f));
            Assert.That(weapon.BaseStats.projectileCount, Is.EqualTo(1));
            Assert.That(weapon.BaseStats.knockbackDistance, Is.Zero);
            Assert.That(weapon.Presentation.Knockback.distance, Is.Zero);
            Assert.That(weapon.AttackTags, Is.EqualTo(CombatTags.Attack),
                "Fire is the source's default visual, not an implicit burn or projectile definition.");
            Assert.That((int)weapon.modifierFlags, Is.EqualTo(111));
            Assert.That(weapon.UltimateData, Is.Null);
            Assert.DoesNotThrow(weapon.ValidateNativeGas);
        }

        [Test]
        public void SourceEmitterKeepsFireDefaultPoisonVariantAndSingleBeamLimit()
        {
            var emitter = Weapon().WeaponPrefab as PlayerBeamAttackBehaviour;
            Assert.That(emitter, Is.Not.Null);
            Assert.That(emitter.spawnRadius, Is.EqualTo(1.5f));
            var serialized = new SerializedObject(emitter);
            Assert.That(serialized.FindProperty("baseLerpSpeed").floatValue, Is.EqualTo(4f));
            Assert.That(serialized.FindProperty("allowMultipleAttacks").boolValue, Is.False);
            Assert.That(serialized.FindProperty("variants.defaultPrefab").objectReferenceValue, Is.EqualTo(Attack(false)));
            Assert.That(serialized.FindProperty("variants.firePrefab").objectReferenceValue, Is.EqualTo(Attack(false)));
            Assert.That(serialized.FindProperty("variants.poisonPrefab").objectReferenceValue, Is.EqualTo(Attack(true)));
            Assert.That(serialized.FindProperty("variants.allowFire").boolValue, Is.True);
            Assert.That(serialized.FindProperty("variants.allowPoison").boolValue, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OvertimeHitboxRetainsSourceIntervalExitGraceAndSpeedScaler(bool poison)
        {
            AnimatedAttack beam = Attack(poison);
            Assert.That(beam.hitbox, Is.TypeOf<PlayerAttackOvertimeHitBox>());
            Assert.That(beam.hitbox.transform, Is.EqualTo(beam.transform.Find("Root/Scale/HitBox")));
            Assert.That(beam.hitbox.GetComponent<PolygonCollider2D>(), Is.Not.Null);
            Assert.That(beam.hitbox.GetComponent<PolygonCollider2D>().enabled, Is.False,
                "The source starts with no hit window until its main animation enables it.");
            var hitbox = new SerializedObject(beam.hitbox);
            Assert.That(hitbox.FindProperty("hitInterval").floatValue, Is.EqualTo(0.5f));
            Assert.That(hitbox.FindProperty("timeoutAfterExit").floatValue, Is.EqualTo(0.3f));
            Assert.That(hitbox.FindProperty("disableAfterTimeout").boolValue, Is.False);
            Assert.That(beam.progressionScaler, Is.Not.Null);
            Assert.That(beam.progressionScaler.allowNegativeSpeedMultiplier, Is.True);
            Assert.That(beam.progressionScaler.speedCustomScalers.Count, Is.EqualTo(1));
            CustomProgressionScaler scaler = beam.progressionScaler.speedCustomScalers.Single();
            Assert.That(scaler, Is.Not.Null);
            Assert.That(scaler.GetType().Name, Is.EqualTo("PlayerAttackOvertimeHitBoxProgressionScaler"));
            var scaling = new SerializedObject(scaler);
            Assert.That(scaling.FindProperty("hitbox").objectReferenceValue, Is.EqualTo(beam.hitbox));
            Assert.That(scaling.FindProperty("defaultHitInterval").floatValue, Is.EqualTo(0.5f));
            Assert.That(scaling.FindProperty("scallingFactor").floatValue, Is.EqualTo(1f));
            Assert.That(scaling.FindProperty("clampMin").boolValue, Is.False);
            Assert.That(scaling.FindProperty("clampMax").boolValue, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReconstructedClipBindingsUseMainPhaseDurationWithoutChangingSourceLoopFlags(bool poison)
        {
            AnimatedAttack beam = Attack(poison);
            var serialized = new SerializedObject(beam);
            Assert.That(serialized.FindProperty("animancer").objectReferenceValue, Is.Not.Null);
            Assert.That(beam.RotationTransform, Is.EqualTo(beam.transform.Find("Root")));
            Assert.That(beam.attackStartAnimTransitionAfterFinish, Is.True,
                "Explicit reconstruction: the source component omitted its transition fields.");
            Assert.That(beam.attackAnimTransitionAfterFinish, Is.False,
                "The source emitter's requested Duration controls the main phase, including upgrades beyond four seconds.");
            Assert.That(beam.GetPresentationDuration(2f), Is.EqualTo(3.36666667f).Within(0.0001f));
            Assert.That(beam.GetPresentationDuration(6f), Is.EqualTo(7.36666667f).Within(0.0001f));
            string[] fields = { "attackStartAnim", "attackAnim", "attackEndAnim" };
            string[] paths = { DanteBeamNativeGasMigration.StartClipPath, DanteBeamNativeGasMigration.MainClipPath,
                DanteBeamNativeGasMigration.EndClipPath };
            for (int i = 0; i < fields.Length; i++)
            {
                AnimationClip clip = Clip(paths[i]);
                Assert.That(serialized.FindProperty(fields[i] + "._Clip").objectReferenceValue, Is.EqualTo(clip));
                Assert.That(serialized.FindProperty(fields[i] + "._FadeDuration").floatValue, Is.Zero);
                Assert.That(serialized.FindProperty(fields[i] + "._NormalizedStartTime").floatValue, Is.Zero);
                Assert.That(serialized.FindProperty(fields[i] + "._Speed").floatValue, Is.EqualTo(1f));
                Assert.That(clip.isLooping, Is.False);
                Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty);
            }
        }

        [TestCase(DanteBeamNativeGasMigration.StartClipPath, 0.36666667f, 0f, 1)]
        [TestCase(DanteBeamNativeGasMigration.MainClipPath, 4f, 1f, 2)]
        [TestCase(DanteBeamNativeGasMigration.EndClipPath, 1f, 0f, 1)]
        public void SourceColliderWindowOnlyEnablesDuringMainPhase(string path, float length, float value, int keyCount)
        {
            AnimationClip clip = Clip(path);
            Assert.That(clip.length, Is.EqualTo(length).Within(0.0001f));
            EditorCurveBinding binding = AnimationUtility.GetCurveBindings(clip).Single(candidate =>
                candidate.path == "Scale/HitBox" && candidate.propertyName == "m_Enabled" &&
                candidate.type == typeof(PolygonCollider2D));
            Keyframe[] keys = AnimationUtility.GetEditorCurve(clip, binding).keys;
            Assert.That(keys.Length, Is.EqualTo(keyCount));
            Assert.That(keys.All(key => key.value == value), Is.True);
            Assert.That(keys[0].time, Is.Zero);
            if (keyCount == 2) Assert.That(keys[1].time, Is.EqualTo(4f));
        }

        [TestCase(false, 18, 30)]
        [TestCase(true, 19, 31)]
        public void SourceHierarchyParticlesAudioAndRenderableDependenciesSurvive(bool poison, int particles, int transforms)
        {
            AnimatedAttack beam = Attack(poison);
            Assert.That(beam.GetComponentsInChildren<ParticleSystem>(true).Length, Is.EqualTo(particles));
            Assert.That(beam.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(transforms));
            Assert.That(beam.transform.Find("Root/Scale/Rotation/FX_PF_Fire_FlamethrowerBurst"), Is.Not.Null);
            Assert.That(beam.transform.Find("Root/Scale/Rotation/FX_PF_Fire_FlamethrowerLoop"), Is.Not.Null);
            Assert.That(beam.transform.Find("Root/Scale/Rotation/Mask_0"), Is.Not.Null);
            Component emitter = beam.transform.Find("Audio/Start").GetComponent("StudioEventEmitter");
            Component endTrigger = beam.transform.Find("Audio/End").GetComponent("StudioParameterTrigger");
            Assert.That(emitter, Is.Not.Null);
            Assert.That(endTrigger, Is.Not.Null);
            var audio = new SerializedObject(emitter);
            Assert.That(audio.FindProperty("EventReference.Guid.Data1").intValue, Is.EqualTo(-1201277076));
            Assert.That(audio.FindProperty("EventPlayTrigger").intValue, Is.EqualTo(11));
            Assert.That(audio.FindProperty("EventStopTrigger").intValue, Is.EqualTo(12));
            var end = new SerializedObject(endTrigger);
            Assert.That(end.FindProperty("Emitters.Array.data[0].Target").objectReferenceValue, Is.EqualTo(emitter));
            foreach (Transform child in beam.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject), Is.Zero, child.name);
                Assert.That(child.GetComponents<Component>().Any(component => component != null &&
                    component.GetType().Name == "NetworkIdentity"), Is.False, child.name);
            }
            foreach (Material material in beam.GetComponentsInChildren<Renderer>(true)
                         .Where(renderer => renderer.enabled).SelectMany(renderer => renderer.sharedMaterials))
            {
                Assert.That(material, Is.Not.Null);
                Assert.That(material.shader, Is.Not.Null);
                Assert.That(material.shader.name, Does.StartWith("AllIn1"));
            }
            Assert.DoesNotThrow(DanteBeamNativeGasMigration.ValidateImportedAssets);
        }

        private static WeaponData Weapon()
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(DanteBeamNativeGasMigration.WeaponPath);
            Assert.That(weapon, Is.Not.Null, "Run the focused Dante beam importer before these asset checks.");
            return weapon;
        }

        private static AnimatedAttack Attack(bool poison)
        {
            string path = poison ? DanteBeamNativeGasMigration.PoisonAttackPath : DanteBeamNativeGasMigration.FireAttackPath;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            return prefab.GetComponent<AnimatedAttack>();
        }

        private static AnimationClip Clip(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            Assert.That(clip, Is.Not.Null);
            return clip;
        }
    }
}
