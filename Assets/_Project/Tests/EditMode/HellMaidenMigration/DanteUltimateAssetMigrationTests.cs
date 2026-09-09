using System.Linq;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.HellMaidenMigration.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MonsterSupergroup.HellMaidenMigration.Tests
{
    public sealed class DanteUltimateAssetMigrationTests
    {
        [Test]
        public void DefinitionRetainsSourceUltimateIdentityAndStatsWithoutCreatingAWeaponDefinition()
        {
            UltimateData definition = Asset<UltimateData>(DanteUltimateAssetMigration.DefinitionPath);
            var serialized = new SerializedObject(definition);
            Assert.That(definition.Id, Is.Zero);
            Assert.That(serialized.FindProperty("title").stringValue, Is.EqualTo("Dante's Inferno"));
            Assert.That(serialized.FindProperty("baseStats.damage").intValue, Is.EqualTo(100));
            Assert.That(serialized.FindProperty("baseStats.critRate").floatValue, Is.Zero);
            Assert.That(serialized.FindProperty("baseStats.critMultiplier").floatValue, Is.EqualTo(1f));
            foreach (string field in new[] { "speed", "size", "duration" })
                Assert.That(serialized.FindProperty("baseStats." + field).floatValue, Is.EqualTo(1f), field);
            Assert.That(serialized.FindProperty("baseStats.projectileCount").intValue, Is.EqualTo(1));
            Assert.That(serialized.FindProperty("baseStats.damageType").intValue, Is.Zero);
            Assert.That(serialized.FindProperty("baseStats.cameraShakePerLevelIncrement").floatValue, Is.EqualTo(0.1f));
            Object knockback = serialized.FindProperty("baseStats.knockbackSettings").objectReferenceValue;
            Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(knockback)), Is.EqualTo("fa1ccdc3a358d964ba88355c94961878"));
            Assert.That(new SerializedObject(knockback).FindProperty("distance").floatValue, Is.Zero);
            Assert.That(AssetDatabase.FindAssets("t:WeaponData", new[] { DanteUltimateAssetMigration.OutputFolder }), Is.Empty);
            Assert.That(definition.ultimateAttackEvents, Is.Null,
                "The source UI/video flow is explicitly deferred, rather than pulled into the gameplay attack closure.");
            Assert.That(DanteUltimateAssetMigration.RestorationLimits, Does.Contain(DanteUltimateAssetMigration.DeferredUiPrefabGuid));
        }

        [Test]
        public void OriginalRootReferencesAndSeparateKnockbackRadiusAndDistanceArePreserved()
        {
            DanteUltimateAttack main = Main();
            UltimateDamageAttack wave = Wave();
            var fields = new SerializedObject(main);
            Assert.That(main.ultimateData, Is.EqualTo(Asset<UltimateData>(DanteUltimateAssetMigration.DefinitionPath)));
            Assert.That(main.ultimateData.ultimateAttackWeaponBehaviour, Is.EqualTo(main));
            Assert.That(main.DanteUltimateWavePrefab, Is.EqualTo(wave.gameObject));
            Assert.That(fields.FindProperty("knockbackRadius").floatValue, Is.EqualTo(5f));
            Assert.That(fields.FindProperty("enemyLayers").intValue, Is.EqualTo(128));
            Object settings = fields.FindProperty("knockbackSettings").objectReferenceValue;
            Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(settings)), Is.EqualTo("43aa8a0ec6989704f884be1042a00ded"));
            var knockback = new SerializedObject(settings);
            Assert.That(knockback.FindProperty("distance").floatValue, Is.EqualTo(2f));
            Assert.That(knockback.FindProperty("speedMultiplier").floatValue, Is.EqualTo(6f));
            Assert.That(knockback.FindProperty("staggerTime").floatValue, Is.EqualTo(0.6f));
            Assert.That(fields.FindProperty("slowMoSafetyDelay").floatValue, Is.EqualTo(1f));
            Assert.That(fields.FindProperty("invulnerabilitySafetyDelay").floatValue, Is.EqualTo(3f));
            Assert.That(main.burnStrength, Is.EqualTo(0.1f));
            Assert.That(main.burnDuration, Is.EqualTo(4f));
            Assert.That(main.burnRate, Is.EqualTo(0.5f));
            int[] guid = { 16352981, 1122692763, 785237127, -1722794395 };
            for (int index = 0; index < guid.Length; index++)
                Assert.That(fields.FindProperty("attackSound.Guid.Data" + (index + 1)).intValue, Is.EqualTo(guid[index]));
        }

        [Test]
        public void OriginalControllersKeepTheAttackTriggerAndUnscaledAnimationClocks()
        {
            DanteUltimateAttack main = Main();
            UltimateDamageAttack wave = Wave();
            Assert.That(main.animator, Is.EqualTo(main.GetComponent<Animator>()));
            Assert.That(wave.animator, Is.EqualTo(wave.GetComponent<Animator>()));
            Assert.That(main.animator.updateMode, Is.EqualTo(AnimatorUpdateMode.UnscaledTime));
            Assert.That(wave.animator.updateMode, Is.EqualTo(AnimatorUpdateMode.UnscaledTime));
            AnimatorController mainController = Asset<AnimatorController>(DanteUltimateAssetMigration.MainControllerPath);
            AnimatorController waveController = Asset<AnimatorController>(DanteUltimateAssetMigration.WaveControllerPath);
            Assert.That(main.animator.runtimeAnimatorController, Is.EqualTo(mainController));
            Assert.That(wave.animator.runtimeAnimatorController, Is.EqualTo(waveController));
            Assert.That(mainController.parameters.Single(parameter => parameter.name == "Attack").type,
                Is.EqualTo(AnimatorControllerParameterType.Trigger));
            Assert.That(mainController.layers, Has.Length.EqualTo(1));
            AnimatorStateMachine mainStates = mainController.layers[0].stateMachine;
            Assert.That(mainStates.defaultState.motion, Is.EqualTo(Clip(DanteUltimateAssetMigration.IdleClipPath)));
            Assert.That(mainStates.states.Select(state => state.state.motion), Does.Contain(Clip(DanteUltimateAssetMigration.MainClipPath)));
            Assert.That(mainStates.states.Select(state => state.state.speed), Is.All.EqualTo(1f));
            Assert.That(waveController.layers[0].stateMachine.defaultState.motion, Is.EqualTo(Clip(DanteUltimateAssetMigration.WaveClipPath)));
            Assert.That(waveController.layers[0].stateMachine.defaultState.speed, Is.EqualTo(1f));
        }

        [TestCase(DanteUltimateAssetMigration.MainClipPath, 4.0333333f, false, DanteUltimateAssetMigration.MainAttackPath)]
        [TestCase(DanteUltimateAssetMigration.IdleClipPath, 0f, true, DanteUltimateAssetMigration.MainAttackPath)]
        [TestCase(DanteUltimateAssetMigration.WaveClipPath, 2.5166667f, false, DanteUltimateAssetMigration.WaveAttackPath)]
        public void OriginalClipDurationsLoopFlagsAndEveryTargetTransformRemain(string path, float duration, bool loop, string prefab)
        {
            AnimationClip clip = Clip(path);
            Transform root = Asset<GameObject>(prefab).transform;
            Assert.That(clip.length, Is.EqualTo(duration).Within(0.0001f));
            Assert.That(clip.isLooping, Is.EqualTo(loop));
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip)
                         .Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
                Assert.That(string.IsNullOrEmpty(binding.path) || root.Find(binding.path) != null, Is.True,
                    path + ": missing source animation target " + binding.path + "." + binding.propertyName);
        }

        [Test]
        public void TwoWavesKeepTheirOriginalEventsAndTheSecondWaveOutlivesTheMainClip()
        {
            AnimationClip main = Clip(DanteUltimateAssetMigration.MainClipPath);
            AnimationEvent[] events = AnimationUtility.GetAnimationEvents(main);
            Assert.That(events, Has.Length.EqualTo(4));
            AnimationEvent[] spawns = events.Where(value => value.functionName == "SpawnWave").ToArray();
            Assert.That(spawns.Select(value => value.intParameter), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(spawns[0].time, Is.Zero);
            Assert.That(spawns[1].time, Is.EqualTo(2.1166666f).Within(0.0001f));
            AnimationEvent[] shakes = events.Where(value => value.functionName == "ShakeCamera").ToArray();
            Assert.That(shakes.Select(value => value.intParameter), Is.EqualTo(new[] { 2, 2 }));
            Assert.That(shakes[0].time, Is.EqualTo(0.21666667f).Within(0.0001f));
            Assert.That(shakes[1].time, Is.EqualTo(2.1166666f).Within(0.0001f));
            AnimationEvent end = AnimationUtility.GetAnimationEvents(Clip(DanteUltimateAssetMigration.WaveClipPath)).Single();
            Assert.That(end.functionName, Is.EqualTo("onAttackAnimationEnd"));
            Assert.That(end.time, Is.EqualTo(2.5f));
            Assert.That(spawns[1].time + end.time, Is.EqualTo(4.6166666f).Within(0.0001f));
            Assert.That(spawns[1].time + end.time, Is.GreaterThan(main.length),
                "The main clip and three-second invulnerability timer cannot own the second wave's lifetime.");
            Assert.That(AnimationUtility.GetAnimationEvents(Clip(DanteUltimateAssetMigration.IdleClipPath)), Is.Empty);
        }

        [Test]
        public void WaveRetainsTheTwelvePointExpandingTriggerAndSingleHitPolicy()
        {
            UltimateDamageAttack wave = Wave();
            Transform hit = wave.transform.Find("Hitbox");
            PlayerAttackHitBox hitbox = hit.GetComponent<PlayerAttackHitBox>();
            PolygonCollider2D collider = hit.GetComponent<PolygonCollider2D>();
            Assert.That(wave.hitbox, Is.EqualTo(hitbox));
            Assert.That(wave.progressionScaler, Is.Null);
            Assert.That(new SerializedObject(wave).FindProperty("hitEffectResolver").objectReferenceValue, Is.Null);
            Assert.That(collider.enabled, Is.True);
            Assert.That(collider.isTrigger, Is.True);
            Assert.That(collider.pathCount, Is.EqualTo(1));
            Assert.That(collider.GetPath(0), Is.EqualTo(new[]
            {
                new Vector2(23.69098f, -45.408096f), new Vector2(43.913017f, -24.683098f),
                new Vector2(50.91444f, -0.6230496f), new Vector2(44.420166f, 24.384733f),
                new Vector2(23.811996f, 45.016674f), new Vector2(2.2297058f, 49.832546f),
                new Vector2(-20.772703f, 45.70047f), new Vector2(-42.21074f, 28.164291f),
                new Vector2(-50.930634f, 1.1563396f), new Vector2(-43.154663f, -25.101948f),
                new Vector2(-22.77718f, -44.70628f), new Vector2(-1.641686f, -48.900448f)
            }));
            var fields = new SerializedObject(hitbox);
            Assert.That(fields.FindProperty("collider").objectReferenceValue, Is.EqualTo(collider));
            Assert.That(fields.FindProperty("otherColliders").arraySize, Is.Zero);
            Assert.That(fields.FindProperty("triggerOnce").boolValue, Is.True);
            Assert.That(fields.FindProperty("timeoutAfterExit").floatValue, Is.EqualTo(0.3f));
            Assert.That(fields.FindProperty("disableAfterTimeout").boolValue, Is.False);
            Assert.That(fields.FindProperty("timeout").floatValue, Is.EqualTo(0.25f));
            foreach (string axis in new[] { "x", "y", "z" })
            {
                AnimationClip clip = Clip(DanteUltimateAssetMigration.WaveClipPath);
                EditorCurveBinding binding = AnimationUtility.GetCurveBindings(clip).Single(candidate =>
                    candidate.path == "Hitbox" && candidate.propertyName == "m_LocalScale." + axis);
                Keyframe[] keys = AnimationUtility.GetEditorCurve(clip, binding).keys;
                Assert.That(keys, Has.Length.EqualTo(5));
                float[] times = { 0f, 0.16666667f, 1f, 1.75f, 2.5166667f };
                float[] values = { 0f, 0.02f, 0.35f, 0.77f, 1f };
                for (int index = 0; index < keys.Length; index++)
                {
                    Assert.That(keys[index].time, Is.EqualTo(times[index]).Within(0.0001f));
                    Assert.That(keys[index].value, Is.EqualTo(values[index]).Within(0.0001f));
                }
            }
        }

        [TestCase(DanteUltimateAssetMigration.MainAttackPath, 20, 10, 13)]
        [TestCase(DanteUltimateAssetMigration.WaveAttackPath, 431, 428, 429)]
        public void OriginalParticlesRenderersAndDependencyClosureRemainWithoutMissingScripts(string path, int transforms, int particles, int renderers)
        {
            GameObject root = Asset<GameObject>(path);
            Assert.That(root.GetComponentsInChildren<Transform>(true), Has.Length.EqualTo(transforms));
            Assert.That(root.GetComponentsInChildren<ParticleSystem>(true), Has.Length.EqualTo(particles));
            Assert.That(root.GetComponentsInChildren<Renderer>(true), Has.Length.EqualTo(renderers));
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject), Is.Zero, child.name);
                Assert.That(child.GetComponents<Component>().Any(component => component != null &&
                    component.GetType().Name == "NetworkIdentity"), Is.False, child.name);
            }
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                string rendererPath = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
                if (path == DanteUltimateAssetMigration.MainAttackPath && rendererPath == "Dante_Burning")
                {
                    Assert.That(renderer.enabled, Is.False);
                    Assert.That(renderer.GetComponent<ParticleSystem>().emission.enabled, Is.False);
                    Assert.That(renderer.sharedMaterials, Has.Length.EqualTo(1));
                    Assert.That(renderer.sharedMaterial, Is.Null);
                    continue;
                }
                for (int slot = 0; slot < renderer.sharedMaterials.Length; slot++)
                {
                    Material material = renderer.sharedMaterials[slot];
                    Assert.That(material, Is.Not.Null, rendererPath + " slot " + slot);
                    Assert.That(material.shader, Is.Not.Null, rendererPath + " slot " + slot);
                    Assert.That(material.shader.name, Is.Not.EqualTo("Hidden/InternalErrorShader"), rendererPath + " slot " + slot);
                }
            }
        }

        [Test]
        public void MissingShaderAdaptersKeepSourceTexturesColorAndActualBlendProperties()
        {
            Material multiply = MaterialGuid("ADD.mat");
            Assert.That(multiply.shader.name, Is.EqualTo("AllIn1SpriteShader/AllIn1SpriteShader"));
            Assert.That(TextureGuid(multiply), Is.EqualTo("3fa0a562edf26f6459817f268bd965de"));
            Assert.That(multiply.GetColor("_Color"), Is.EqualTo(new Color(1f, 0.038277514f, 0f, 1f)));
            Assert.That(multiply.IsKeywordEnabled("PREMULTIPLYALPHA_ON"), Is.True);
            Assert.That(multiply.GetFloat("_MySrcMode"), Is.EqualTo(2f));
            Assert.That(multiply.GetFloat("_MyDstMode"), Is.EqualTo(10f));
            Material glow = MaterialGuid("glow1_ADD_3.mat");
            Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(glow)), Is.EqualTo(DanteUltimateAssetMigration.AdaptedGlowMaterialGuid));
            Assert.That(AssetDatabase.GUIDToAssetPath(DanteUltimateAssetMigration.SourceGlowMaterialGuid),
                Is.Not.EqualTo(AssetDatabase.GetAssetPath(glow)), "Do not rewrite the pre-existing shared built-in material.");
            Assert.That(glow.shader.name, Is.EqualTo("AllIn1Vfx/AllIn1VfxURPCompat"));
            Assert.That(TextureGuid(glow), Is.EqualTo("3867ce58eaf7305489456ce9212b3068"));
            Assert.That(glow.GetFloat("_SrcMode"), Is.EqualTo(5f));
            Assert.That(glow.GetFloat("_DstMode"), Is.EqualTo(1f));
            Assert.That(glow.GetFloat("_ZWrite"), Is.Zero);
        }

        [Test]
        public void LightAndCameraEventReceiversRemainWhileUnknownExportPropertiesAreExplicitlyPreserved()
        {
            GameObject root = Main().gameObject;
            Assert.That(root.GetComponent("TimelineEffects"), Is.Not.Null);
            Assert.That(root.GetComponent("PausableParticleSystem"), Is.Not.Null);
            Assert.That(root.GetComponentsInChildren<Component>(true).Any(component => component != null &&
                component.GetType().Name == "PlayableDirector"), Is.False,
                "TimelineEffects here is an animation-event receiver, not the deferred UI Timeline flow.");
            foreach (string path in new[] { "Light 2D", "add light" })
                Assert.That(root.transform.Find(path).GetComponent("Light2D"), Is.Not.Null, path);
            string[] properties = AnimationUtility.GetCurveBindings(Clip(DanteUltimateAssetMigration.MainClipPath))
                .Select(binding => binding.propertyName).ToArray();
            foreach (string property in new[] { "material.path_0x8B19FAF2_uRjvinN", "script_0xDAB5983F_WRNotnJ", "script_0xADB2A8A9_qoIJtjL" })
            {
                Assert.That(properties, Does.Contain(property));
                Assert.That(DanteUltimateAssetMigration.RestorationLimits, Does.Contain(property));
            }
            Assert.DoesNotThrow(DanteUltimateAssetMigration.ValidateImportedAssets);
        }

        private static Material MaterialGuid(string name) => Asset<Material>(DanteUltimateAssetMigration.OutputFolder + "/Material/" + name);
        private static string TextureGuid(Material material) => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material.GetTexture("_MainTex")));
        private static DanteUltimateAttack Main() => Asset<GameObject>(DanteUltimateAssetMigration.MainAttackPath).GetComponent<DanteUltimateAttack>();
        private static UltimateDamageAttack Wave() => Asset<GameObject>(DanteUltimateAssetMigration.WaveAttackPath).GetComponent<UltimateDamageAttack>();
        private static AnimationClip Clip(string path) => Asset<AnimationClip>(path);
        private static T Asset<T>(string path) where T : Object
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(value, Is.Not.Null, "Run the focused Dante Ultimate asset importer first: " + path);
            return value;
        }
    }
}
