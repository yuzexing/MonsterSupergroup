using MonsterSupergroup.Builds;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class BuildConnectionTests
    {
        [TestCase("0.0.2", "0.0.10", BuildRejection.ClientOlder)]
        [TestCase("1.0.0", "0.99.99", BuildRejection.HostOlder)]
        [TestCase("0.0.0", "0.0.0", BuildRejection.None)]
        [TestCase("1.0", "0.0.0", BuildRejection.UnknownVersion)]
        [TestCase(null, "0.0.0", BuildRejection.UnknownVersion)]
        public void GameVersionComparisonIsNumeric(string client, string host, BuildRejection expected) => Assert.That(BuildCompatibility.Compare(client, host), Is.EqualTo(expected));
        [Test] public void ProtocolMismatchIsNotReportedAsAnOlderGame()
        {
            Assert.That(BuildCompatibility.CheckIdentity("5:0.0.0", "0.0.1", "6", out _), Is.EqualTo(BuildRejection.ProtocolMismatch));
            Assert.That(BuildCompatibility.CheckIdentity("6:0.0.0", "0.0.1", "6", out var version), Is.EqualTo(BuildRejection.ClientOlder));
            Assert.That(version, Is.EqualTo("0.0.0"));
        }
        [TestCase(true)] [TestCase(false)]
        public void NoticePreservesBothVersionsWithoutChangingWireFields(bool steam)
        {
            string value = BuildCompatibility.Encode(BuildRejection.HostOlder, "0.0.2", "0.0.1", steam);
            Assert.That(BuildCompatibility.TryDecode(value, out var reason, out var client, out var host, out var isSteam), Is.True);
            Assert.That(reason, Is.EqualTo(BuildRejection.HostOlder)); Assert.That(client, Is.EqualTo("0.0.2"));
            Assert.That(host, Is.EqualTo("0.0.1")); Assert.That(isSteam, Is.EqualTo(steam));
        }
        [TestCase("@build-notice:ClientOlder|<bad>|0.0.0|steam")]
        [TestCase("@build-notice:99|0.0.0|0.0.1|steam")]
        [TestCase("arbitrary remote text")]
        public void InvalidNoticePayloadIsNotInterpreted(string value) => Assert.That(BuildCompatibility.TryDecode(value, out _, out _, out _, out _), Is.False);
        [Test] public void SpecificAdmissionSurvivesTransportAndDisconnectCallbacks()
        {
            var state = new ConnectionNoticeState(); state.Begin();
            state.Set(state.Attempt, "version", 100);
            Assert.That(state.Set(state.Attempt, "transport", 10), Is.False);
            Assert.That(state.Set(state.Attempt, "disconnect", 50), Is.False);
            Assert.That(state.Message, Is.EqualTo("version"));
        }
        [Test] public void HostClosedWinsInEitherCallbackOrder()
        {
            var state = new ConnectionNoticeState(); state.Begin(); state.MarkConnected();
            state.Set(state.Attempt, "transport", 10); state.Set(state.Attempt, "lost", 50); state.Set(state.Attempt, "closed", 80);
            Assert.That(state.Message, Is.EqualTo("closed")); Assert.That(state.Connected, Is.True);
            Assert.That(state.Set(state.Attempt, "lost", 50), Is.False);
        }
        [Test] public void LocalLeaveAndOldAttemptDoNotOverwriteNewConnection()
        {
            var state = new ConnectionNoticeState(); state.Begin(); uint old = state.Attempt; state.LeaveLocally();
            Assert.That(state.Set(old, "lost", 50), Is.False);
            state.Begin(); Assert.That(state.Connected, Is.False);
            Assert.That(state.Set(old, "closed", 80), Is.False);
            Assert.That(state.Set(state.Attempt, "new-error", 10), Is.True);
        }
    }
}
