using System;
using System.Reflection;
using AstralShift.HellMaiden.AI;
using Mirror;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class EnemyRuntimeSerializationTests
    {
        [TestCase(false)] [TestCase(true)]
        public void EmptyRuntimeUsesAtMostTwoBytesAndSnapshotAtMostForty(bool emptyReceipts)
        {
            var snapshot = new EnemySimulationSnapshot
            {
                EnemyEntityId = 100, AssignmentEpoch = 1, Sequence = 1,
                Runtime = new EnemySimulationRuntimeState
                { PredictedKnockbacks = emptyReceipts ? Array.Empty<EnemyPredictedKnockbackReceipt>() : null }
            };
            var writer = new NetworkWriter();
            writer.Write(snapshot);
            Assert.That(writer.Position, Is.LessThanOrEqualTo(40), "The generated snapshot writer must use the compact runtime serializer.");
            var reader = new NetworkReader(writer.ToArraySegment());
            AssertFields(snapshot, reader.Read<EnemySimulationSnapshot>(), "snapshot");
            Assert.That(reader.Remaining, Is.Zero);
            TestContext.WriteLine("Idle snapshot bytes: " + writer.Position);
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void EveryRuntimeFieldSurvivesLosslessRoundTrip(int seed)
        {
            var random = new System.Random(seed);
            var expected = (EnemySimulationRuntimeState)Populate(typeof(EnemySimulationRuntimeState), random);
            var writer = new NetworkWriter();
            writer.WriteEnemySimulationRuntimeState(expected);
            var reader = new NetworkReader(writer.ToArraySegment());
            AssertFields(expected, reader.ReadEnemySimulationRuntimeState(), "runtime");
            Assert.That(reader.Remaining, Is.Zero);
        }

        [Test]
        public void InactiveBlocksAndSubEpsilonVectorsAreNotDiscarded()
        {
            var expected = new EnemySimulationRuntimeState
            {
                Action = new EnemyActionState
                {
                    DashStart = new Vector2(1e-12f, 0), ExplosionPosition = new Vector2(0, 1e-13f),
                    ProjectileDirection = new Vector2(1e-14f, 0), SequenceWarnings = new Vector3(0, 0, 1e-15f),
                    WarningStep = new EnemyWarningStepState { Facing = new Vector2(1e-16f, 0) }
                },
                Knockback = new EnemyKnockbackMotionState { LastPosition = new Vector2(1e-17f, 0) },
                KnockbackSettings = new EnemyKnockbackSettings { Direction = new Vector2(1e-18f, 0), CurveKeys = Array.Empty<EnemyKnockbackCurveKey>() }
            };
            var writer = new NetworkWriter(); writer.WriteEnemySimulationRuntimeState(expected);
            AssertFields(expected, new NetworkReader(writer.ToArraySegment()).ReadEnemySimulationRuntimeState(), "tiny");
        }

        [Test]
        public void CachedKnockbackReceiptsAreImmutableAndExpire()
        {
            var history = new EnemyPredictedKnockbackHistory();
            history.TryRemember(1, 0);
            var first = history.Capture(0);
            Assert.That(history.Capture(1), Is.SameAs(first));
            history.TryRemember(2, 1);
            var second = history.Capture(1);
            Assert.That(second, Has.Length.EqualTo(2));
            Assert.That(first, Has.Length.EqualTo(1));
            history.Acknowledge(1);
            Assert.That(history.Capture(2)[0].DamageEventId, Is.EqualTo(2));
            Assert.That(history.Capture(4), Is.Empty);
            Assert.That(second, Has.Length.EqualTo(2));
        }

        [Test]
        public void SteamBatchingIsIndependentOfMaximumMessageSize()
        {
            var obj = new GameObject("Steam batch size test"); obj.SetActive(false);
            try
            {
                var type = Type.GetType("Mirror.FizzySteam.FizzySteamworks, FizzySteamworks", true);
                var transport = (Transport)obj.AddComponent(type);
                Assert.That(transport.GetBatchThreshold(Channels.Unreliable), Is.EqualTo(1200));
                Assert.That(transport.GetBatchThreshold(Channels.Reliable), Is.EqualTo(1200));
                Assert.That(transport.GetMaxPacketSize(Channels.Reliable), Is.EqualTo(512 * 1024 - 1));
            }
            finally { UnityEngine.Object.DestroyImmediate(obj); }
        }

        private static object Populate(Type type, System.Random random)
        {
            if (type.IsEnum) return Enum.ToObject(type, random.Next(0, 4));
            if (type == typeof(bool)) return random.Next(2) == 0;
            if (type == typeof(float)) return (float)(random.NextDouble() * 10 + .25);
            if (type == typeof(double)) return random.NextDouble() * 100 + .125;
            if (type == typeof(byte)) return (byte)random.Next(1, 4);
            if (type == typeof(int)) return random.Next(-2, 5);
            if (type == typeof(uint)) return (uint)random.Next(1, 300);
            if (type == typeof(ulong)) return ((ulong)random.Next(1, 100) << 48) + (ulong)random.Next(1, 500);
            if (type.IsArray)
            {
                var array = Array.CreateInstance(type.GetElementType(), 2);
                for (int i = 0; i < 2; i++) array.SetValue(Populate(type.GetElementType(), random), i);
                return array;
            }
            var value = Activator.CreateInstance(type);
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public)) field.SetValue(value, Populate(field.FieldType, random));
            return value;
        }
        private static void AssertFields(object expected, object actual, string path)
        {
            if (expected == null || actual == null) { Assert.That(actual, Is.EqualTo(expected), path); return; }
            var type = expected.GetType();
            if (type.IsPrimitive || type.IsEnum) { Assert.That(actual, Is.EqualTo(expected), path); return; }
            if (expected is Array expectedArray)
            {
                var actualArray = (Array)actual;
                Assert.That(actualArray.Length, Is.EqualTo(expectedArray.Length), path);
                for (int i = 0; i < expectedArray.Length; i++) AssertFields(expectedArray.GetValue(i), actualArray.GetValue(i), path + "[" + i + "]");
                return;
            }
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                AssertFields(field.GetValue(expected), field.GetValue(actual), path + "." + field.Name);
        }
    }
}
