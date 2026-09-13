#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using FMODUnity;
using MonsterSupergroup.Gameplay.Combat;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class WispPresentationTests
    {
        private GameObject poolRoot, ownerRoot;
        private ProjectileAttackBehaviour emitter;
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static ProjectilePresentationStats Stats => new ProjectilePresentationStats {
            DamageMultiplierSum = 1, SpeedMultiplierSum = 1, SizeMultiplierSum = 1, DurationMultiplierSum = 1,
            EffectiveSpeed = .4f, Duration = 10, ProjectileCount = 1, BaseProjectileCount = 1 };

        [SetUp]
        public void SetUp()
        {
            poolRoot = new GameObject("Wisp test pool"); poolRoot.AddComponent<PoolManager>().Init();
            ownerRoot = new GameObject("Wisp test owner"); ownerRoot.SetActive(false);
            var owner = ownerRoot.AddComponent<PlayerMovement>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/GameObject/Dante_SlowProjectile_Behaviour.prefab");
            emitter = Object.Instantiate(prefab).GetComponent<ProjectileAttackBehaviour>();
            emitter.InitializePresentationReplica(2, owner);
        }

        [TearDown]
        public void TearDown()
        {
            if (emitter != null) { emitter.DisposePresentationReplica(); Object.DestroyImmediate(emitter.gameObject); }
            Object.DestroyImmediate(ownerRoot); Object.DestroyImmediate(poolRoot); PoolManager.Instance = null;
        }

        private ProjectileAttack Spawn(AttackElement element, float age = 0, bool shot = true)
        {
            return emitter.PlayPresentation(new ProjectilePresentationSpawn(2, new ProjectilePresentationKey(1, 0),
                Vector3.zero, Vector2.right, element, true, Stats), age, playLaunchSound: shot);
        }

        [UnityTest]
        public IEnumerator Wisp_AllVariantsRestoreAnimationAndMaterialStateOnReuse()
        {
            foreach (var element in new[] { AttackElement.Default, AttackElement.Fire, AttackElement.Poison })
            {
                var first = Spawn(element, .8f, false);
                Assert.That(first.transform.position.x, Is.EqualTo(.32f).Within(.001));
                Assert.That(first.transform.Find("Root/Scale/Fire").gameObject.activeSelf, Is.True);
                var renderer = first.transform.Find("Root/Scale/HeadLight (1)").GetComponent<Renderer>();
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/GameObject/PlayerAttack_Dante_Projectile" +
                    (element == AttackElement.Default ? "" : element == AttackElement.Fire ? "_Fire Variant" : "_Poison Variant") + ".prefab");
                Color authored = source.transform.Find("Root/Scale/HeadLight (1)").GetComponent<Renderer>().sharedMaterial.GetColor("_Color");
                renderer.sharedMaterial.SetColor("_Color", Color.black);
                first.TerminatePresentation(ProjectilePresentationPhase.Cancelled, Vector3.zero);
                var second = Spawn(element, 0, false);
                Assert.That(second, Is.SameAs(first));
                Assert.That(renderer.sharedMaterial.GetColor("_Color"), Is.EqualTo(authored));
                Assert.That(source.transform.Find("Root/Scale/HeadLight (1)").GetComponent<Renderer>().sharedMaterial.GetColor("_Color"), Is.EqualTo(authored));
                yield return new WaitForSeconds(.4f);
                Assert.That(second.gameObject.activeInHierarchy, Is.True, "Appearance clip must not end the projectile");
                second.TerminatePresentation(ProjectilePresentationPhase.Cancelled, Vector3.zero);
                Assert.That(second.GetComponentsInChildren<ParticleSystem>(true).All(p => p.particleCount == 0), Is.True);
            }
        }

        [UnityTest]
        public IEnumerator Wisp_RemoteImpactHasNoCollisionAndDoesNotReplayDuplicateTermination()
        {
            var projectile = Spawn(AttackElement.Default, .5f, false);
            projectile.TerminatePresentation(ProjectilePresentationPhase.Hit, new Vector3(2, 3));
            var effects = Object.FindObjectsByType<AttackHitParticleEffect>(FindObjectsSortMode.None);
            Assert.That(effects.Length, Is.EqualTo(1));
            Assert.That(effects[0].transform.position, Is.EqualTo(new Vector3(2, 3)));
            Assert.That(effects[0].GetComponentsInChildren<Collider2D>().All(c => !c.enabled), Is.True);
            projectile.TerminatePresentation(ProjectilePresentationPhase.Hit, Vector3.zero);
            Assert.That(Object.FindObjectsByType<AttackHitParticleEffect>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            yield return new WaitForSeconds(2f);
            Assert.That(Object.FindObjectsByType<AttackHitParticleEffect>(FindObjectsSortMode.None).Length, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Wisp_OwnerPiercingHitDealsDamageOnceAndPublishesContactWithoutEndingFlight()
        {
            var owner = ownerRoot.GetComponent<PlayerMovement>();
            owner.SetAimDirection(Vector2.right);
            var build = ownerRoot.AddComponent<PlayerBuildRuntime>();
            var targetRoot = new GameObject("Wisp piercing target");
            try
            {
                build.Initialize(owner, new FixedRandom());
                var weapon = build.EquipWeapon(AssetDatabase.LoadAssetAtPath<AstralShift.HellMaiden.Data.Cards.WeaponData>(
                    "Assets/MonoBehaviour/WeaponData_Dante_SlowProjectile.asset"), poolRoot.transform);
                ProjectilePresentationPhase? phase = null;
                build.ProjectilePresentationTerminated += edge => phase = edge.Phase;
                targetRoot.layer = LayerMask.NameToLayer("EnemyHitbox");
                targetRoot.transform.position = new Vector3(.5f, 0);
                targetRoot.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                targetRoot.AddComponent<CircleCollider2D>().radius = 1;
                var target = targetRoot.AddComponent<WispProcessTarget>(); target.Initialize(0x6ffff001);
                weapon.Attack();
                for (int i = 0; i < 10 && target.HitCount == 0; i++) yield return new WaitForFixedUpdate();
                Assert.That(target.HitCount, Is.EqualTo(1));
                Assert.That(target.TotalDamage, Is.EqualTo(15));
                Assert.That(phase, Is.EqualTo(ProjectilePresentationPhase.Impact));
                Assert.That(Object.FindObjectsByType<ProjectileAttack>(FindObjectsSortMode.None).Any(p => !p.PresentationOnly), Is.True);
                Assert.That(Object.FindObjectsByType<AttackHitParticleEffect>(FindObjectsSortMode.None)
                    .SelectMany(p => p.GetComponentsInChildren<Collider2D>()).All(c => !c.enabled), Is.True);
            }
            finally { build.ClearBuild(); Object.DestroyImmediate(targetRoot); }
        }

        private sealed class FixedRandom : MonsterSupergroup.GAS.IRandomSource { public float Next01() => .99f; }

        [UnityTest]
        public IEnumerator Wisp_PiercingContactPreservesFlightAndLoopAndUsesReportedImpactPosition()
        {
            var projectile = Spawn(AttackElement.Default, .5f, false);
            Vector3 before = projectile.transform.position;
            projectile.TerminatePresentation(ProjectilePresentationPhase.Impact, new Vector3(2, 3));
            Assert.That(projectile.gameObject.activeInHierarchy, Is.True);
            Assert.That(projectile.transform.position, Is.EqualTo(before), "A contact must not teleport the projectile");
            Assert.That(((FMOD.Studio.EventInstance)typeof(ProjectileAttack).GetField("_loopInstance", Private).GetValue(projectile)).isValid(), Is.True);
            var impact = Object.FindFirstObjectByType<AttackHitParticleEffect>();
            Assert.That(impact, Is.Not.Null);
            Assert.That(impact.transform.position, Is.EqualTo(new Vector3(2, 3)));
            Assert.That(impact.GetComponentsInChildren<Collider2D>().All(c => !c.enabled), Is.True);
            yield return null;
            projectile.TerminatePresentation(ProjectilePresentationPhase.Cancelled, projectile.transform.position);
        }

        [UnityTest]
        public IEnumerator Wisp_AudioLoopIsReleasedAndHistoricalShotIsSuppressed()
        {
            yield return null;
            var projectile = Spawn(AttackElement.Default, 3f, false);
            Assert.That((bool)typeof(ProjectileAttack).GetField("_suppressLaunchSound", Private).GetValue(projectile), Is.True);
            RuntimeManager.StudioSystem.flushCommands();
            var loop = (FMOD.Studio.EventInstance)typeof(ProjectileAttack).GetField("_loopInstance", Private).GetValue(projectile);
            Assert.That(loop.isValid(), Is.True, "Imported loop event must be available");
            projectile.TerminatePresentation(ProjectilePresentationPhase.Expired, projectile.transform.position);
            Assert.That(((FMOD.Studio.EventInstance)typeof(ProjectileAttack).GetField("_loopInstance", Private).GetValue(projectile)).isValid(), Is.False);
            var reused = Spawn(AttackElement.Default);
            Assert.That((bool)typeof(ProjectileAttack).GetField("_suppressLaunchSound", Private).GetValue(reused), Is.False);
            reused.Dispose();
            Assert.That(((FMOD.Studio.EventInstance)typeof(ProjectileAttack).GetField("_loopInstance", Private).GetValue(reused)).isValid(), Is.False);
        }

        [Test]
        public void Wisp_MissingOptionalAudioDoesNotThrowOrCreateAnInstance()
        {
            var projectile = Spawn(AttackElement.Default, 1f, false);
            projectile.Dispose();
            var reference = new EventReference { Guid = new FMOD.GUID { Data1 = 1234567, Data2 = 7654321, Data3 = 987654, Data4 = 456789 } };
            typeof(ProjectileAttack).GetField("projectileLoopSound", Private).SetValue(projectile, new AnimatedAttack.AnimatedAttackSound(reference, true));
            Assert.DoesNotThrow(projectile.PlayLaunchedLoopSound);
            Assert.That(((FMOD.Studio.EventInstance)typeof(ProjectileAttack).GetField("_loopInstance", Private).GetValue(projectile)).isValid(), Is.False);
        }
    }
}
#endif
