using System.IO;
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
    public sealed class DanteCirclingNativeGasMigrationTests
    {
        [Test]
        public void SourceIdentityStatsAndSmallKnockbackEnterNativeDefinition()
        {
            WeaponData weapon = Weapon();
            Assert.That(weapon.ID, Is.EqualTo(6));
            Assert.That(weapon.BaseStats.damage, Is.EqualTo(12));
            Assert.That(weapon.BaseStats.critRate, Is.EqualTo(0.05f));
            Assert.That(weapon.BaseStats.critMultiplier, Is.EqualTo(1.4f));
            Assert.That(weapon.BaseStats.speed, Is.EqualTo(1f));
            Assert.That(weapon.BaseStats.size, Is.EqualTo(1.1f));
            Assert.That(weapon.BaseStats.duration, Is.EqualTo(3.14f));
            Assert.That(weapon.BaseStats.projectileCount, Is.EqualTo(2));
            Assert.That(weapon.BaseStats.knockbackDistance, Is.EqualTo(0.2f));
            Assert.That(weapon.Presentation.Knockback.distance, Is.EqualTo(0.2f));
            Assert.That(weapon.Presentation.Knockback.speedMultiplier, Is.EqualTo(10f));
            Assert.That(weapon.AttackTags, Is.EqualTo(CombatTags.Attack),
                "Orbiting visuals do not imply Projectile or Fire damage semantics.");
            Assert.That((int)weapon.modifierFlags, Is.EqualTo(-1));
            Assert.That(weapon.UltimateData, Is.Null);
            Assert.DoesNotThrow(weapon.ValidateNativeGas);
        }

        [Test]
        public void SourceEmitterPlaneRadiusVelocityAndOrbRotationArePreserved()
        {
            var emitter = Weapon().WeaponPrefab as CirclingAttackBehaviour;
            Assert.That(emitter, Is.Not.Null);
            Assert.That(emitter.attackPrefab, Is.EqualTo(Attack()));
            Assert.That(emitter.baseRadius, Is.EqualTo(2f));
            Assert.That(emitter.baseSpeed, Is.EqualTo(2f));
            Assert.That(emitter.baseCooldown, Is.EqualTo(5f));
            Assert.That(emitter.transform.localPosition, Is.EqualTo(new Vector3(0f, -0.5f, 0f)));
            Assert.That(Quaternion.Angle(emitter.transform.localRotation, Quaternion.Euler(45f, 0f, 0f)), Is.LessThan(0.01f));
            Assert.That(Quaternion.Angle(Attack().transform.localRotation, Quaternion.identity), Is.LessThan(0.01f));
            Assert.That(new SerializedObject(Attack()).FindProperty("isometricRotation").boolValue, Is.False);
        }

        [Test]
        public void ContinuousHitIntervalHasOnlySourceSizeScaling()
        {
            AnimatedAttack attack = Attack();
            Assert.That(attack.hitbox, Is.TypeOf<PlayerAttackOvertimeHitBox>());
            Assert.That(attack.hitbox.transform, Is.EqualTo(attack.transform.Find("Root/Scale/HitBox")));
            var collider = attack.hitbox.GetComponent<CircleCollider2D>();
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.radius, Is.EqualTo(1f));
            Assert.That(collider.enabled, Is.True);
            Assert.That(collider.isTrigger, Is.True);
            var hitbox = new SerializedObject(attack.hitbox);
            Assert.That(hitbox.FindProperty("hitInterval").floatValue, Is.EqualTo(0.75f));
            Assert.That(hitbox.FindProperty("timeoutAfterExit").floatValue, Is.EqualTo(0.5f));
            Assert.That(hitbox.FindProperty("disableAfterTimeout").boolValue, Is.False);
            Assert.That(attack.progressionScaler, Is.Not.Null);
            Assert.That(attack.progressionScaler.sizeTransforms.Count, Is.EqualTo(4));
            var scaler = new SerializedObject(attack.progressionScaler);
            foreach (string field in new[] { "speedTransforms", "speedParticleSystemProperties", "speedShaderProperties", "speedCustomScalers" })
                Assert.That(scaler.FindProperty(field).arraySize, Is.Zero, field);
            Assert.That(attack.GetComponentsInChildren<Component>(true).Any(component => component != null &&
                component.GetType().Name == "PlayerAttackOvertimeHitBoxProgressionScaler"), Is.False);
        }

        [Test]
        public void MissingAnimationReferencesAreExplicitlyRebuiltForExternalDuration()
        {
            AnimatedAttack attack = Attack();
            var serialized = new SerializedObject(attack);
            Assert.That(serialized.FindProperty("animancer").objectReferenceValue, Is.Not.Null);
            Assert.That(attack.RotationTransform, Is.EqualTo(attack.transform.Find("Root")));
            Assert.That(attack.attackStartAnimTransitionAfterFinish, Is.True);
            Assert.That(attack.attackAnimTransitionAfterFinish, Is.False,
                "A zero-length looping pose must wait for the orbit runtime to explicitly start Hide.");
            string[] fields = { "attackStartAnim", "attackAnim", "attackEndAnim" };
            string[] paths = { DanteCirclingNativeGasMigration.StartClipPath, DanteCirclingNativeGasMigration.MainClipPath,
                DanteCirclingNativeGasMigration.EndClipPath };
            for (int i = 0; i < fields.Length; i++)
            {
                Assert.That(serialized.FindProperty(fields[i] + "._Clip").objectReferenceValue, Is.EqualTo(Clip(paths[i])));
                Assert.That(serialized.FindProperty(fields[i] + "._FadeDuration").floatValue, Is.Zero);
                Assert.That(serialized.FindProperty(fields[i] + "._NormalizedStartTime").floatValue, Is.Zero);
                Assert.That(serialized.FindProperty(fields[i] + "._Speed").floatValue, Is.EqualTo(1f));
            }
            Assert.That(attack.transform.Find("Root").GetComponent<Animator>().runtimeAnimatorController, Is.Null);
        }

        [TestCase(DanteCirclingNativeGasMigration.StartClipPath, 0.016666668f, false, 1f, 2)]
        [TestCase(DanteCirclingNativeGasMigration.MainClipPath, 0f, true, 1f, 1)]
        [TestCase(DanteCirclingNativeGasMigration.EndClipPath, 0.8f, false, 0f, 2)]
        public void SourcePhaseTimingLoopFlagsAndColliderWindowsAreUnchanged(string path, float length, bool looping, float value, int count)
        {
            AnimationClip clip = Clip(path);
            Assert.That(clip.length, Is.EqualTo(length).Within(0.0001f));
            Assert.That(clip.isLooping, Is.EqualTo(looping));
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty);
            EditorCurveBinding binding = AnimationUtility.GetCurveBindings(clip).Single(candidate =>
                candidate.path == "Scale/HitBox" && candidate.type == typeof(CircleCollider2D) && candidate.propertyName == "m_Enabled");
            Keyframe[] keys = AnimationUtility.GetEditorCurve(clip, binding).keys;
            Assert.That(keys.Length, Is.EqualTo(count));
            Assert.That(keys.All(key => key.value == value), Is.True);
            Assert.That(keys[0].time, Is.Zero);
        }

        [Test]
        public void OriginalHierarchyParticlesAndAudioRemainWithoutInventingAudioTriggers()
        {
            AnimatedAttack attack = Attack();
            Assert.That(attack.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(31));
            Assert.That(attack.GetComponentsInChildren<ParticleSystem>(true).Length, Is.EqualTo(21));
            Assert.That(attack.transform.Find("Root/Scale/Main/Sphere_Front"), Is.Not.Null);
            Assert.That(attack.transform.Find("Root/Scale/Main/Sphere_Back"), Is.Not.Null);
            Assert.That(attack.transform.Find("Root/Scale/StartEnd/CausticTrails"), Is.Not.Null);
            Component emitter = attack.transform.Find("Audio/Start").GetComponent("StudioEventEmitter");
            var audio = new SerializedObject(emitter);
            Assert.That(audio.FindProperty("EventReference.Guid.Data1").intValue, Is.EqualTo(-2079503045));
            Assert.That(audio.FindProperty("EventPlayTrigger").intValue, Is.Zero);
            Assert.That(audio.FindProperty("EventStopTrigger").intValue, Is.Zero);
            Component trigger = attack.transform.Find("Audio/End").GetComponent("StudioParameterTrigger");
            var end = new SerializedObject(trigger);
            Assert.That(end.FindProperty("Emitters.Array.data[0].Target").objectReferenceValue, Is.EqualTo(emitter));
            Assert.That(end.FindProperty("TriggerEvent").intValue, Is.EqualTo(12));
            Component tween = attack.transform.Find("Audio/End").GetComponent("FmodParameterTween");
            Assert.That(tween, Is.Not.Null);
            Assert.That(new SerializedObject(tween).FindProperty("emitter").objectReferenceValue, Is.EqualTo(emitter));
            foreach (Transform child in attack.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject), Is.Zero, child.name);
                Assert.That(child.GetComponents<Component>().Any(component => component != null &&
                    component.GetType().Name == "NetworkIdentity"), Is.False, child.name);
            }
            ParticleSystem container = attack.transform.Find("Root").GetComponent<ParticleSystem>();
            ParticleSystemRenderer containerRenderer = container.GetComponent<ParticleSystemRenderer>();
            Assert.That(container.emission.enabled, Is.False);
            Assert.That(containerRenderer.enabled, Is.False);
            Assert.That(containerRenderer.sharedMaterials, Has.Length.EqualTo(1));
            Assert.That(containerRenderer.sharedMaterials[0], Is.Null,
                "The original disabled Root particle container intentionally has no rendering material.");
            foreach (Material material in attack.GetComponentsInChildren<Renderer>(true)
                         .Where(renderer => renderer != containerRenderer).SelectMany(renderer => renderer.sharedMaterials))
            {
                Assert.That(material, Is.Not.Null);
                Assert.That(material.shader, Is.Not.Null);
                Assert.That(material.shader.name, Does.StartWith("AllIn1"));
            }
            Assert.DoesNotThrow(DanteCirclingNativeGasMigration.ValidateImportedAssets);
        }

        [Test]
        public void OpaqueBlackSparkAtlasUsesExplicitOpacityFallbackWithoutChangingItsTexture()
        {
            var renderer = Attack().transform.Find("Root/Scale/Main/BurstSparks").GetComponent<ParticleSystemRenderer>();
            Assert.That(renderer.enabled, Is.True);
            Material material = renderer.sharedMaterial;
            Assert.That(material.shader.name, Is.EqualTo("AllIn1Vfx/AllIn1VfxURPCompat"));
            Assert.That(material.IsKeywordEnabled("PREMULTIPLYCOLOR_ON"), Is.True,
                "This shader feature derives alpha from luminance; an opaque black atlas otherwise draws black billboards.");
            Assert.That(material.GetFloat("_SrcMode"), Is.EqualTo(5f));
            Assert.That(material.GetFloat("_DstMode"), Is.EqualTo(10f));
            string texturePath = AssetDatabase.GetAssetPath(material.GetTexture("_MainTex"));
            Assert.That(AssetDatabase.AssetPathToGUID(texturePath), Is.EqualTo("52ddf8f8ef713e14cba057c107c4a1cc"));
            var pixels = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(ImageConversion.LoadImage(pixels, File.ReadAllBytes(texturePath)), Is.True);
                Color32[] colors = pixels.GetPixels32();
                Assert.That(pixels.width, Is.EqualTo(256));
                Assert.That(pixels.height, Is.EqualTo(256));
                Assert.That(colors.All(color => color.a == 255), Is.True,
                    "Preserve the source atlas: do not silently replace its alpha or paint out the background.");
                Assert.That(colors.Count(color => color.r < 4 && color.g < 4 && color.b < 4), Is.GreaterThan(40000));
                Assert.That(colors.Any(color => color.r > 200 && color.g > 100), Is.True,
                    "The original bright spark content must remain visible through the opacity fallback.");
            }
            finally { Object.DestroyImmediate(pixels); }
        }

        [TestCase("FX_MT_FireSparks_01_5.mat", 10f)]
        [TestCase("FX_MT_CausticTrails_02.mat", 10f)]
        [TestCase("FX_MT_SimpleGlow_06.mat", 1f)]
        [TestCase("FX_MT_ImpactLight.mat", 1f)]
        public void OpacityFallbackDoesNotReplaceExistingTextureAlphaOrAdditiveGlows(string name, float destinationBlend)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(DanteCirclingNativeGasMigration.OutputFolder + "/Material/" + name);
            Assert.That(material, Is.Not.Null);
            Assert.That(material.GetFloat("_SrcMode"), Is.EqualTo(5f));
            Assert.That(material.GetFloat("_DstMode"), Is.EqualTo(destinationBlend));
            Assert.That(material.IsKeywordEnabled("PREMULTIPLYCOLOR_ON"), Is.False);
        }

        [Test]
        public void NativeDatabaseKeepsExistingWeaponsAndAddsCirclingOnce()
        {
            WeaponDB database = AssetDatabase.LoadAssetAtPath<WeaponDB>(DanteNativeGasMigration.NativeWeaponDatabasePath);
            Assert.That(database, Is.Not.Null);
            Assert.That(database.Weapons.Select(entry => entry.ID), Is.SupersetOf(new uint[] { 2, 1, 3, 6 }));
            Assert.That(database.Weapons.Take(3).Select(entry => entry.ID), Is.EqualTo(new uint[] { 2, 1, 3 }));
            Assert.That(database.Weapons.Count(entry => entry.ID == 6), Is.EqualTo(1));
            Assert.That(database.Weapons.Single(entry => entry.ID == 6), Is.EqualTo(Weapon()));
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(DanteNativeGasMigration.NetworkPlayerPrefabPath);
            Assert.That(player, Is.Not.Null);
            Component build = player.GetComponent("PlayerBuildRuntime");
            Assert.That(build, Is.Not.Null);
            Assert.That(new SerializedObject(build).FindProperty("initialWeaponId").intValue, Is.EqualTo(2));
        }

        private static WeaponData Weapon()
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(DanteCirclingNativeGasMigration.WeaponPath);
            Assert.That(weapon, Is.Not.Null, "Run the focused Dante circling importer before these asset checks.");
            return weapon;
        }
        private static AnimatedAttack Attack() => AssetDatabase.LoadAssetAtPath<GameObject>(DanteCirclingNativeGasMigration.AttackPath)
            .GetComponent<AnimatedAttack>();
        private static AnimationClip Clip(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            Assert.That(clip, Is.Not.Null);
            return clip;
        }
    }
}
