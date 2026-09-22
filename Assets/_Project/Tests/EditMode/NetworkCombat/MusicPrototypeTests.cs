using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class MusicPrototypeTests
    {
        private static MusicPrototypeRuntime Start(out MusicParameters p)
        {
            p = MusicParameters.Defaults;
            var r = new MusicPrototypeRuntime();
            Assert.That(r.TryBegin(100, p, 10), Is.True);
            Assert.That(r.TrySchedule(100, 10.2, 10.1), Is.True);
            return r;
        }
        [Test] public void DefaultsMatchTenBeatPrototype()
        {
            var p = MusicParameters.Defaults;
            Assert.That(p.IsValid); Assert.That(p.Bpm, Is.EqualTo(120)); Assert.That(p.BeatCount, Is.EqualTo(10));
            Assert.That(p.CountInBeats, Is.EqualTo(2)); Assert.That(p.BeatTime(9), Is.EqualTo(5.5));
            Assert.That(p.Cooldown, Is.EqualTo(20)); Assert.That(p.SpeedBonus, Is.EqualTo(.25f));
        }
        [TestCase(-.12, true)] [TestCase(.12, true)] [TestCase(-.121, false)] [TestCase(.121, false)]
        public void WindowIncludesBothBoundaries(double offset, bool expected)
        {
            var r = Start(out var p); double t = p.BeatTime(0) + offset;
            Assert.That(r.TryJudge(100, 0, t, 10.2 + t + .1, out bool hit));
            Assert.That(hit, Is.EqualTo(expected));
        }
        [Test] public void FullComboCompletesExactlyOnceAndCooldownBeginsAtEnd()
        {
            var r = Start(out var p);
            for (int i = 0; i < 10; i++)
                Assert.That(r.TryJudge(100, i, p.BeatTime(i), 10.2 + p.BeatTime(i), out bool hit) && hit);
            Assert.That(r.State.Active, Is.False); Assert.That(r.State.Hits, Is.EqualTo(10));
            Assert.That(r.State.CooldownReadyAt, Is.EqualTo(35.7).Within(.0001));
            Assert.That(r.TryJudge(100, 9, 5.5, 15.7, out _), Is.False);
            Assert.That(r.TryBegin(101, p, 35.69), Is.False); Assert.That(r.TryBegin(101, p, 35.7), Is.True);
        }
        [Test] public void OneEarlyMissDoesNotCancelRemainingNineBeats()
        {
            var r = Start(out var p);
            Assert.That(r.TryJudge(100, 0, .8, 11, out bool first)); Assert.That(first, Is.False);
            Assert.That(r.TryJudge(100, 0, 1, 11.2, out _), Is.False);
            for (int i = 1; i < 10; i++) Assert.That(r.TryJudge(100, i, p.BeatTime(i), 10.2 + p.BeatTime(i), out _));
            Assert.That(r.State.Hits, Is.EqualTo(9)); Assert.That(r.State.Cancelled, Is.False);
        }
        [Test] public void AllOmittedBeatsEventuallyFinishWithCooldown()
        {
            var r = Start(out var p); Assert.That(r.Advance(18));
            Assert.That(r.State.Active, Is.False); Assert.That(r.State.Judged, Is.EqualTo(10));
            Assert.That(r.State.Hits, Is.Zero); Assert.That(r.State.CooldownReadyAt, Is.EqualTo(38));
            Assert.That(r.Advance(19), Is.False);
        }
        [Test] public void LateDeliveryUsesInputTimeInsteadOfArrivalTime()
        {
            var r = Start(out _);
            Assert.That(r.TryJudge(100, 0, 1, 12.2, out bool hit)); Assert.That(hit);
        }
        [Test] public void WrongCastDuplicateFutureAndExpiredClaimsAreRejected()
        {
            var r = Start(out _);
            Assert.That(r.TryJudge(101, 0, 1, 11.2, out _), Is.False);
            Assert.That(r.TryJudge(100, 1, 1, 11.2, out _), Is.False);
            Assert.That(r.TryJudge(100, 0, 1, 10.3, out _), Is.False);
            Assert.That(r.TryJudge(100, 0, 1, 13, out _), Is.False);
            Assert.That(r.TryJudge(100, 0, 1, 11.3, out _), Is.True);
            Assert.That(r.TryJudge(100, 0, 1, 11.3, out _), Is.False);
        }
        [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)] [TestCase(double.NegativeInfinity)]
        public void InvalidTimestampsCannotConsumeABeat(double time)
        {
            var r = Start(out _); Assert.That(r.TryJudge(100, 0, time, 11.2, out _), Is.False);
            Assert.That(r.State.Judged, Is.Zero);
        }
        [Test] public void SpamOwnsOnlyNearestSlotAndCountInHasNoJudgment()
        {
            var p = MusicParameters.Defaults;
            Assert.That(MusicTiming.InputIndex(p, .2), Is.EqualTo(-1));
            Assert.That(MusicTiming.InputIndex(p, .75), Is.EqualTo(0));
            Assert.That(MusicTiming.InputIndex(p, 1.249), Is.EqualTo(0));
            Assert.That(MusicTiming.InputIndex(p, 1.25), Is.EqualTo(1));
            Assert.That(MusicTiming.InputIndex(p, 5.75), Is.EqualTo(-1));
        }
        [Test] public void SwitchingCannotRestartLiveRuntimeAndCancellationStartsCooldownOnce()
        {
            var r = Start(out var p); Assert.That(r.TryBegin(101, p, 11), Is.False);
            Assert.That(r.Cancel(12)); Assert.That(r.State.CooldownReadyAt, Is.EqualTo(32));
            Assert.That(r.Cancel(13), Is.False); Assert.That(r.State.CooldownReadyAt, Is.EqualTo(32));
            Assert.That(r.TryJudge(100, 3, 2.5, 12.7, out _), Is.False);
        }
        [Test] public void ScheduleCannotBeMovedByReplayAndMissingScheduleTimesOut()
        {
            var r = Start(out var p); Assert.That(r.TrySchedule(100, 12, 12), Is.False);
            Assert.That(r.State.StartedAt, Is.EqualTo(10.2));
            var missing = new MusicPrototypeRuntime(); missing.TryBegin(1, p, 1);
            Assert.That(missing.Advance(4)); Assert.That(missing.State.Cancelled); Assert.That(missing.State.CooldownReadyAt, Is.EqualTo(24));
        }
    }
}
