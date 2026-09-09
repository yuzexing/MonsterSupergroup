using System;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.GAS;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class EnemyKnockbackContractTests
    {
        private KnockbackSettings preset;
        [SetUp]
        public void SetUp()
        {
            preset = ScriptableObject.CreateInstance<KnockbackSettings>();
            preset.distance = 2f;
            preset.speedMultiplier = 6f;
            preset.staggerTime = .6f;
            preset.speedCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }
        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(preset);

        [Test]
        public void PresetRoundTripPreservesAllAuthoredCurveValuesAndIsAnIndependentCopy()
        {
            preset.speedCurve = new AnimationCurve(
                new Keyframe(0, 0, 1, .3f, .2f, .4f) { weightedMode = WeightedMode.Both },
                new Keyframe(1, 1, .7f, 1, .25f, .5f) { weightedMode = WeightedMode.In });
            preset.speedCurve.preWrapMode = WrapMode.ClampForever;
            preset.speedCurve.postWrapMode = WrapMode.PingPong;
            EnemyKnockbackSettings encoded = EnemyKnockbackSettings.From(preset);
            KnockbackSettings decoded = encoded.CreateRuntimePreset();
            try
            {
                Assert.That(decoded, Is.Not.SameAs(preset));
                Assert.That(decoded.distance, Is.EqualTo(2f));
                Assert.That(decoded.speedMultiplier, Is.EqualTo(6f));
                Assert.That(decoded.staggerTime, Is.EqualTo(.6f));
                Assert.That(decoded.speedCurve.keys, Is.EqualTo(preset.speedCurve.keys));
                Assert.That(decoded.speedCurve.preWrapMode, Is.EqualTo(preset.speedCurve.preWrapMode));
                Assert.That(decoded.speedCurve.postWrapMode, Is.EqualTo(preset.speedCurve.postWrapMode));
                for (int i = 0; i <= 20; i++)
                    Assert.That(decoded.speedCurve.Evaluate(i / 20f), Is.EqualTo(preset.speedCurve.Evaluate(i / 20f)).Within(.00001f));
                decoded.distance = 99;
                Assert.That(preset.distance, Is.EqualTo(2f));
            }
            finally { UnityEngine.Object.DestroyImmediate(decoded); }
        }

        [TestCase(EnemySimulationHost.ClientPlayer, 7u, false, true)]
        [TestCase(EnemySimulationHost.ClientPlayer, 8u, false, false)]
        [TestCase(EnemySimulationHost.ClientPlayer, 7u, true, false)]
        [TestCase(EnemySimulationHost.ServerFallback, 0u, true, true)]
        [TestCase(EnemySimulationHost.ServerAuthoritative, 0u, true, true)]
        [TestCase(EnemySimulationHost.ServerAuthoritative, 7u, false, false)]
        [TestCase(EnemySimulationHost.Frozen, 0u, true, false)]
        public void OnlyCurrentAssignedSimulatorMayConsumeCommand(EnemySimulationHost host, uint receiver, bool server, bool allowed)
        {
            EnemySimulationAssignment assignment = Assignment();
            assignment.Host = host;
            var history = new EnemyKnockbackCommandHistory();
            Assert.That(history.TryAccept(Command(), assignment, receiver, server, 10d), Is.EqualTo(allowed));
            Assert.That(history.LastCommandId, Is.EqualTo(allowed ? 1UL : 0UL));
        }

        [Test]
        public void DuplicateAndOlderCommandsDoNotBecomeValidAfterAssignmentChanges()
        {
            var history = new EnemyKnockbackCommandHistory();
            EnemyKnockbackCommand command = Command();
            EnemySimulationAssignment assignment = Assignment();
            Assert.That(history.TryAccept(command, assignment, 7, false, 10), Is.True);
            Assert.That(history.TryAccept(command, assignment, 7, false, 10), Is.False);
            assignment.Epoch++;
            command.AssignmentEpoch++;
            Assert.That(history.TryAccept(command, assignment, 7, false, 10), Is.False);
            command.CommandId++;
            Assert.That(history.TryAccept(command, assignment, 7, false, 10), Is.True);
            command.CommandId--;
            Assert.That(history.TryAccept(command, assignment, 7, false, 10), Is.False);
        }

        [Test]
        public void OldEpochWrongEnemyOrWrongReceiverDoNotConsumeOrderingState()
        {
            var history = new EnemyKnockbackCommandHistory();
            EnemyKnockbackCommand command = Command();
            EnemySimulationAssignment assignment = Assignment();
            assignment.Epoch++;
            Assert.That(history.TryAccept(command, assignment, 7, false, 10), Is.False);
            assignment = Assignment();
            assignment.EnemyEntityId++;
            Assert.That(history.TryAccept(command, assignment, 7, false, 10), Is.False);
            Assert.That(history.TryAccept(command, Assignment(), 8, false, 10), Is.False);
            Assert.That(history.LastCommandId, Is.Zero);
            Assert.That(history.TryAccept(command, Assignment(), 7, false, 10), Is.True);
        }

        [TestCase("enemy")]
        [TestCase("epoch")]
        [TestCase("source")]
        [TestCase("ability")]
        [TestCase("root")]
        [TestCase("command")]
        [TestCase("time")]
        [TestCase("origin")]
        [TestCase("distance")]
        [TestCase("speed")]
        [TestCase("stagger")]
        [TestCase("direction")]
        [TestCase("curve")]
        [TestCase("curve order")]
        [TestCase("weighted mode")]
        public void MalformedCommandsCannotBeApplied(string defect)
        {
            EnemyKnockbackCommand command = Command();
            switch (defect)
            {
                case "enemy": command.EnemyEntityId = 0; break;
                case "epoch": command.AssignmentEpoch = 0; break;
                case "source": command.SourcePlayerId = 0; break;
                case "ability": command.AbilityCombatId = 2; break;
                case "root": command.RootEventId = 0; break;
                case "command": command.CommandId = 0; break;
                case "time": command.IssuedAt = double.NaN; break;
                case "origin": command.Origin.x = float.PositiveInfinity; break;
                case "distance": command.Settings.Distance = -1; break;
                case "speed": command.Settings.SpeedMultiplier = 0; break;
                case "stagger": command.Settings.StaggerTime = float.NaN; break;
                case "direction": command.Settings.FixedDirection = true; command.Settings.Direction = Vector2.zero; break;
                case "curve": command.Settings.CurveKeys = null; break;
                case "curve order": command.Settings.CurveKeys[1].Time = command.Settings.CurveKeys[0].Time; break;
                case "weighted mode": command.Settings.CurveKeys[0].WeightedMode = 257; break;
            }
            Assert.That(command.IsValid, Is.False);
            Assert.That(new EnemyKnockbackCommandHistory().TryAccept(command, Assignment(), 7, false, 10), Is.False);
        }

        [TestCase(7.999d, false)]
        [TestCase(8d, true)]
        [TestCase(10d, true)]
        [TestCase(10.1d, true)]
        [TestCase(10.101d, false)]
        public void TimestampWindowIsBounded(double issuedAt, bool allowed)
        {
            EnemyKnockbackCommand command = Command();
            command.IssuedAt = issuedAt;
            Assert.That(new EnemyKnockbackCommandHistory().TryAccept(command, Assignment(), 7, false, 10), Is.EqualTo(allowed));
        }

        [Test]
        public void IndependentEnemyReceiversAcceptSameRootAndDisconnectResetClearsHistory()
        {
            var first = new EnemyKnockbackCommandHistory();
            var second = new EnemyKnockbackCommandHistory();
            EnemyKnockbackCommand command = Command();
            Assert.That(first.TryAccept(command, Assignment(), 7, false, 10), Is.True);
            Assert.That(second.TryAccept(command, Assignment(), 7, false, 10), Is.True);
            first.Clear();
            Assert.That(first.LastCommandId, Is.Zero);
        }

        [Test]
        public void MirrorRoundTripPreservesIdentityAndServerPreset()
        {
            EnemyKnockbackCommand command = Command();
            var writer = new NetworkWriter();
            writer.Write(command);
            var reader = new NetworkReader(writer.ToArraySegment());
            EnemyKnockbackCommand read = reader.Read<EnemyKnockbackCommand>();
            Assert.That(read.IsValid, Is.True);
            Assert.That(read.RootEventId, Is.EqualTo(command.RootEventId));
            Assert.That(read.CommandId, Is.EqualTo(command.CommandId));
            Assert.That(read.AssignmentEpoch, Is.EqualTo(command.AssignmentEpoch));
            Assert.That(read.AbilityCombatId, Is.EqualTo(0x80000000u));
            Assert.That(read.Settings.Distance, Is.EqualTo(2));
            Assert.That(read.Settings.SpeedMultiplier, Is.EqualTo(6));
            Assert.That(read.Settings.StaggerTime, Is.EqualTo(.6f));
            Assert.That(read.Settings.CurveKeys, Is.EqualTo(command.Settings.CurveKeys));
            Assert.That(reader.Remaining, Is.Zero);
        }

        private EnemyKnockbackCommand Command() => new EnemyKnockbackCommand
        {
            EnemyEntityId = 42, AssignmentEpoch = 3, SourcePlayerId = 9, AbilityCombatId = 0x80000000u,
            RootEventId = CombatEventId.Compose(5, 2, 1).Value, CommandId = 1, IssuedAt = 10,
            Origin = Vector2.zero, Settings = EnemyKnockbackSettings.From(preset)
        };
        private static EnemySimulationAssignment Assignment() => new EnemySimulationAssignment
        {
            EnemyEntityId = 42, Epoch = 3, Host = EnemySimulationHost.ClientPlayer,
            SimulationOwnerPlayerId = 7, AggroTargetPlayerId = 9
        };
    }
}
