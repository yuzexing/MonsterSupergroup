using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class SteamLobbyInvitationTests
    {
        private FakeSteam api;
        private SteamLobbyInvitations invitations;
        private SteamInviteContext context;
        private double time;
        [SetUp] public void SetUp()
        {
            api = new FakeSteam(); context = new SteamInviteContext(42, true, true); time = 10;
            invitations = new SteamLobbyInvitations(api, () => context, () => time);
        }
        [Test] public void AllFriends_OnlineFirst_NameThenId_SelfExcluded_DuplicatesRemoved_OfflineRetained()
        {
            api.Friends = new List<SteamInviteFriendInfo> {
                Friend(1, "Self"), Friend(5, "Anna", SteamFriendPresence.Offline), Friend(4, "Ben"),
                Friend(3, "Anna"), Friend(2, "Anna"), Friend(2, "Anna"), Friend(6, "离线", SteamFriendPresence.Offline)
            };
            Assert.That(invitations.Refresh().Friends.Select(f => f.SteamId), Is.EqualTo(new ulong[] { 2, 3, 4, 5, 6 }));
        }
        [TestCase(false)] [TestCase(true)]
        public void OfficialSend_ExactLobbyAndFriend_OfflineAllowed_HostAndClient(bool client)
        {
            if (client) { api.LocalUserId = 9; api.Lobby.Members = new ulong[] { 1, 9 }; }
            api.Friends[0] = Friend(2, "Offline friend", SteamFriendPresence.Offline);
            var result = invitations.Send(42, 2);
            Assert.That(result.Submitted, Is.True);
            Assert.That(result.Message, Is.EqualTo("邀请已提交，等待好友接受"));
            Assert.That(api.Sent, Is.EqualTo(new[] { (42ul, 2ul) }));
            Assert.That(api.Lobby.Members.Contains(2ul), Is.False, "Submission never reserves a seat.");
        }
        [Test] public void RapidClicksAndRepeatedRefresh_OnlyOneSubmissionWithinFiveSeconds()
        {
            Assert.That(invitations.Send(42, 2).Submitted, Is.True);
            for (int i = 0; i < 20; i++)
            { invitations.Refresh(); Assert.That(invitations.Send(42, 2).Code, Is.EqualTo("cooldown")); }
            time = 14.999;
            Assert.That(invitations.Send(42, 2).Submitted, Is.False);
            time = 15;
            Assert.That(invitations.Send(42, 2).Submitted, Is.True);
            Assert.That(api.Sent.Count, Is.EqualTo(2));
        }
        [TestCase(false)] [TestCase(true)] public void FalseOrException_AllowsImmediateManualRetry(bool throws)
        {
            api.ThrowOnSend = throws; api.SendSucceeds = false;
            Assert.That(invitations.Send(42, 2).Submitted, Is.False);
            Assert.That(invitations.Refresh().Friends.Single().LastResult.Submitted, Is.False);
            api.ThrowOnSend = false; api.SendSucceeds = true;
            Assert.That(invitations.Send(42, 2).Submitted, Is.True);
        }
        [TestCase("uninitialized", "steam_unavailable")]
        [TestCase("logged_out", "steam_unavailable")]
        [TestCase("loading", "not_preparing")]
        [TestCase("remote_loading", "not_preparing")]
        [TestCase("host_left", "host_left")]
        [TestCase("local_left", "not_member")]
        [TestCase("full", "full")]
        [TestCase("capacity_missing", "capacity_unknown")]
        [TestCase("removed_friend", "not_friend")]
        [TestCase("joined_friend", "already_member")]
        [TestCase("old_lobby", "stale_lobby")]
        public void SendRevalidatesChangesAfterPageOpened(string change, string expected)
        {
            Assert.That(invitations.Refresh().CanSend, Is.True);
            switch (change) {
                case "uninitialized": context = new SteamInviteContext(42, false, true); break;
                case "logged_out": api.IsLoggedOn = false; break;
                case "loading": context = new SteamInviteContext(42, true, false); break;
                case "remote_loading": api.Lobby.Preparing = false; break;
                case "host_left": api.Lobby.ValidHost = false; break;
                case "local_left": api.Lobby.Members = new ulong[] { 9 }; break;
                case "full": api.Lobby.Members = new ulong[] { 1, 7, 8, 9 }; break;
                case "capacity_missing": api.Lobby.Limit = 0; break;
                case "removed_friend": api.Friends.Clear(); break;
                case "joined_friend": api.Lobby.Members = new ulong[] { 1, 2 }; break;
                case "old_lobby": context = new SteamInviteContext(43, true, true); break;
            }
            Assert.That(invitations.Send(42, 2).Code, Is.EqualTo(expected));
            Assert.That(api.Sent, Is.Empty);
        }
        [Test] public void FullLobby_StillShowsMembersAndDisablesInvites()
        {
            api.Lobby.Members = new ulong[] { 1, 2, 3, 4 };
            var snapshot = invitations.Refresh();
            Assert.That(snapshot.CanSend, Is.False);
            Assert.That(snapshot.Friends.Single().InLobby, Is.True);
        }
        [Test] public void ExitOrLoading_ClearsSnapshotAndCooldown_NewLobbyRejectsOldPage()
        {
            invitations.Send(42, 2); var old = invitations.Refresh();
            context = new SteamInviteContext(42, true, false); invitations.SynchronizeContext();
            Assert.That(invitations.Snapshot.Friends, Is.Empty);
            Assert.That(invitations.Snapshot.LobbyId, Is.Zero);
            Assert.That(old.Friends.Count, Is.EqualTo(1), "Published snapshots remain immutable.");
            context = new SteamInviteContext(43, true, true);
            Assert.That(invitations.Send(42, 2).Code, Is.EqualTo("stale_lobby"));
            Assert.That(invitations.Send(43, 2).Submitted, Is.True);
            invitations.Clear();
            Assert.That(invitations.Snapshot.Friends, Is.Empty);
        }
        [Test] public void QueryFailure_IsRecoverableAndNeverSends()
        {
            api.ThrowOnRead = true;
            Assert.That(invitations.Refresh().Error, Does.Contain("无法读取"));
            api.ThrowOnRead = false;
            Assert.That(invitations.Refresh().CanSend, Is.True);
            Assert.That(api.Sent, Is.Empty);
        }
        private static SteamInviteFriendInfo Friend(ulong id, string name, SteamFriendPresence presence = SteamFriendPresence.Online)
            => new SteamInviteFriendInfo(id, name, presence);
        private sealed class FakeSteam : ISteamInviteApi
        {
            internal bool IsLoggedOn = true, SendSucceeds = true, ThrowOnSend, ThrowOnRead;
            internal ulong LocalUserId = 1;
            internal List<SteamInviteFriendInfo> Friends = new List<SteamInviteFriendInfo> { Friend(2, "Friend") };
            internal SteamInviteLobbyInfo Lobby = new SteamInviteLobbyInfo { Limit = 4, Preparing = true, ValidHost = true, Members = new ulong[] { 1 } };
            internal readonly List<(ulong, ulong)> Sent = new List<(ulong, ulong)>();
            public bool LoggedOn => IsLoggedOn;
            public ulong LocalUser => LocalUserId;
            public bool IsFriend(ulong user) => Friends.Any(f => f.Id == user);
            public IEnumerable<SteamInviteFriendInfo> ReadFriends() => Friends;
            public SteamInviteLobbyInfo ReadLobby(ulong lobby) => ThrowOnRead ? throw new InvalidOperationException() : Lobby;
            public bool Send(ulong lobby, ulong friend)
            { if (ThrowOnSend) throw new InvalidOperationException(); Sent.Add((lobby, friend)); return SendSucceeds; }
        }
    }
}
