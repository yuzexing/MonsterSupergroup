using System;
using System.Linq;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MonsterSupergroup.Gameplay.Combat;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class PickupAssetTests
    {
        private const string Root = "Assets/_Project/Content/NetworkCombat/";
        [Test]
        public void ProductionWorldSharesXpRules_HealthHasSourceFlightCapacityAndOnlyPresentation()
        {
            var rules = AssetDatabase.LoadAssetAtPath<GameplayPickupRules>(Root + "GameplayPickupRules.asset");
            Assert.That(rules.experience, Is.SameAs(AssetDatabase.LoadAssetAtPath<GameplayExperienceRules>(Root + "GameplayExperienceRules.asset")));
            var world = new SerializedObject(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "NetworkCombatWorld.prefab").GetComponent<NetworkExperienceWorld>());
            Assert.That(world.FindProperty("pickupRules").objectReferenceValue, Is.SameAs(rules));
            var health = rules.healthItem.Capture();
            Assert.That(health.Value, Is.EqualTo(200)); Assert.That(health.WorldLimit, Is.EqualTo(4));
            Assert.That(health.IdleCapacity, Is.EqualTo(100)); Assert.That(rules.experienceItem.idleCapacity, Is.EqualTo(500));
            Assert.That(health.FlightDuration, Is.EqualTo(.8f).Within(.00001));
            Assert.That(health.Prefab.GetComponent<NetworkIdentity>().assetId, Is.Not.Zero);
            Assert.That(health.Prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(x => x == null), Is.False);
            Assert.That(health.Prefab.GetComponentsInChildren<Renderer>(true).All(GameplayPlanarEffect.UsesPlanarMaterial), Is.True);
            Assert.That(health.Prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(x => x.GetType().Name == "WorldItemHealth" || x.GetType().Name == "LootManager"), Is.False);
        }
        [Test]
        public void CapturedFlightCurvesDoNotChangeWhenAssetIsEdited_InvalidDurationRejected()
        {
            var source = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameplayPickupDefinition>(Root + "HealthPickup.asset"));
            try
            {
                var captured = source.Capture(); float middle = captured.BackCurve.Evaluate(.5f);
                source.backCurve = AnimationCurve.Constant(0, 1, 7);
                Assert.That(captured.BackCurve.Evaluate(.5f), Is.EqualTo(middle));
                source.jumpDuration = float.NaN;
                Assert.Throws<ArgumentException>(() => source.Capture());
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }
        [Test]
        public void ReceiptFieldsRoundTripAndBandwidthIncludesThem()
        {
            var original = new PlayerHealthReport { EventId = 12, Sequence = 3, PlayerId = 5, EntityId = 5,
                Health = 400, MaxHealth = 500, Alive = true, StateVersion = 9,
                PickupDropId = 18, PickupClaimVersion = 2, PickupRound = 3, PickupRestoredHealth = 200 };
            using var writer = NetworkWriterPool.Get(); writer.Write(original);
            using var reader = NetworkReaderPool.Get(writer.ToArraySegment()); var actual = reader.Read<PlayerHealthReport>();
            Assert.That(actual, Is.EqualTo(original));
            // Mirror packs integers; the contract estimator deliberately budgets uncompressed fields.
            Assert.That(writer.Position, Is.EqualTo(15), "Wire size for this known small-value fixture.");
            Assert.That(CombatBandwidthEstimator.PlayerHealthReportBytes, Is.EqualTo(53));
        }
    }
}
