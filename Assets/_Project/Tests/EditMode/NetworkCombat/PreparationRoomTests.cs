using System.Linq;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class PreparationRoomTests
    {
        private PreparationRoom Room()
        {
            var room = new PreparationRoom("run", "Gameplay", true, new uint[] { 1, 2, 3, 6, 8, 402 });
            Assert.That(room.Join(1, "Host", true, out _), Is.True);
            return room;
        }
        [Test] public void FourSeats_RejectFifth_LeavingReleasesSeatAndReady()
        {
            var room = Room();
            for (ulong i = 2; i <= 4; i++) Assert.That(room.Join(i, "Player", false, out _), Is.True);
            Assert.That(room.Join(5, "Fifth", false, out string error), Is.False); StringAssert.Contains("房间已满", error);
            room.SetReady(2, 1, true, out _); room.Leave(2);
            Assert.That(room.Join(2, "Returning", false, out _), Is.True);
            var player = room.Snapshot(2).Members.Single(m => m.ParticipantId == 2);
            Assert.That(player.Seat, Is.EqualTo(1)); Assert.That(player.Ready, Is.False);
        }
        [Test] public void MultiplayerRequiresAllReady_OnlyHostStarts_DuplicateStartsDoNotRelock()
        {
            var room = Room(); room.Join(2, "Client", false, out _);
            Assert.That(room.Start(1, room.Revision, out _), Is.False);
            room.SetReady(1, 1, true, out _); room.SetReady(2, 1, true, out _);
            Assert.That(room.Start(2, room.Revision, out _), Is.False);
            Assert.That(room.Start(1, room.Revision, out _), Is.True);
            var launch = room.Launch; uint revision = room.Revision;
            Assert.That(room.Start(1, 0, out _), Is.True);
            Assert.That(room.Launch, Is.SameAs(launch)); Assert.That(room.Revision, Is.EqualTo(revision));
            Assert.That(room.SetLoadout(1, 1, 6, out _), Is.False);
            Assert.That(room.Join(3, "Late", false, out _), Is.False);
        }
        [Test] public void ChangingWeaponInvalidatesOnlyOwnReady_StaleReadyRejected()
        {
            var room = Room(); room.Join(2, "Client", false, out _);
            room.SetReady(1, 1, true, out _); room.SetReady(2, 1, true, out _);
            Assert.That(room.SetLoadout(2, 1, 402, out _), Is.True);
            Assert.That(room.Snapshot(1).Members[0].Ready, Is.True);
            Assert.That(room.Snapshot(1).Members[1].Ready, Is.False);
            Assert.That(room.SetReady(2, 1, true, out _), Is.False);
            Assert.That(room.SetReady(2, 2, true, out _), Is.True);
            Assert.That(room.SetLoadout(2, 9, 2, out _), Is.False);
            Assert.That(room.SetLoadout(2, 1, 999, out _), Is.False);
            Assert.That(room.SetLoadout(77, 1, 2, out _), Is.False);
        }
        [TestCase(1u)] [TestCase(2u)] [TestCase(3u)] [TestCase(6u)] [TestCase(8u)] [TestCase(402u)]
        public void SoloCanStartWithoutReady_SealedSelectionSurvives(uint weapon)
        {
            var room = Room(); Assert.That(room.SetLoadout(1, 1, weapon, out _), Is.True);
            Assert.That(room.Start(1, room.Revision, out _), Is.True);
            Assert.That(room.Launch.WeaponFor(1), Is.EqualTo(weapon));
            Assert.That(room.AllGameplayReady, Is.False);
            room.MarkGameplayReady(1); room.BeginCombat();
            Assert.That(room.Phase, Is.EqualTo(PreparationPhase.InGame));
        }
        [Test] public void SealedRosterIsSeparateFromStarted_ExcludesEarlierDepartures()
        {
            var session = new RunSession();
            session.TryConnect("host", 0, out var host, out _);
            session.TryConnect("gone", 1, out var gone, out _); session.Disconnect(1, null);
            session.SealRoster(new[] { host.Id });
            Assert.That(session.Participants.Select(p => p.Id), Is.EquivalentTo(new[] { host.Id }), "Earlier lobby departures are not run participants.");
            Assert.That(session.IsRosterLocked, Is.True); Assert.That(session.IsRunStarted, Is.False);
            Assert.That(session.TryConnect("gone", 2, out _, out _), Is.False);
            session.BeginRun(); Assert.That(session.IsRunStarted, Is.True);
            session.Disconnect(0, null); session.TryConnect("host", 3, out var restored, out _);
            Assert.That(restored.Id, Is.EqualTo(host.Id));
        }
        [Test] public void SteamPhasesRemainReachableForReconnect_StartupInviteIsParsed()
        {
            Assert.That(SteamLobbyMetadata.IsActiveSession(SteamLobbyMetadata.LoadingState), Is.True);
            Assert.That(SteamLobbyMetadata.IsActiveSession(SteamLobbyMetadata.InGameState), Is.True);
            Assert.That(SteamLobbyMetadata.IsActiveSession(SteamLobbyMetadata.ClosedState), Is.False);
            Assert.That(SteamLobbyService.ReadStartupInvitation(new[] { "game.exe", "+connect_lobby", "109775242349881186" }), Is.EqualTo(109775242349881186ul));
            Assert.That(SteamLobbyService.ReadStartupInvitation(new[] { "--connect-lobby=109775242349881187" }), Is.EqualTo(109775242349881187ul));
        }
    }
}
