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
    public sealed class DanteDashNativeGasMigrationTests
    {
        [Test]
        public void SourceFireTrailIdentityAndStatsBecomeNativeDataWithoutInventedStatus()
        {
            WeaponData weapon = Weapon();
            Assert.That(weapon.ID, Is.EqualTo(8));
            Assert.That(weapon.BaseStats.damage, Is.EqualTo(9));
            Assert.That(weapon.BaseStats.critRate, Is.EqualTo(0.04f));
            Assert.That(weapon.BaseStats.critMultiplier, Is.EqualTo(1.3f));
            Assert.That(weapon.BaseStats.speed, Is.EqualTo(1f));
            Assert.That(weapon.BaseStats.size, Is.EqualTo(1f));
            Assert.That(weapon.BaseStats.duration, Is.EqualTo(1.25f));
            Assert.That(weapon.BaseStats.projectileCount, Is.EqualTo(1));
            Assert.That(weapon.BaseStats.knockbackDistance, Is.Zero);
            Assert.That(weapon.Presentation.Knockback.distance, Is.Zero);
            Assert.That(weapon.AttackTags, Is.EqualTo(CombatTags.Attack));
            Assert.That((int)weapon.modifierFlags, Is.EqualTo(109));
            Assert.That(weapon.UltimateData, Is.Null);
            Assert.DoesNotThrow(weapon.ValidateNativeGas);
        }

        [Test]
        public void EmitterRetainsDefaultAndPoisonVariantsWithoutAddingFireVariant()
        {
            Assert.That(Weapon().WeaponPrefab, Is.TypeOf<DashAttackBehaviour>());
            var emitter = new SerializedObject(Weapon().WeaponPrefab);
            Assert.That(emitter.FindProperty("variants.defaultPrefab").objectReferenceValue, Is.EqualTo(Attack(false)));
            Assert.That(emitter.FindProperty("variants.poisonPrefab").objectReferenceValue, Is.EqualTo(Attack(true)));
            Assert.That(emitter.FindProperty("variants.firePrefab").objectReferenceValue, Is.Null);
            Assert.That(emitter.FindProperty("variants.allowFire").boolValue, Is.False);
            Assert.That(emitter.FindProperty("variants.allowPoison").boolValue, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SerializedAttackReferencesSpacingAndEdgeDefaultsRemainIntact(bool poison)
        {
            MultiParticlePlayerTrailAttack attack = Attack(poison);
            Assert.That(attack.GetComponent<AnimatedAttack>(), Is.Null);
            Assert.That(attack.GetComponentsInChildren<Animator>(true), Is.Empty);
            Assert.That(attack.progressionScaler, Is.Null, "The source has no progression scaler; do not add the beam Speed scaler.");
            Assert.That(attack.hitbox, Is.TypeOf<PlayerAttackOvertimeHitBox>());
            var serialized = new SerializedObject(attack);
            Assert.That(serialized.FindProperty("trailParticles").objectReferenceValue, Is.EqualTo(Particles(poison)));
            Assert.That(serialized.FindProperty("trailStepParticles").objectReferenceValue,
                Is.EqualTo(attack.transform.Find("Fire_Trail_Small").GetComponent<ParticleSystem>()));
            Assert.That(serialized.FindProperty("trailDelta").floatValue, Is.EqualTo(1f));
            EdgeCollider2D edge = attack.GetComponent<EdgeCollider2D>();
            Assert.That(serialized.FindProperty("edgeCollider").objectReferenceValue, Is.EqualTo(edge));
            Assert.That(edge.edgeRadius, Is.EqualTo(0.5f));
            Assert.That(edge.isTrigger, Is.False, "Keep the source setting; actual trigger interaction is a native runtime test.");
            Assert.That(edge.points, Is.EqualTo(new[] { new Vector2(-0.5f, 0f), new Vector2(0.5f, 0f) }));
            var hitbox = new SerializedObject(attack.hitbox);
            Assert.That(hitbox.FindProperty("collider").objectReferenceValue, Is.EqualTo(edge));
            Assert.That(hitbox.FindProperty("hitInterval").floatValue, Is.EqualTo(0.5f),
                "This is the source serialized default; legacy Init overwrote it with Weapon.Speed, which is 1 for ID 8.");
            Assert.That(hitbox.FindProperty("timeoutAfterExit").floatValue, Is.EqualTo(0.3f));
            Assert.That(hitbox.FindProperty("disableAfterTimeout").boolValue, Is.False);
            Assert.That(Quaternion.Angle(attack.transform.localRotation, Quaternion.identity), Is.LessThan(0.001f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OriginalSegmentParticlesKeepLoopingEmissionAndFiniteVisualTail(bool poison)
        {
            ParticleSystem segment = Particles(poison);
            Assert.That(segment.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(5));
            Assert.That(segment.GetComponentsInChildren<ParticleSystem>(true).Length, Is.EqualTo(5));
            Assert.That(segment.emission.enabled, Is.False, "Root is an authored particle container.");
            foreach (string name in new[] { "Fire", "Fire (1)", "Embers" })
            {
                ParticleSystem particle = segment.transform.Find("Fire_Trail1/" + name).GetComponent<ParticleSystem>();
                Assert.That(particle.main.loop, Is.True, name);
                Assert.That(particle.main.duration, Is.EqualTo(1f), name);
                Assert.That(particle.main.startLifetime.mode, Is.EqualTo(ParticleSystemCurveMode.Constant), name);
                Assert.That(particle.main.startLifetime.constant, Is.EqualTo(0.6f), name);
                Assert.That(particle.emission.enabled, Is.True, name);
                Assert.That(particle.emission.rateOverTime.constant, Is.EqualTo(name == "Embers" ? 5f : 15f), name);
                Assert.That(particle.textureSheetAnimation.enabled, Is.True, name);
            }
            AssertRenderable(segment.gameObject, string.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AttachedParticlesHierarchyAndSourceAudioEventArePreserved(bool poison)
        {
            MultiParticlePlayerTrailAttack attack = Attack(poison);
            Assert.That(attack.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(6));
            Assert.That(attack.GetComponentsInChildren<ParticleSystem>(true).Length, Is.EqualTo(5));
            Transform ground = attack.transform.Find("Fire_Trail_Small/Ground_02  (1)");
            Assert.That(ground, Is.Not.Null);
            foreach (string name in new[] { "GroundFire_01", "GroundFire_02", "FireSparks" })
            {
                ParticleSystem particle = ground.Find(name).GetComponent<ParticleSystem>();
                Assert.That(particle.main.loop, Is.False, name);
                Assert.That(particle.main.duration, Is.EqualTo(100f), name);
                Assert.That(particle.main.startLifetime.mode, Is.EqualTo(ParticleSystemCurveMode.TwoConstants), name);
            }
            var serialized = new SerializedObject(attack);
            Assert.That(serialized.FindProperty("soundEvent.Guid.Data1").intValue, Is.EqualTo(232515404));
            Assert.That(serialized.FindProperty("soundEvent.Guid.Data2").intValue, Is.EqualTo(1258032158));
            Assert.That(serialized.FindProperty("soundEvent.Guid.Data3").intValue, Is.EqualTo(1951765410));
            Assert.That(serialized.FindProperty("soundEvent.Guid.Data4").intValue, Is.EqualTo(-522659016));
            AssertRenderable(attack.gameObject, "Fire_Trail_Small");
            Assert.DoesNotThrow(DanteDashNativeGasMigration.ValidateImportedAssets);
        }

        [Test]
        public void DatabaseAddsDashOnceAndKeepsEarlierPhasesAndDefaultWeapon()
        {
            WeaponDB database = AssetDatabase.LoadAssetAtPath<WeaponDB>(DanteNativeGasMigration.NativeWeaponDatabasePath);
            Assert.That(database, Is.Not.Null);
            Assert.That(database.Weapons.Take(4).Select(entry => entry.ID), Is.EqualTo(new uint[] { 2, 1, 3, 6 }));
            Assert.That(database.Weapons.Count(entry => entry.ID == 8), Is.EqualTo(1));
            Assert.That(database.Weapons.Single(entry => entry.ID == 8), Is.EqualTo(Weapon()));
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(DanteNativeGasMigration.NetworkPlayerPrefabPath);
            Assert.That(player, Is.Not.Null);
            Component build = player.GetComponent("PlayerBuildRuntime");
            Assert.That(new SerializedObject(build).FindProperty("initialWeaponId").intValue, Is.EqualTo(2));
        }

        [Test]
        public void PlayerDashCurveRetainsAllAuthoredKeyframeDataAndHasPositiveArea()
        {
            AnimationCurve curve = PlayerMovement().FindProperty("dashCurve").animationCurveValue;
            Assert.That(curve, Is.Not.Null);
            Assert.That(curve.keys, Has.Length.EqualTo(2));
            Keyframe first = curve.keys[0];
            Keyframe second = curve.keys[1];
            Assert.That(first.time, Is.Zero);
            Assert.That(first.value, Is.EqualTo(0.3f));
            Assert.That(first.inTangent, Is.EqualTo(2f));
            Assert.That(first.outTangent, Is.EqualTo(2f));
            Assert.That(first.inWeight, Is.Zero);
            Assert.That(first.outWeight, Is.Zero);
            Assert.That(first.weightedMode, Is.EqualTo(WeightedMode.None));
            Assert.That(second.time, Is.EqualTo(0.9929199f));
            Assert.That(second.value, Is.EqualTo(0.75f));
            Assert.That(second.inTangent, Is.EqualTo(-1.4645969f));
            Assert.That(second.outTangent, Is.EqualTo(-1.4645969f));
            Assert.That(second.inWeight, Is.EqualTo(0.04819073f));
            Assert.That(second.outWeight, Is.Zero);
            Assert.That(second.weightedMode, Is.EqualTo(WeightedMode.None));
            for (int i = 0; i < curve.length; i++)
            {
                Assert.That(AnimationUtility.GetKeyLeftTangentMode(curve, i), Is.EqualTo(AnimationUtility.TangentMode.Free));
                Assert.That(AnimationUtility.GetKeyRightTangentMode(curve, i), Is.EqualTo(AnimationUtility.TangentMode.Free));
            }
            double area = 0;
            for (int i = 0; i < 1000; i++)
                area += (Mathf.Clamp01(curve.Evaluate(i / 1000f)) + Mathf.Clamp01(curve.Evaluate((i + 1) / 1000f))) * 0.0005;
            Assert.That(double.IsFinite(area) && area > 0, Is.True,
                "An empty curve had zero area and produced an infinite Dash time for a nonzero distance.");
        }

        [Test]
        public void PlayerDashCollisionConfigurationUsesTheAuditedLayerIndices()
        {
            var movement = PlayerMovement();
            Assert.That(movement.FindProperty("dashExclusionLayerMask").intValue, Is.EqualTo(4087));
            Assert.That(movement.FindProperty("obstacleLayerMask").intValue, Is.EqualTo(2048));
            Assert.That(movement.FindProperty("edgeLayerMask").intValue, Is.EqualTo(4198400));
            Assert.That(movement.FindProperty("dashObstacleMargin").floatValue, Is.EqualTo(0.3f));
            Assert.That(movement.FindProperty("dashBufferTime").floatValue, Is.EqualTo(0.1f));
            Assert.That(LayerMask.LayerToName(11), Is.EqualTo("Obstacles"));
            Assert.That(LayerMask.LayerToName(12), Is.EqualTo("Edges"));
            Assert.That(LayerMask.LayerToName(22), Is.EqualTo("TrapEdges"));
            Assert.That(LayerMask.GetMask("Obstacles"), Is.EqualTo(2048));
            Assert.That(LayerMask.GetMask("Edges", "TrapEdges"), Is.EqualTo(4198400));
            string[] excluded = { "Default", "TransparentFX", "Ignore Raycast", "Water", "UI", "EnemyCollision",
                "EnemyHitbox", "EnemyAttack", "PlayerAttack", "Player", "Obstacles" };
            Assert.That(LayerMask.GetMask(excluded), Is.EqualTo(4087));
        }

        private static SerializedObject PlayerMovement()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(DanteNativeGasMigration.NetworkPlayerPrefabPath);
            Assert.That(player, Is.Not.Null);
            Component movement = player.GetComponent("PlayerMovement");
            Assert.That(movement, Is.Not.Null);
            return new SerializedObject(movement);
        }

        private static void AssertRenderable(GameObject root, string sourceContainerPath)
        {
            Transform container = sourceContainerPath.Length == 0 ? root.transform : root.transform.Find(sourceContainerPath);
            Assert.That(container, Is.Not.Null, sourceContainerPath);
            ParticleSystem particles = container.GetComponent<ParticleSystem>();
            ParticleSystemRenderer containerRenderer = container.GetComponent<ParticleSystemRenderer>();
            Assert.That(particles, Is.Not.Null);
            Assert.That(particles.emission.enabled, Is.False);
            Assert.That(containerRenderer, Is.Not.Null);
            Assert.That(containerRenderer.enabled, Is.False);
            Assert.That(containerRenderer.sharedMaterials, Has.Length.EqualTo(1));
            Assert.That(containerRenderer.sharedMaterials[0], Is.Null,
                "Only this explicitly audited source particle container has an intentional null material.");
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject), Is.Zero, child.name);
                Assert.That(child.GetComponents<Component>().Any(component => component != null &&
                    component.GetType().Name == "NetworkIdentity"), Is.False, child.name);
            }
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true).Where(value => value != containerRenderer))
            {
                string path = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material material = materials[slot];
                    Assert.That(material, Is.Not.Null, path + " material slot " + slot);
                    Assert.That(material.shader, Is.Not.Null, path + " material slot " + slot);
                    Assert.That(material.shader.name, Does.StartWith("AllIn1"), path + " material slot " + slot);
                }
            }
        }
        private static WeaponData Weapon()
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(DanteDashNativeGasMigration.WeaponPath);
            Assert.That(weapon, Is.Not.Null, "Run the focused Dante Dash importer before these asset tests.");
            return weapon;
        }
        private static MultiParticlePlayerTrailAttack Attack(bool poison) => AssetDatabase.LoadAssetAtPath<GameObject>(
            poison ? DanteDashNativeGasMigration.PoisonAttackPath : DanteDashNativeGasMigration.FireAttackPath)
            .GetComponent<MultiParticlePlayerTrailAttack>();
        private static ParticleSystem Particles(bool poison) => AssetDatabase.LoadAssetAtPath<GameObject>(
            poison ? DanteDashNativeGasMigration.PoisonParticlesPath : DanteDashNativeGasMigration.FireParticlesPath)
            .GetComponent<ParticleSystem>();
    }
}
