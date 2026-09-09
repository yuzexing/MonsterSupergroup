using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using MonsterSupergroup.HellMaidenMigration.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.HellMaidenMigration.Tests
{
    public sealed class OvidSummonNativeGasMigrationTests
    {
        [TestCase(OvidSummonNativeGasMigration.DefaultAttackPath)]
        [TestCase(OvidSummonNativeGasMigration.FireAttackPath)]
        [TestCase(OvidSummonNativeGasMigration.PoisonAttackPath)]
        public void ShadowRetainsSourceGeometryTextureAndParametersWithAnExplicitFadeApproximation(string path)
        {
            SpriteRenderer shadow = Variant(path).transform.Find(OvidSummonNativeGasMigration.ShadowPath).GetComponent<SpriteRenderer>();
            Assert.That(shadow.transform.localPosition, Is.EqualTo(new Vector3(0, -18, -0.6f)));
            Assert.That(shadow.transform.localScale, Is.EqualTo(new Vector3(7.5f, 1, 1)));
            Assert.That(shadow.color, Is.EqualTo(Color.white));
            Material material = shadow.sharedMaterial;
            Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material)), Is.EqualTo("ef5dd6be7c703534bb52398357fa94ed"));
            Assert.That(material.shader, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Shader>(OvidSummonNativeGasMigration.ShadowShaderPath)));
            Assert.That(material.GetTexture("_MainTex"), Is.SameAs(shadow.sprite.texture));
            Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(shadow.sprite.texture)), Is.EqualTo("4f47ced8ac09c7d44b968acaa95ce587"));
            Assert.That(material.GetColor("_Color"), Is.EqualTo(Color.white), "The source shadow is not an opaque black tint.");
            Assert.That(material.GetFloat("_ShadowFadeStart"), Is.EqualTo(0.16f).Within(0.00001f));
            Assert.That(material.GetFloat("_ShadowFadeEnd"), Is.EqualTo(1));
            Assert.That(material.GetFloat("_EnableDirectionalAlphaFade"), Is.EqualTo(1));
            Assert.That(material.GetFloat("_DirectionalAlphaFadeFade"), Is.EqualTo(2.61f));
            Assert.That(material.GetFloat("_DirectionalAlphaFadeRotation"), Is.EqualTo(263));
            Assert.That(material.GetFloat("_DirectionalAlphaFadeWidth"), Is.EqualTo(0.84f));
            Assert.That(material.GetFloat("_DirectionalAlphaFadeNoiseFactor"), Is.EqualTo(0.2f));
            Assert.That(OvidSummonNativeGasMigration.RestorationLimits, Does.Contain("approximation"));
            foreach (Renderer renderer in Variant(path).GetComponentsInChildren<Renderer>(true).Where(value => value != shadow))
                Assert.That(renderer.sharedMaterials.Where(value => value != null).All(value => value.shader != material.shader), Is.True,
                    "The directional multiply approximation belongs only to the authored Shadow renderer.");
        }

        [Test]
        public void SourceDefinitionNormalizesZeroCountToOneSummonAndKeepsAllOtherStats()
        {
            WeaponData weapon = Weapon();
            Assert.That(weapon.ID, Is.EqualTo(402));
            Assert.That(weapon.BaseStats.damage, Is.EqualTo(24));
            Assert.That(weapon.BaseStats.critRate, Is.Zero);
            Assert.That(weapon.BaseStats.critMultiplier, Is.EqualTo(1f));
            Assert.That(weapon.BaseStats.speed, Is.EqualTo(0.2f));
            Assert.That(weapon.BaseStats.size, Is.EqualTo(1f));
            Assert.That(weapon.BaseStats.duration, Is.EqualTo(1f));
            Assert.That(weapon.BaseStats.projectileCount, Is.EqualTo(1),
                "Source ID 402 has zero count; the Native definition explicitly represents its one actual summon.");
            Assert.That(weapon.BaseStats.knockbackDistance, Is.EqualTo(0.5f));
            Assert.That(weapon.Presentation.Knockback.distance, Is.EqualTo(0.5f));
            Assert.That(weapon.Presentation.Knockback.speedMultiplier, Is.EqualTo(10f));
            Assert.That(weapon.AttackTags, Is.EqualTo(CombatTags.Attack));
            Assert.That((int)weapon.modifierFlags, Is.EqualTo(239));
            Assert.That(weapon.UltimateData, Is.Null);
            Assert.DoesNotThrow(weapon.ValidateNativeGas);
            Assert.That(OvidSummonNativeGasMigration.RestorationLimits, Does.Contain("ProjectileCount=0"));
        }

        [Test]
        public void SourceSixtySecondCocoonAndThreeVariantsArePreserved()
        {
            Assert.That(Weapon().WeaponPrefab, Is.TypeOf<OvidSummonAttackBehaviour>());
            var emitter = new SerializedObject(Weapon().WeaponPrefab);
            Assert.That(emitter.FindProperty("cacoonStateTime").floatValue, Is.EqualTo(60f));
            string[] fields = { "variants.defaultPrefab", "variants.firePrefab", "variants.poisonPrefab" };
            string[] paths = Variants();
            for (int index = 0; index < fields.Length; index++)
                Assert.That(emitter.FindProperty(fields[index]).objectReferenceValue,
                    Is.EqualTo(Variant(paths[index]).GetComponent<SummonAIBehaviour>()));
            Assert.That(emitter.FindProperty("variants.allowFire").boolValue, Is.True);
            Assert.That(emitter.FindProperty("variants.allowPoison").boolValue, Is.True);
        }

        [TestCase(OvidSummonNativeGasMigration.DefaultAttackPath)]
        [TestCase(OvidSummonNativeGasMigration.FireAttackPath)]
        [TestCase(OvidSummonNativeGasMigration.PoisonAttackPath)]
        public void MissingModuleReferencesFollowTheAuthoredHierarchy(string path)
        {
            GameObject root = Variant(path);
            var ai = new SerializedObject(root.GetComponent<SummonAIBehaviour>());
            var idle = root.GetComponentInChildren<OvidSummonIdleModule>(true);
            var attack = root.GetComponentInChildren<OvidSummonAttackModule>(true);
            var mover = root.GetComponentInChildren<OvidSummonMover>(true);
            var positioning = root.GetComponentInChildren<OvidSummonPositioningModule>(true);
            Transform visual = root.transform.Find(OvidSummonNativeGasMigration.VisualRootPath);
            Component animancer = visual.GetComponent("AnimancerComponent");
            Animator animator = visual.GetComponent<Animator>();
            Assert.That(ai.FindProperty("idlingStateModule").objectReferenceValue, Is.EqualTo(idle));
            Assert.That(ai.FindProperty("attackingStateModule").objectReferenceValue, Is.EqualTo(attack));
            Assert.That(ai.FindProperty("positioningStateModule").objectReferenceValue, Is.EqualTo(positioning));
            Assert.That(ai.FindProperty("animancer").objectReferenceValue, Is.EqualTo(animancer));
            Assert.That(new SerializedObject(animancer).FindProperty("_Animator").objectReferenceValue, Is.EqualTo(animator));
            Assert.That(animator.runtimeAnimatorController, Is.Null);
            Assert.That(animator.updateMode, Is.EqualTo(AnimatorUpdateMode.Normal));
            var moverFields = new SerializedObject(mover);
            Assert.That(moverFields.FindProperty("rb").objectReferenceValue, Is.EqualTo(root.GetComponent<Rigidbody2D>()));
            Assert.That(moverFields.FindProperty("isoPivot").objectReferenceValue, Is.EqualTo(root.transform.Find("Iso Rotation")));
            Assert.That(moverFields.FindProperty("rotationPivot").objectReferenceValue, Is.EqualTo(visual));
            Assert.That(Quaternion.Angle(root.transform.Find("Iso Rotation").localRotation, Quaternion.Euler(-45f, 0f, 0f)), Is.LessThan(0.01f));
            Assert.That(visual.localPosition, Is.EqualTo(new Vector3(0f, 4.5f, 0f)));
            Assert.That(Quaternion.Angle(visual.localRotation, Quaternion.identity), Is.LessThan(0.01f));
            Assert.That(new SerializedObject(idle).FindProperty("mover").objectReferenceValue, Is.EqualTo(mover));
            Assert.That(new SerializedObject(attack).FindProperty("mover").objectReferenceValue, Is.EqualTo(mover));
            Assert.That(new SerializedObject(positioning).FindProperty("mover").objectReferenceValue, Is.EqualTo(mover));
            Assert.That(new SerializedObject(attack).FindProperty("hitBox").objectReferenceValue,
                Is.EqualTo(root.transform.Find(OvidSummonNativeGasMigration.HitBoxPath).GetComponent<PlayerAttackOvertimeHitBox>()));
            AssertTransition(idle, "idleAnimation", OvidSummonNativeGasMigration.CacoonClipPath);
            AssertTransition(idle, "birthAnimation", OvidSummonNativeGasMigration.BirthClipPath);
            AssertTransition(attack, "attackEnterAnimation", OvidSummonNativeGasMigration.EnterClipPath);
            AssertTransition(attack, "attackLoopAnimation", OvidSummonNativeGasMigration.MainClipPath);
            AssertTransition(attack, "attackExitAnimation", OvidSummonNativeGasMigration.ExitClipPath);
            AssertTransition(mover, "moveAnimation", OvidSummonNativeGasMigration.MoveClipPath);
        }

        [TestCase(OvidSummonNativeGasMigration.DefaultAttackPath)]
        [TestCase(OvidSummonNativeGasMigration.FireAttackPath)]
        [TestCase(OvidSummonNativeGasMigration.PoisonAttackPath)]
        public void MissingCurvesUseExecutableLinearDefaultsWhilePositioningKeepsItsOverrides(string path)
        {
            GameObject root = Variant(path);
            var mover = root.GetComponentInChildren<OvidSummonMover>(true);
            var attack = root.GetComponentInChildren<OvidSummonAttackModule>(true);
            AssertLinear(mover, "accelerationCurve");
            AssertLinear(attack, "sweepAccelerationCurve");
            var moving = new SerializedObject(mover);
            Assert.That(moving.FindProperty("maxMoveSpeed").floatValue, Is.EqualTo(12f));
            Assert.That(moving.FindProperty("minMoveSpeed").floatValue, Is.EqualTo(0.5f));
            Assert.That(moving.FindProperty("maxSpeedDistance").floatValue, Is.EqualTo(20f));
            Assert.That(moving.FindProperty("cacoonSpeedMultiplier").floatValue, Is.EqualTo(2f));
            Assert.That(moving.FindProperty("tiltAngle").floatValue, Is.EqualTo(-45f));
            Assert.That(moving.FindProperty("minAnimSpeed").floatValue, Is.EqualTo(1f));
            Assert.That(moving.FindProperty("maxAnimSpeed").floatValue, Is.EqualTo(2f));
            var attacking = new SerializedObject(attack);
            Assert.That(attacking.FindProperty("minDetectionRadius").floatValue, Is.EqualTo(5f));
            Assert.That(attacking.FindProperty("maxDetectionRadius").floatValue, Is.EqualTo(10f));
            Assert.That(attacking.FindProperty("clusterSearchRadius").floatValue, Is.EqualTo(4f));
            Assert.That(attacking.FindProperty("sweepAngle").floatValue, Is.EqualTo(30f));
            Assert.That(attacking.FindProperty("framePartitioningCount").intValue, Is.EqualTo(4));
            Assert.That(attacking.FindProperty("maxEnemiesToProcess").intValue, Is.EqualTo(100));
            var positioning = new SerializedObject(root.GetComponentInChildren<OvidSummonPositioningModule>(true));
            Assert.That(positioning.FindProperty("stopDistance").floatValue, Is.EqualTo(4.5f));
            Assert.That(positioning.FindProperty("optimalAttackDistance").floatValue, Is.EqualTo(7f));
            Assert.That(positioning.FindProperty("minDetectionRadius").floatValue, Is.EqualTo(3f));
            Assert.That(positioning.FindProperty("maxDetectionRadius").floatValue, Is.EqualTo(15f));
        }

        [TestCase(OvidSummonNativeGasMigration.CacoonClipPath, 4f, true)]
        [TestCase(OvidSummonNativeGasMigration.BirthClipPath, 3.8f, false)]
        [TestCase(OvidSummonNativeGasMigration.MoveClipPath, 1.0333334f, true)]
        [TestCase(OvidSummonNativeGasMigration.EnterClipPath, 1f, false)]
        [TestCase(OvidSummonNativeGasMigration.MainClipPath, 0.33333334f, false)]
        [TestCase(OvidSummonNativeGasMigration.ExitClipPath, 0.6333334f, false)]
        public void OriginalClipTimingLoopFlagsAndBindingTargetsAreRetained(string path, float duration, bool looping)
        {
            AnimationClip clip = Clip(path);
            Assert.That(clip.length, Is.EqualTo(duration).Within(0.0001f));
            Assert.That(clip.isLooping, Is.EqualTo(looping));
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty);
            foreach (string variant in Variants())
            {
                Transform visual = Variant(variant).transform.Find(OvidSummonNativeGasMigration.VisualRootPath);
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                    Assert.That(string.IsNullOrEmpty(binding.path) || visual.Find(binding.path) != null, Is.True,
                        path + ": missing original animation target " + binding.path);
            }
        }

        [Test]
        public void SourceHitWindowsIncludeTheEndOfEnterAndBeginningOfExit()
        {
            foreach (string collider in new[] { "TopCollider", "AngleCollider" })
            {
                AnimationCurve enter = ColliderCurve(OvidSummonNativeGasMigration.EnterClipPath, collider);
                AnimationCurve loop = ColliderCurve(OvidSummonNativeGasMigration.MainClipPath, collider);
                AnimationCurve exit = ColliderCurve(OvidSummonNativeGasMigration.ExitClipPath, collider);
                Assert.That(enter.Evaluate(0.65f), Is.Zero);
                Assert.That(enter.Evaluate(0.68f), Is.EqualTo(1f));
                Assert.That(enter.keys.First(key => key.value == 1f).time, Is.EqualTo(0.6666667f).Within(0.0001f));
                Assert.That(loop.keys.All(key => key.value == 1f), Is.True);
                Assert.That(exit.Evaluate(0.32f), Is.EqualTo(1f));
                Assert.That(exit.Evaluate(0.35f), Is.Zero);
                Assert.That(exit.keys.First(key => key.value == 0f).time, Is.EqualTo(0.33333334f).Within(0.0001f));
                foreach (string idle in new[] { OvidSummonNativeGasMigration.CacoonClipPath,
                             OvidSummonNativeGasMigration.BirthClipPath, OvidSummonNativeGasMigration.MoveClipPath })
                    Assert.That(ColliderCurve(idle, collider).keys.All(key => key.value == 0f), Is.True);
            }
        }

        [TestCase(OvidSummonNativeGasMigration.DefaultAttackPath)]
        [TestCase(OvidSummonNativeGasMigration.FireAttackPath)]
        [TestCase(OvidSummonNativeGasMigration.PoisonAttackPath)]
        public void OriginalOvertimeGeometryAndSizeOnlyScalingArePreserved(string path)
        {
            GameObject root = Variant(path);
            Transform hit = root.transform.Find(OvidSummonNativeGasMigration.HitBoxPath);
            var hitbox = new SerializedObject(hit.GetComponent<PlayerAttackOvertimeHitBox>());
            var top = hit.Find("TopCollider").GetComponent<BoxCollider2D>();
            var angle = hit.Find("AngleCollider").GetComponent<PolygonCollider2D>();
            Assert.That(hitbox.FindProperty("collider").objectReferenceValue, Is.EqualTo(top));
            Assert.That(hitbox.FindProperty("otherColliders").arraySize, Is.EqualTo(1));
            Assert.That(hitbox.FindProperty("otherColliders.Array.data[0]").objectReferenceValue, Is.EqualTo(angle));
            Assert.That(hitbox.FindProperty("hitInterval").floatValue, Is.EqualTo(0.5f));
            Assert.That(hitbox.FindProperty("timeoutAfterExit").floatValue, Is.EqualTo(1.5f));
            Assert.That(hitbox.FindProperty("disableAfterTimeout").boolValue, Is.False);
            Assert.That(top.isTrigger, Is.True);
            Assert.That(angle.isTrigger, Is.False,
                "The source AngleCollider is a non-trigger PolygonCollider2D; only TopCollider is a trigger.");
            Assert.That(top.size, Is.EqualTo(new Vector2(1.25f, 13f)));
            Assert.That(top.transform.localPosition, Is.EqualTo(new Vector3(0f, -6.58f, -1.5f)));
            Assert.That(angle.GetPath(0), Is.EqualTo(new[] { new Vector2(0.1f, -13.3f), new Vector2(2f, -13f),
                new Vector2(2f, 0f), new Vector2(-1.7f, -0.7f) }));
            Assert.That(Quaternion.Angle(angle.transform.localRotation, Quaternion.Euler(0f, 90f, 0f)), Is.LessThan(0.01f));
            var scaler = new SerializedObject(root.GetComponentInChildren<AttackProgressionScaler>(true));
            Assert.That(scaler.FindProperty("sizeTransforms").arraySize, Is.EqualTo(1));
            Assert.That(scaler.FindProperty("sizeCustomScalers").arraySize, Is.EqualTo(2));
            Assert.That(scaler.FindProperty("sizeCustomScalers.Array.data[0]").objectReferenceValue,
                Is.EqualTo(scaler.FindProperty("sizeCustomScalers.Array.data[1]").objectReferenceValue),
                "Preserve the source's duplicate reference; its scaler assigns width absolutely.");
            foreach (string field in new[] { "speedTransforms", "speedParticleSystemProperties", "speedShaderProperties", "speedCustomScalers" })
                Assert.That(scaler.FindProperty(field).arraySize, Is.Zero, field);
            var lineScaler = root.GetComponentInChildren<LineRendererProgressionScaler>(true);
            Assert.That(lineScaler.defaultWidth, Is.EqualTo(2.5f));
            Assert.That(lineScaler.lineRenderer, Is.EqualTo(root.GetComponentInChildren<LineRenderer>(true)));
        }

        [TestCase(OvidSummonNativeGasMigration.DefaultAttackPath)]
        [TestCase(OvidSummonNativeGasMigration.FireAttackPath)]
        [TestCase(OvidSummonNativeGasMigration.PoisonAttackPath)]
        public void OriginalHierarchyAndVisualDependenciesRetainOnlyTheTwoAuthoredNullContainers(string path)
        {
            GameObject root = Variant(path);
            Assert.That(root.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(66));
            Assert.That(root.GetComponentsInChildren<ParticleSystem>(true).Length, Is.EqualTo(15));
            Assert.That(root.GetComponentsInChildren<Renderer>(true).Length, Is.EqualTo(20));
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject), Is.Zero, child.name);
                Assert.That(child.GetComponents<Component>().Any(component => component != null &&
                    component.GetType().Name == "NetworkIdentity"), Is.False, child.name);
            }
            string[] containers = { OvidSummonNativeGasMigration.VisualRootPath + "/Power_Burst_V3",
                OvidSummonNativeGasMigration.VisualRootPath + "/Buterfly_Attack Laser" };
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                string rendererPath = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
                if (containers.Contains(rendererPath))
                {
                    Assert.That(renderer.enabled, Is.False, rendererPath);
                    Assert.That(renderer.GetComponent<ParticleSystem>().emission.enabled, Is.False, rendererPath);
                    Assert.That(renderer.sharedMaterials, Has.Length.EqualTo(1));
                    Assert.That(renderer.sharedMaterial, Is.Null);
                    continue;
                }
                foreach (Material material in renderer.sharedMaterials)
                {
                    Assert.That(material, Is.Not.Null, rendererPath);
                    Assert.That(material.shader, Is.Not.Null, rendererPath);
                    if (rendererPath == OvidSummonNativeGasMigration.VisualRootPath + "/Buterfly_Attack Laser/Rotate/Shadow")
                        Assert.That(material.shader.name, Is.EqualTo("HellMaiden/Presentation/Ovid Summon Shadow"), rendererPath);
                    else
                        Assert.That(material.shader.name, Does.StartWith("AllIn1"), rendererPath);
                }
            }
        }

        [TestCase(OvidSummonNativeGasMigration.DefaultAttackPath, "fa6ae21672f404249ae45ab41be660e0")]
        [TestCase(OvidSummonNativeGasMigration.FireAttackPath, "38607dcfdf75a2c4685f154c316505cc")]
        [TestCase(OvidSummonNativeGasMigration.PoisonAttackPath, "9b127916ab03f2d499da3a19d842df1e")]
        public void BeamVariantsKeepTheirDistinctTexturesAndOriginalUvScroll(string path, string textureGuid)
        {
            Material material = Variant(path).GetComponentInChildren<LineRenderer>(true).sharedMaterial;
            Assert.That(material.shader.name, Is.EqualTo("AllIn1Vfx/AllIn1VfxURPCompat"));
            Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material.GetTexture("_MainTex"))), Is.EqualTo(textureGuid));
            Assert.That(material.GetTextureScale("_MainTex"), Is.EqualTo(new Vector2(0.04f, 1f)));
            Assert.That(material.GetFloat("_ShapeXSpeed"), Is.EqualTo(-5f));
            Assert.That(material.GetFloat("_ShapeYSpeed"), Is.Zero);
            Assert.That(material.GetFloat("_SrcMode"), Is.EqualTo(5f));
            Assert.That(material.GetFloat("_DstMode"), Is.EqualTo(10f));
            Material sparks = AssetDatabase.LoadAssetAtPath<Material>(OvidSummonNativeGasMigration.OutputFolder + "/Material/Spark_0.mat");
            Assert.That(sparks, Is.Not.Null);
            Assert.That(sparks.IsKeywordEnabled("PREMULTIPLYCOLOR_ON"), Is.True,
                "The source grain atlas has opaque alpha even on its black background.");
        }

        [TestCase(OvidSummonNativeGasMigration.DefaultAttackPath)]
        [TestCase(OvidSummonNativeGasMigration.FireAttackPath)]
        [TestCase(OvidSummonNativeGasMigration.PoisonAttackPath)]
        public void ExistingAudioReferencesRemainAndMissingBeamSoundIsNotInvented(string path)
        {
            GameObject root = Variant(path);
            string visual = OvidSummonNativeGasMigration.VisualRootPath;
            Component wing = root.transform.Find(visual + "/Sound/WingFlap").GetComponent("StudioEventEmitter");
            Component birth = root.transform.Find(visual + "/Sound/Transform").GetComponent("StudioEventEmitter");
            AssertEvent(wing, new[] { 202224117, 1168404540, 270208185, 1868296016 });
            AssertEvent(birth, new[] { 1517815308, 1157615173, 1974035375, 693225658 });
            var attack = new SerializedObject(root.GetComponentInChildren<OvidSummonAttackModule>(true));
            for (int i = 1; i <= 4; i++) Assert.That(attack.FindProperty("beamSound.Guid.Data" + i).intValue, Is.Zero);
            AudioSource audio = root.transform.Find(visual + "/Power_Burst_V3/OrbitalBeamSmallYellow").GetComponent<AudioSource>();
            Assert.That(audio, Is.Not.Null);
            Assert.That(audio.playOnAwake, Is.True);
            Assert.That(audio.loop, Is.False);
            Object resource = new SerializedObject(audio).FindProperty("m_Resource").objectReferenceValue;
            Assert.That(resource, Is.TypeOf<AudioClip>());
            Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(resource)), Is.EqualTo("43b9d6a4470d26741960d741524726cb"));
        }

        [Test]
        public void NativeDatabaseAppendsSummonOnceWithoutReplacingExistingWeaponsOrPlayerDefault()
        {
            WeaponDB database = AssetDatabase.LoadAssetAtPath<WeaponDB>(DanteNativeGasMigration.NativeWeaponDatabasePath);
            Assert.That(database.Weapons.Select(weapon => weapon.ID), Is.SupersetOf(new uint[] { 2, 1, 3, 6, 402 }));
            Assert.That(database.Weapons.Take(3).Select(weapon => weapon.ID), Is.EqualTo(new uint[] { 2, 1, 3 }));
            Assert.That(database.Weapons.Count(weapon => weapon.ID == 402), Is.EqualTo(1));
            Assert.That(database.Weapons.Single(weapon => weapon.ID == 402), Is.EqualTo(Weapon()));
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(DanteNativeGasMigration.NetworkPlayerPrefabPath);
            Assert.That(new SerializedObject(player.GetComponent("PlayerBuildRuntime")).FindProperty("initialWeaponId").intValue, Is.EqualTo(2));
            Assert.DoesNotThrow(OvidSummonNativeGasMigration.ValidateImportedAssets);
        }

        private static void AssertEvent(Component emitter, int[] guid)
        {
            Assert.That(emitter, Is.Not.Null);
            var serialized = new SerializedObject(emitter);
            for (int i = 0; i < guid.Length; i++)
                Assert.That(serialized.FindProperty("EventReference.Guid.Data" + (i + 1)).intValue, Is.EqualTo(guid[i]));
            Assert.That(serialized.FindProperty("EventPlayTrigger").intValue, Is.Zero);
            Assert.That(serialized.FindProperty("EventStopTrigger").intValue, Is.Zero);
        }
        private static void AssertTransition(Component component, string field, string path)
        {
            var serialized = new SerializedObject(component);
            Assert.That(serialized.FindProperty(field + "._Clip").objectReferenceValue, Is.EqualTo(Clip(path)));
            Assert.That(serialized.FindProperty(field + "._Speed").floatValue, Is.EqualTo(1f));
            Assert.That(serialized.FindProperty(field + "._FadeDuration").floatValue, Is.Zero);
            Assert.That(serialized.FindProperty(field + "._NormalizedStartTime").floatValue, Is.Zero);
        }
        private static void AssertLinear(Component component, string field)
        {
            var curve = (CustomAnimationCurve)component.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(component);
            Assert.That(curve, Is.Not.Null);
            Assert.That(curve.useOwnAnimationCurve, Is.False);
            foreach (float t in new[] { 0f, 0.25f, 0.75f, 1f }) Assert.That(curve.EasePercentage(t), Is.EqualTo(t).Within(0.0001f));
        }
        private static AnimationCurve ColliderCurve(string path, string collider)
        {
            AnimationClip clip = Clip(path);
            EditorCurveBinding binding = AnimationUtility.GetCurveBindings(clip).Single(candidate =>
                candidate.path == "Buterfly_Attack Laser/Rotate/Hitbox/" + collider && candidate.propertyName == "m_Enabled");
            return AnimationUtility.GetEditorCurve(clip, binding);
        }
        private static string[] Variants() => new[] { OvidSummonNativeGasMigration.DefaultAttackPath,
            OvidSummonNativeGasMigration.FireAttackPath, OvidSummonNativeGasMigration.PoisonAttackPath };
        private static WeaponData Weapon() => AssetDatabase.LoadAssetAtPath<WeaponData>(OvidSummonNativeGasMigration.WeaponPath);
        private static AnimationClip Clip(string path)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            Assert.That(clip, Is.Not.Null, "Run the focused Ovid summon importer first: " + path);
            return clip;
        }
        private static GameObject Variant(string path)
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(root, Is.Not.Null, "Run the focused Ovid summon importer first: " + path);
            return root;
        }
    }
}
