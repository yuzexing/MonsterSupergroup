using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class AllurePrototypeTests
    {
        [Test]
        public void DefaultsUseThreeIndependentTwentySecondCooldownsAndFiveSecondDecoy()
        {
            var p = AllureParameters.Defaults;
            Assert.That(p.IsValid); Assert.That(p.MaximumTargets, Is.EqualTo(10));
            Assert.That(p.ThrowCooldown, Is.EqualTo(20)); Assert.That(p.TakeCooldown, Is.EqualTo(20));
            Assert.That(p.DecoyCooldown, Is.EqualTo(20)); Assert.That(p.DecoyDuration, Is.EqualTo(5));
        }
        [Test]
        public void AcceptedThrowDoesNotConsumeTakeOrDecoyAndCooldownExpiresAtBoundary()
        {
            var p = AllureParameters.Defaults; var runtime = new AllurePrototypeRuntime();
            Assert.That(runtime.Accept(1, AllureAction.Throw, p, 10, 3, default, 0));
            Assert.That(runtime.CanBegin(AllureAction.Throw, p, 29.999), Is.False);
            Assert.That(runtime.CanBegin(AllureAction.Throw, p, 30), Is.True);
            Assert.That(runtime.CanBegin(AllureAction.Take, p, 10));
            Assert.That(runtime.CanBegin(AllureAction.Decoy, p, 10));
            Assert.That(runtime.Accept(2, AllureAction.Take, p, 11, 1, default, 0));
            Assert.That(runtime.State.ThrowReadyAt, Is.EqualTo(30));
            Assert.That(runtime.State.TakeReadyAt, Is.EqualTo(31));
        }
        [TestCase(0)] [TestCase(-1)] [TestCase(11)]
        public void InvalidTargetCountDoesNotConsumeCooldown(int count)
        {
            var runtime = new AllurePrototypeRuntime();
            Assert.That(runtime.Accept(1, AllureAction.Throw, AllureParameters.Defaults, 10, count, default, 0), Is.False);
            Assert.That(runtime.State.Revision, Is.Zero); Assert.That(runtime.State.ThrowReadyAt, Is.Zero);
        }
        [Test]
        public void OldDecoyCleanupCannotClearReplacementAndCleanupKeepsCooldowns()
        {
            var p = AllureParameters.Defaults; p.DecoyCooldown = .1f;
            var runtime = new AllurePrototypeRuntime();
            Assert.That(runtime.Accept(1, AllureAction.Decoy, p, 10, 2, Vector2.one, 15));
            Assert.That(runtime.Accept(2, AllureAction.Decoy, p, 11, 3, Vector2.right, 16));
            Assert.That(runtime.ClearDecoy(1), Is.False);
            Assert.That(runtime.State.DecoyCastId, Is.EqualTo(2));
            Assert.That(runtime.State.DecoyPosition, Is.EqualTo(Vector2.right));
            double readyAt = runtime.State.DecoyReadyAt;
            Assert.That(runtime.ClearDecoy(2)); Assert.That(runtime.State.DecoyCastId, Is.Zero);
            Assert.That(runtime.State.DecoyReadyAt, Is.EqualTo(readyAt));
            Assert.That(runtime.ClearDecoy(2), Is.False);
        }
        [Test]
        public void TransferWhileDecoyActivePreservesDecoyLifetime()
        {
            var p = AllureParameters.Defaults; var runtime = new AllurePrototypeRuntime();
            Assert.That(runtime.Accept(1, AllureAction.Decoy, p, 10, 4, Vector2.one, 15));
            Assert.That(runtime.Accept(2, AllureAction.Throw, p, 11, 2, default, 0));
            Assert.That(runtime.State.DecoyCastId, Is.EqualTo(1));
            Assert.That(runtime.State.DecoyExpiresAt, Is.EqualTo(15));
            Assert.That(runtime.State.LastAffectedCount, Is.EqualTo(2));
        }
        [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)] [TestCase(double.NegativeInfinity)]
        public void NonFiniteTimeCannotMutateState(double now)
        {
            var runtime = new AllurePrototypeRuntime();
            Assert.That(runtime.Accept(1, AllureAction.Throw, AllureParameters.Defaults, now, 1, default, 0), Is.False);
            Assert.That(runtime.State.Revision, Is.Zero);
        }
        [Test]
        public void MalformedDecoyAndDuplicateCastDoNotChangeAcceptedState()
        {
            var runtime = new AllurePrototypeRuntime(); var p = AllureParameters.Defaults;
            Assert.That(runtime.Accept(1, AllureAction.Decoy, p, 10, 1, new Vector2(float.NaN, 0), 15), Is.False);
            Assert.That(runtime.Accept(1, AllureAction.Decoy, p, 10, 1, Vector2.zero, 10), Is.False);
            Assert.That(runtime.Accept(1, AllureAction.Throw, p, 10, 1, default, 0));
            uint revision = runtime.State.Revision;
            Assert.That(runtime.Accept(1, AllureAction.Throw, p, 40, 1, default, 0), Is.False);
            Assert.That(runtime.State.Revision, Is.EqualTo(revision));
            Assert.That(runtime.State.ThrowReadyAt, Is.EqualTo(30));
        }
        [Test]
        public void CooldownResetIsExplicitAndDoesNotSilentlyRemoveDecoy()
        {
            var runtime = new AllurePrototypeRuntime(); var p = AllureParameters.Defaults;
            Assert.That(runtime.Accept(1, AllureAction.Decoy, p, 10, 1, Vector2.zero, 15));
            runtime.ResetCooldowns();
            Assert.That(runtime.State.DecoyReadyAt, Is.Zero);
            Assert.That(runtime.State.DecoyCastId, Is.EqualTo(1));
        }
        [Test]
        public void InvalidParametersAndUnknownActionAreRejected()
        {
            var p = AllureParameters.Defaults; var runtime = new AllurePrototypeRuntime();
            Assert.That(runtime.CanBegin((AllureAction)255, p, 10), Is.False);
            p.ThrowCooldown = float.NaN; Assert.That(p.IsValid, Is.False);
            Assert.That(runtime.CanBegin(AllureAction.Throw, p, 10), Is.False);
            p = AllureParameters.Defaults; p.MaximumTargets = 0; Assert.That(p.IsValid, Is.False);
            p = AllureParameters.Defaults; p.DecoyDuration = float.PositiveInfinity; Assert.That(p.IsValid, Is.False);
        }
    }
}
