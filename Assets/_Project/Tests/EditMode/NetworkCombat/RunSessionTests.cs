using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class RunSessionTests
    {
        [Test]
        public void LockedRoster_ResumesOriginalParticipantWithNewAvatarAndEpoch()
        {
            var session = new RunSession();
            Assert.That(session.TryConnect("verified:a", 1, out var first, out _), Is.True);
            Assert.That(session.TryConnect("verified:b", 2, out var second, out _), Is.True);
            session.AttachAvatar(1, 10, 3);
            session.AttachAvatar(2, 11, 4);
            session.BeginRun();
            var state = new PlayerRuntimeCheckpoint { PreviousAvatarId = 10, LifeState = RunPlayerLifeState.Downed };
            session.Disconnect(1, state);
            Assert.That(first.AvatarId, Is.Zero);
            Assert.That(first.ConnectionState, Is.EqualTo(RunConnectionState.Disconnected));
            Assert.That(session.TryConnect("verified:c", 3, out _, out _), Is.False);
            Assert.That(session.TryConnect("verified:a", 3, out var resumed, out _), Is.True);
            session.AttachAvatar(3, 20, 5);
            Assert.That(resumed, Is.SameAs(first));
            Assert.That(resumed.Checkpoint, Is.SameAs(state));
            Assert.That(resumed.LifeState, Is.EqualTo(RunPlayerLifeState.Downed));
            Assert.That(resumed.AvatarId, Is.EqualTo(20));
            Assert.That(resumed.ConnectionEpoch, Is.EqualTo(5));
            Assert.That(second.AvatarId, Is.EqualTo(11));
            Assert.That(second.Checkpoint, Is.Null);
        }

        [Test]
        public void DuplicateAndStaleConnectionsCannotStealParticipantOrCheckpoint()
        {
            var session = new RunSession();
            session.TryConnect("verified:a", 1, out var first, out _);
            Assert.That(session.TryConnect("verified:a", 2, out _, out _), Is.False);
            Assert.That(session.TryConnect("verified:b", 1, out _, out _), Is.False);
            session.Disconnect(1, new PlayerRuntimeCheckpoint { CapturedAt = 10 });
            session.TryConnect("verified:a", 2, out _, out _);
            session.Disconnect(1, new PlayerRuntimeCheckpoint { CapturedAt = 30 });
            Assert.That(first.ConnectionState, Is.EqualTo(RunConnectionState.Connected));
            Assert.That(first.Checkpoint.CapturedAt, Is.EqualTo(10));
        }

        [Test]
        public void NewSessionDoesNotInheritPriorRosterOrRunIdentity()
        {
            var first = new RunSession();
            first.TryConnect("a", 0, out _, out _);
            first.BeginRun();
            var next = new RunSession();
            Assert.That(next.RunId, Is.Not.EqualTo(first.RunId));
            Assert.That(next.IsRosterLocked, Is.False);
            Assert.That(next.Participants, Is.Empty);
        }
    }
}
