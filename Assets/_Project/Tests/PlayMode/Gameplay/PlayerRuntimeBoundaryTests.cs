using System;
using System.Reflection;
using AstralShift.HellMaiden.Player;
using AstralShift.QTI.Interactors;
using AstralShift.QTI.Triggers.Physics2D;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class PlayerRuntimeBoundaryTests
    {
        private GameObject first, second;

        [SetUp]
        public void SetUp()
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Content/NetworkCombat/NetworkPlayer.prefab");
            first = UnityEngine.Object.Instantiate(prefab);
            second = UnityEngine.Object.Instantiate(prefab);
#else
            Assert.Ignore("Editor fixture loads the production prefab. Standalone coverage uses Boot process tests.");
#endif
        }

        [TearDown]
        public void TearDown()
        {
            if (first != null) UnityEngine.Object.DestroyImmediate(first);
            if (second != null) UnityEngine.Object.DestroyImmediate(second);
        }

        [Test]
        public void AwakeDoesNotCreateBuildOrBindOwner_ExplicitInitializationDoesNotResetOtherPlayers()
        {
            var a = first.GetComponent<PlayerMovement>();
            var b = second.GetComponent<PlayerMovement>();
            Assert.That(a.IsRuntimeInitialized, Is.False);
            Assert.That(a.IsLocalOwnerBound, Is.False);
            Assert.That(first.GetComponent<PlayerBuildRuntime>().IsBuildActive, Is.False);
            first.GetComponent<NetworkPlayerBootstrap>().EnsurePlayerRuntimeInitialized();
            second.GetComponent<NetworkPlayerBootstrap>().EnsurePlayerRuntimeInitialized();
            var health = first.GetComponent<CombatantBehaviour>();
            health.ApplyCanonicalHealth(37, 140, 5);
            a.EnsureRuntimeInitialized();
            b.EnsureRuntimeInitialized();
            Assert.That(health.CurrentHealth, Is.EqualTo(37));
            Assert.That(a.PlayerStats.MaxHP, Is.EqualTo(140));
            Assert.That(second.GetComponent<CombatantBehaviour>().CurrentHealth,
                Is.EqualTo(b.PlayerStats.playerBaseStatsDatabase.values.maxHP));
            Assert.That(b.PlayerStats.MaxHP, Is.EqualTo(b.PlayerStats.playerBaseStatsDatabase.values.maxHP));
            Assert.That(a.PlayerStats, Is.Not.SameAs(b.PlayerStats));
        }

        [Test]
        public void OptionalInputAndCameraBindingCanRepeatWithoutCreatingOrResettingGameplay()
        {
            var player = first.GetComponent<PlayerMovement>();
            player.EnsureRuntimeInitialized();
            var health = first.GetComponent<CombatantBehaviour>();
            health.ApplyCanonicalHealth(38, 100, 6);
            var binding = new LocalPlayerInputBinding();
            for (int i = 0; i < 3; i++)
            {
                Assert.DoesNotThrow(() => binding.Bind(player));
                binding.Refresh();
                Assert.That(player.IsLocalOwnerBound, Is.True);
                binding.Dispose();
                binding.Dispose();
                Assert.That(player.IsLocalOwnerBound, Is.False);
            }
            Assert.That(health.CurrentHealth, Is.EqualTo(38));
            Assert.That(first.GetComponent<PlayerBuildRuntime>().IsBuildActive, Is.False);
        }

        [Test]
        public void HealthBindingReconfigurationDoesNotMultiplySubscriptions()
        {
            var player = first.GetComponent<PlayerMovement>();
            player.EnsureRuntimeInitialized();
            var health = first.GetComponent<CombatantBehaviour>();
            var eventField = typeof(CombatantBehaviour).GetField("HealthChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            int baseline = ((Delegate)eventField.GetValue(health)).GetInvocationList().Length;
            for (int i = 0; i < 5; i++) player.CombatantBinding.Configure(player, health);
            Assert.That(((Delegate)eventField.GetValue(health)).GetInvocationList().Length, Is.EqualTo(baseline));
        }

        [Test]
        public void InteractionKeepsConfiguredFinderAndSelectionLockButIgnoresDestroyedFinder()
        {
            var player = first.GetComponent<PlayerMovement>();
            player.EnsureRuntimeInitialized();
            var targetObject = new GameObject("Interaction target");
            targetObject.transform.SetParent(first.transform);
            var target = targetObject.AddComponent<CountingInteractionTrigger>();
            var finder = first.AddComponent<Interaction2DFinder>();
            typeof(Interaction2DFinder).GetField("_nearestInteraction", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(finder, target);
            player.interactionFinder = finder;
            player.Interact();
            Assert.That(target.Calls, Is.EqualTo(1));
            player.SetUpgradeSelectionLocked(true);
            player.Interact();
            Assert.That(target.Calls, Is.EqualTo(1));
            player.SetUpgradeSelectionLocked(false);
            player.Interact();
            Assert.That(target.Calls, Is.EqualTo(2));
            UnityEngine.Object.DestroyImmediate(finder);
            Assert.That(ReferenceEquals(player.interactionFinder, null), Is.False, "Unity retains the managed reference after destruction.");
            Assert.DoesNotThrow(player.Interact);
            Assert.That(target.Calls, Is.EqualTo(2), "A destroyed finder must not invoke its old target.");
        }

        private sealed class CountingInteractionTrigger : Input2DTrigger
        {
            public int Calls { get; private set; }
            public override void Interact(IInteractor interactor) => Calls++;
        }

        [Test]
        public void BatchModeDoesNotImplyDedicatedServer()
        {
            Assert.That(GameplayRuntimeEnvironment.HasDedicatedServerArgument(new[] { "-batchmode", "-nographics" }), Is.False);
            Assert.That(GameplayRuntimeEnvironment.HasDedicatedServerArgument(new[] { "--dedicated-server" }), Is.True);
        }
    }
}
