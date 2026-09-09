#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class DanteUltimateNativeAttackTests
    {
        private const string DefinitionPath = "Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Ultimate/MonoBehaviour/UltimateData_Dante.asset";
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private readonly List<RuntimeEquipmentModifiers> containers = new List<RuntimeEquipmentModifiers>();
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        [TearDown]
        public void TearDown()
        {
            for (int index = objects.Count - 1; index >= 0; index--)
                if (objects[index] != null) UnityEngine.Object.DestroyImmediate(objects[index]);
            foreach (RuntimeEquipmentModifiers container in containers) container.Clear();
            objects.Clear(); containers.Clear();
        }

        [Test]
        public void OriginalControllerReportsWaveTimesWithoutNativeFallback()
        {
            DanteUltimateAttack root = Instance();
            var probe = root.gameObject.AddComponent<UltimateSourceAnimationEventProbe>();
            root.gameObject.SetActive(true);
            root.animator.enabled = false;
            root.animator.Rebind();
            root.animator.Update(0f);
            root.animator.SetTrigger("Attack");
            root.animator.Update(0f);
            for (int frame = 1; frame <= 300; frame++)
            {
                probe.SampleTime = frame / 60f;
                root.animator.Update(1f / 60f);
            }
            Assert.That(root.ActiveUseId, Is.Zero, "The Native scheduler is inactive throughout this controller-only diagnostic.");
            Assert.That(probe.Ordinals, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(probe.Times[0], Is.InRange(0f, 0.28334f));
            Assert.That(probe.Times[1] - probe.Times[0], Is.EqualTo(2.1166666f).Within(0.035f));
            Debug.Log($"ULTIMATE_SOURCE_CONTROLLER_TIMING first={probe.Times[0]:F6} second={probe.Times[1]:F6} " +
                $"entryBlend={root.EntryTransitionDuration:F6} lastWaveEnd={probe.Times[1] + 2.5f:F6}");
        }

        [Test]
        public void OneAdmittedRootOwnsBothRealWaveLeasesAndKeepsItsFrozenBuildStats()
        {
            DanteUltimateAttack root = Native(out PlayerBuildRuntime build, out var sink);
            int cameraCalls = 0;
            root.ConfigureNativeUltimate(root.OwnerPlayer, root.NativeRuntime, Definition(), _ => cameraCalls++);
            build.PerkMultipliers.damage = 0.5f;
            var starts = new List<byte>(); var ends = new List<byte>(); var completed = new List<ulong>();
            root.WaveStarted += starts.Add; root.WaveEnded += ends.Add; root.NativeAttackCompleted += completed.Add;
            int spawns = 0; int terminations = 0;
            root.PresentationSpawned += _ => spawns++;
            root.PresentationTerminated += _ => terminations++;
            WeaponData view = root.WeaponData;
            Assert.That(root.TryBegin(CombatEventId.Compose(1, 1, 1).Value), Is.True);
            AttackSnapshot snapshot = Snapshot(root);
            Assert.That(snapshot.Stats.Damage, Is.EqualTo(150));
            Assert.That(sink.Events.Count(value => value.Kind == CombatEventKind.AttackStarted), Is.EqualTo(1));
            Assert.That(snapshot.Context.EventId.Value, Is.EqualTo(root.ActiveUseId));
            Assert.That(snapshot.Context.AbilityId, Is.EqualTo(0x80000000u));
            Assert.That(root.TryBegin(root.ActiveUseId), Is.False);
            Advance(root, 0.5f);
            UltimateDamageAttack first = ActiveWaves(root).Single();
            Assert.That(WaveSnapshot(first), Is.SameAs(snapshot));
            var target = Create("Ultimate GAS target").AddComponent<MeleeTestTarget>();
            Collider2D collider = target.gameObject.AddComponent<BoxCollider2D>();
            Hit(first, collider); Hit(first, collider);
            Assert.That(target.Health, Is.EqualTo(850));
            build.PerkMultipliers.damage = 1f;
            Advance(root, 1.65f);
            Assert.That(ActiveWaves(root), Has.Length.EqualTo(2));
            UltimateDamageAttack second = ActiveWaves(root).Single(wave => wave != first);
            Assert.That(WaveSnapshot(second), Is.SameAs(snapshot));
            Hit(second, collider);
            Assert.That(target.Health, Is.EqualTo(700), "A Build change cannot rewrite the active root's immutable attack.");
            Advance(root, 1.9f);
            Assert.That(root.ElapsedSeconds, Is.GreaterThan(4.0333333f));
            Assert.That(root.IsNativeActive, Is.True, "The second wave outlives the main clip and invulnerability delay.");
            Assert.That(snapshot.IsDisposed, Is.False);
            Advance(root, 0.7f);
            Assert.That(root.ActiveUseId, Is.Zero);
            Assert.That(snapshot.IsDisposed, Is.True);
            Assert.That(starts, Is.EqualTo(new byte[] { 0, 1 }));
            Assert.That(ends, Is.EqualTo(new byte[] { 0, 1 }));
            Assert.That(completed, Has.Count.EqualTo(1));
            Assert.That(cameraCalls, Is.EqualTo(2), "The first Awake must preserve the explicitly bound Owner camera callback.");
            Assert.That(spawns, Is.EqualTo(1)); Assert.That(terminations, Is.EqualTo(1));
            Assert.That(build.WeaponCount, Is.Zero, "The Ultimate never occupies a hand slot.");
            Assert.That(root.TryBegin(CombatEventId.Compose(1, 1, 100).Value), Is.True);
            Assert.That(Snapshot(root).Stats.Damage, Is.EqualTo(200));
            Assert.That(root.WeaponData, Is.SameAs(view), "One compatibility view is reused for all casts in this binding.");
            root.Cancel(root.ActiveUseId);
        }

        [Test]
        public void CancelAndExternalDisableReleaseEveryLeaseAndCannotEndALaterUse()
        {
            DanteUltimateAttack root = Native(out _, out _);
            int completed = 0; root.NativeAttackCompleted += _ => completed++;
            Assert.That(root.TryBegin(CombatEventId.Compose(1, 1, 1).Value), Is.True);
            Advance(root, 2.2f);
            AttackSnapshot first = Snapshot(root);
            ulong old = root.ActiveUseId;
            Assert.That(root.Cancel(old), Is.True);
            Assert.That(first.IsDisposed, Is.True);
            Assert.That(ActiveWaves(root), Is.Empty);
            Assert.That(root.TryBegin(CombatEventId.Compose(1, 1, 20).Value), Is.True);
            AttackSnapshot next = Snapshot(root);
            Assert.That(root.Cancel(old), Is.False);
            root.gameObject.SetActive(false);
            Assert.That(next.IsDisposed, Is.True);
            Assert.That(root.ActiveUseId, Is.Zero);
            Assert.That(completed, Is.EqualTo(2));
            foreach (UltimateDamageAttack wave in root.GetComponentsInChildren<UltimateDamageAttack>(true))
            {
                Assert.That(WaveSnapshot(wave), Is.Null);
                Assert.That(wave.hitbox.collider.enabled, Is.False);
            }
        }

        [Test]
        public void AgedRemotePlaybackSeeksOriginalWavesWithoutCreatingGasOrCallingAnOwnerCamera()
        {
            DanteUltimateAttack root = Instance();
            var owner = Create("Ultimate remote avatar", false).AddComponent<PlayerMovement>();
            root.InitializePresentationReplica(owner);
            int returned = 0; int roots = 0; root.NativeAttackCompleted += _ => roots++;
            var stats = new ProjectilePresentationStats { Duration = 1f, EffectiveSpeed = 1f, ProjectileCount = 1, BaseProjectileCount = 1 };
            Assert.That(root.PlayPresentation(new UltimatePresentationSpawn(0, 501, stats), 3f, _ => returned++), Is.True);
            Assert.That(root.NativeRuntime, Is.Null);
            Assert.That(root.WeaponData, Is.Null);
            Assert.That(root.IsPresentationActive, Is.True);
            UltimateDamageAttack wave = ActiveWaves(root).Single();
            Assert.That(wave.WaveElapsed, Is.EqualTo(3f - 2.1166666f).Within(0.02f));
            Assert.That(WaveSnapshot(wave), Is.Null);
            Assert.That(wave.hitbox.collider.enabled, Is.False);
            var target = Create("Ultimate remote no-damage target").AddComponent<MeleeTestTarget>();
            Hit(wave, target.gameObject.AddComponent<BoxCollider2D>());
            Assert.That(target.Health, Is.EqualTo(1000));
            Advance(root, 1.8f);
            Assert.That(root.ActiveUseId, Is.Zero);
            Assert.That(returned, Is.EqualTo(1));
            Assert.That(roots, Is.Zero);
            Assert.That(root.PlayPresentation(new UltimatePresentationSpawn(0, 501, stats), 0f), Is.False);
        }

        [Test]
        public void SourceTimeAndKnockbackGettersAreReadOnlyAndIdsDoNotConsumeWeaponZero()
        {
            var prefab = (DanteUltimateAttack)Definition().ultimateAttackWeaponBehaviour;
            bool animatorEnabled = prefab.animator.enabled;
            Assert.That(prefab.SequenceDuration, Is.EqualTo(4.6166666f).Within(0.0001f));
            Assert.That(prefab.animator.enabled, Is.EqualTo(animatorEnabled));
            Assert.That(prefab.EntryTransitionDuration, Is.EqualTo(0.25f));
            Assert.That(prefab.InvulnerabilityDuration, Is.EqualTo(3f));
            Assert.That(prefab.KnockbackRadius, Is.EqualTo(5f));
            Assert.That(prefab.InitialKnockbackSettings.distance, Is.EqualTo(2f));
            Assert.That(UltimateNativeDefinitionAdapter.EncodeAbilityId(0), Is.EqualTo(0x80000000u));
            Assert.Throws<ArgumentOutOfRangeException>(() => UltimateNativeDefinitionAdapter.EncodeAbilityId(0x80000000u));
        }

        private DanteUltimateAttack Native(out PlayerBuildRuntime build, out EventSink sink)
        {
            var owner = Create("Ultimate Native owner", false).AddComponent<PlayerMovement>();
            owner.AttacksParent = Create("Ultimate attacks").transform;
            build = owner.gameObject.AddComponent<PlayerBuildRuntime>(); build.Initialize(owner, new FixedRandom());
            DanteUltimateAttack root = Instance(owner.AttacksParent);
            WeaponRuntimeBehaviour runtime = root.gameObject.AddComponent<WeaponRuntimeBehaviour>();
            runtime.InitializeOnAwake = false;
            RuntimeEquipmentModifiers modifiers = UltimateNativeDefinitionAdapter.CreateIntrinsicModifiers(Definition());
            containers.Add(modifiers);
            sink = new EventSink(); runtime.ConfigureCombatIdentity(1, 1);
            runtime.InitializeExternal(UltimateNativeDefinitionAdapter.ToNativeBaseStats(Definition()),
                UltimateNativeDefinitionAdapter.EncodeAbilityId(Definition().Id), modifiers, build.PerkMultipliers,
                new FixedRandom(), new SequentialCombatEventIdSource(1, 1, 1000), sink);
            root.ConfigureNativeUltimate(owner, runtime, Definition());
            Assert.That(runtime.ModifierCount, Is.EqualTo(1));
            return root;
        }

        private DanteUltimateAttack Instance(Transform parent = null)
        {
            var staging = Create("Ultimate inactive staging", false);
            var root = UnityEngine.Object.Instantiate((DanteUltimateAttack)Definition().ultimateAttackWeaponBehaviour, staging.transform);
            root.gameObject.SetActive(false);
            root.transform.SetParent(parent, false);
            objects.Add(root.gameObject);
            return root;
        }
        private static UltimateData Definition()
        {
            UltimateData source = AssetDatabase.LoadAssetAtPath<UltimateData>(DefinitionPath);
            Assert.That(source, Is.Not.Null, "Run DanteUltimateAssetMigration.Import first.");
            return source;
        }
        private static void Advance(DanteUltimateAttack root, float seconds) =>
            typeof(DanteUltimateAttack).GetMethod("Advance", Private).Invoke(root, new object[] { seconds, false });
        private static AttackSnapshot Snapshot(DanteUltimateAttack root) =>
            (AttackSnapshot)typeof(DanteUltimateAttack).GetField("activeSnapshot", Private).GetValue(root);
        private static AttackSnapshot WaveSnapshot(UltimateDamageAttack wave) =>
            (AttackSnapshot)typeof(BasePlayerAttack).GetProperty("NativeAttackSnapshot", Private).GetValue(wave);
        private static UltimateDamageAttack[] ActiveWaves(DanteUltimateAttack root) =>
            root.GetComponentsInChildren<UltimateDamageAttack>(true).Where(wave => wave.IsWavePlaying).ToArray();
        private static void Hit(UltimateDamageAttack wave, Collider2D target) =>
            typeof(PlayerAttackHitBox).GetMethod("OnTriggerEnter2D", Private).Invoke(wave.hitbox, new object[] { target });
        private GameObject Create(string name, bool active = true)
        { var value = new GameObject(name); value.SetActive(active); objects.Add(value); return value; }
        private sealed class FixedRandom : IRandomSource { public float Next01() => 0.99f; }
        private sealed class EventSink : ICombatEventSink
        { public readonly List<CombatEvent> Events = new List<CombatEvent>(); public void Publish(CombatEvent combatEvent) => Events.Add(combatEvent); }
    }

    public sealed class UltimateSourceAnimationEventProbe : MonoBehaviour
    {
        public float SampleTime;
        public readonly List<int> Ordinals = new List<int>();
        public readonly List<float> Times = new List<float>();
        public void SpawnWave(int ordinal) { Ordinals.Add(ordinal); Times.Add(SampleTime); }
    }
}
#endif
