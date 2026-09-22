using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class SteamLobbyConnectionTests
    {
        private const ulong Lobby = 109775242349881186;
        private const ulong OtherLobby = 109775242349881187;

        [TestCase("+connect_lobby 109775242349881186")]
        [TestCase("--connect-lobby=109775242349881186")]
        [TestCase("  +connect_lobby\t109775242349881186  ")]
        [TestCase("game.exe --network-diagnostics +connect_lobby 109775242349881186")]
        public void BothSteamLaunchFormatsResolveOnlyTheLobby(string text)
        { Assert.That(SteamLobbyConnection.Parse(text), Is.EqualTo(Lobby)); }

        [TestCase(null)] [TestCase("")] [TestCase("+connect_lobby")]
        [TestCase("+connect_lobby 0")] [TestCase("+connect_lobby 42")]
        [TestCase("+connect_lobby 76561198000000001")]
        [TestCase("--connect-lobby=-1")]
        [TestCase("--connect-lobby=18446744073709551616")]
        [TestCase("+connect_lobby 109775242349881186;anything")]
        [TestCase("+connect_lobby 109775242349881186 --connect-lobby=109775242349881187")]
        public void InvalidOrConflictingRequestsCannotTriggerALeave(string text)
        { Assert.That(SteamLobbyConnection.Parse(text), Is.Zero); }

        [Test] public void FormattedConnectionRoundTripsAndInvalidIdCannotBePublished()
        {
            Assert.That(SteamLobbyConnection.Parse(SteamLobbyConnection.Format(Lobby)), Is.EqualTo(Lobby));
            Assert.Throws<ArgumentException>(() => SteamLobbyConnection.Format(42));
            Assert.That(SteamLobbyConnection.Parse(new string(' ', 4097) + SteamLobbyConnection.Format(Lobby)), Is.Zero);
        }

        [Test] public void SteamStartupRequestWinsButOrdinaryLaunchOptionsDoNotHideLegacyInvite()
        {
            string[] process = { "game.exe", "+connect_lobby", Lobby.ToString() };
            Assert.That(SteamLobbyConnection.ParseStartup("--network-diagnostics", process), Is.EqualTo(Lobby));
            Assert.That(SteamLobbyConnection.ParseStartup(SteamLobbyConnection.Format(OtherLobby), process), Is.EqualTo(OtherLobby));
            Assert.That(SteamLobbyConnection.ParseStartup("+connect_lobby invalid", process), Is.Zero);
        }

        [TestCase(true, true, true, 2, 2, 4, true)]
        [TestCase(false, true, true, 2, 2, 4, false)]
        [TestCase(true, false, true, 2, 2, 4, false)]
        [TestCase(true, true, false, 2, 2, 4, false)]
        [TestCase(true, true, true, 4, 2, 4, false)]
        [TestCase(true, true, true, 2, 4, 4, false)]
        [TestCase(true, true, true, 0, 2, 4, false)]
        [TestCase(true, true, true, 2, 0, 4, false)]
        [TestCase(true, true, true, 2, 2, 0, false)]
        public void MemberPresenceRequiresAuthenticationOpenPreparationAndBothCapacities(bool authenticated,
            bool preparing, bool member, int steamCount, int roomCount, int limit, bool expected)
        {
            Assert.That(SteamLobbyPresence.ConnectionFor(Lobby, authenticated, preparing, member, steamCount, roomCount, limit),
                Is.EqualTo(expected ? SteamLobbyConnection.Format(Lobby) : string.Empty));
        }

        [Test] public void PresencePublishesOnceClearsImmediatelyAndRepublishesAfterRoomChange()
        {
            var sent = new List<string>();
            var presence = new SteamLobbyPresence(value => { sent.Add(value); return true; }, _ => { });
            string first = SteamLobbyConnection.Format(Lobby), second = SteamLobbyConnection.Format(OtherLobby);
            presence.Update(first, 0); presence.Update(first, 1);
            presence.Update(string.Empty, 2); presence.Update(string.Empty, 3);
            presence.Update(second, 4);
            Assert.That(sent, Is.EqualTo(new[] { first, string.Empty, second }));
        }

        [TestCase(false)] [TestCase(true)]
        public void FailedPresenceIsRetriedWithoutRepeatingEveryFrame(bool throws)
        {
            int calls = 0; bool success = false;
            var presence = new SteamLobbyPresence(value => { calls++; if (throws && !success) throw new Exception(); return success; }, _ => { });
            string connection = SteamLobbyConnection.Format(Lobby);
            presence.Update(connection, 0); presence.Update(connection, 4.99);
            Assert.That(calls, Is.EqualTo(1));
            success = true; presence.Update(connection, 5); presence.Update(connection, 6);
            Assert.That(calls, Is.EqualTo(2));
        }

        [Test] public void LatestAcceptedInviteWaitsForCleanupAndIsConsumedExactlyOnce()
        {
            var queue = new SteamLobbyJoinQueue();
            Assert.That(queue.Request(Lobby, 0), Is.True);
            Assert.That(queue.Request(Lobby, 1), Is.False);
            Assert.That(queue.Take(false, 2, out _), Is.Zero);
            Assert.That(queue.Request(OtherLobby, 3), Is.True);
            Assert.That(queue.Take(true, 4, out bool timeout), Is.EqualTo(OtherLobby));
            Assert.That(timeout, Is.False);
            Assert.That(queue.Take(true, 5, out _), Is.Zero);
        }

        [Test] public void TimeoutClearsRequestAndLateCleanupDoesNotJoin()
        {
            var queue = new SteamLobbyJoinQueue(); queue.Request(Lobby, 10);
            queue.Request(Lobby, 39); // Duplicates must not extend the cleanup deadline.
            Assert.That(queue.Take(false, 40, out bool timeout), Is.Zero);
            Assert.That(timeout, Is.True);
            Assert.That(queue.Take(true, 41, out _), Is.Zero);
            Assert.That(queue.Request(42, 42), Is.False);
        }
    }
}
