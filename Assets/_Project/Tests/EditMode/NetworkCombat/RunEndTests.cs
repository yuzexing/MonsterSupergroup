using System.Linq;
using MonsterSupergroup.GAS;
using NUnit.Framework;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class RunEndTests
    {
        private static void Down(CombatLedger ledger, uint id, uint version = 2)
        {
            var result = ledger.ApplyOwnerFinalReport(id, new PlayerHealthReport {
                PlayerId = id, EntityId = id, EventId = CombatEventId.Compose(1, 1, version).Value,
                Sequence = version, StateVersion = version, Health = 0, MaxHealth = 100, Alive = false });
            Assert.That(result.Accepted, Is.True);
        }
        private static void Add(RunSession session, CombatLedger ledger, int connection, uint avatar)
        {
            session.TryConnect("verified:" + connection, connection, out _, out _);
            session.AttachAvatar(connection, avatar, 1);
            ledger.RegisterEntity(avatar, 100, CombatEntityKind.Player, CombatEntityAuthority.OwnerFinal, avatar);
        }
        [Test]
        public void OnlyConfirmedOnlineWipeEndsOnce_OfflineAliveDoesNotBlock()
        {
            var session = new RunSession(); var ledger = new CombatLedger();
            Add(session, ledger, 0, 10); Add(session, ledger, 1, 11); Add(session, ledger, 2, 12);
            session.BeginRun(); Down(ledger, 10); Down(ledger, 11);
            Assert.That(session.TryEndRun(ledger), Is.False);
            session.Disconnect(2, new PlayerRuntimeCheckpoint { LifeState = RunPlayerLifeState.Active });
            Assert.That(session.TryEndRun(ledger), Is.True);
            Assert.That(session.TryEndRun(ledger), Is.False);
            Assert.That(session.IsRunEnded, Is.True);
        }
        [Test]
        public void LoadingOrUnknownRestoringAvatarNeverCountsAsDowned()
        {
            var session = new RunSession(); var ledger = new CombatLedger();
            Add(session, ledger, 0, 10); Down(ledger, 10);
            Assert.That(session.TryEndRun(ledger), Is.False);
            session.BeginRun(); session.Disconnect(0, null);
            Assert.That(session.TryEndRun(ledger), Is.False);
            session.TryConnect("verified:0", 3, out _, out _);
            Assert.That(session.TryEndRun(ledger), Is.False);
            session.AttachAvatar(3, 15, 2);
            Assert.That(session.TryEndRun(ledger), Is.False);
        }
        [Test]
        public void NextRoundPreservesIdentity_ClearsOfflineCheckpointAndRunState()
        {
            var session = new RunSession(); var ledger = new CombatLedger();
            Add(session, ledger, 0, 10); Add(session, ledger, 1, 11); session.BeginRun();
            session.Disconnect(1, new PlayerRuntimeCheckpoint { LifeState = RunPlayerLifeState.Downed });
            var host = session.Participants[0]; ulong id = host.Id; string run = session.RunId;
            Down(ledger, 10); Assert.That(session.TryEndRun(ledger), Is.True); session.BeginNextRound();
            Assert.That(session.RunId, Is.Not.EqualTo(run)); Assert.That(session.Round, Is.EqualTo(2));
            Assert.That(session.IsRunEnded || session.IsRunStarted || session.IsRosterLocked, Is.False);
            Assert.That(host.Id, Is.EqualTo(id)); Assert.That(host.AvatarId, Is.Zero);
            Assert.That(session.Participants.All(p => p.Checkpoint == null), Is.True);
            session.TryConnect("new", 4, out var newcomer, out _);
            Assert.That(newcomer.Id, Is.GreaterThan(2));
        }
        [TestCase(false)] [TestCase(true)]
        public void HostChoosesOnce_RestoredPartyKeepsLoadoutButClearsReady(bool restart)
        {
            var room = new PreparationRoom("old", "map", true, new uint[] { 2, 402 });
            room.Join(1, "Host", true, out _); room.Join(2, "Client", false, out _);
            room.SetLoadout(2, 1, 402, out _);
            room.SetReady(1, 1, true, out _); room.SetReady(2, 2, true, out _);
            Assert.That(room.Start(1, room.Revision, out _), Is.True);
            room.MarkGameplayReady(1); room.MarkGameplayReady(2); room.BeginCombat(); room.EndRun();
            Assert.That(room.RequestEndAction(2, RunEndAction.Restart, out _), Is.False);
            var action = restart ? RunEndAction.Restart : RunEndAction.ReturnToRoom;
            Assert.That(room.RequestEndAction(1, action, out _), Is.True);
            Assert.That(room.RequestEndAction(1, RunEndAction.Restart, out _), Is.False);
            Assert.That(room.SetReady(1, 1, true, out _), Is.False);
            var next = new PreparationRoom("new", "map", true, new uint[] { 2, 402 }, 2);
            next.RestoreParty(room.Launch.Members, restart);
            Assert.That(next.Snapshot(1).Members.Select(m => m.WeaponId), Is.EqualTo(new uint[] { 2, 402 }));
            Assert.That(next.Snapshot(1).Members.All(m => !m.Ready && !m.GameplayReady), Is.True);
            Assert.That(next.Phase, Is.EqualTo(restart ? PreparationPhase.Loading : PreparationPhase.Preparing));
            Assert.That(next.AllGameplayReady, Is.False);
        }
        [Test]
        public void EndedGatewayRejectsLateReportsAndTicks_ResetKeepsGatewayObject()
        {
            var gateway = new ServerCombatGateway();
            gateway.Ledger.RegisterEntity(10, 100, CombatEntityKind.Player, CombatEntityAuthority.OwnerFinal, 10);
            gateway.StopCombat();
            var batch = gateway.ProcessBatch(10, default, 1);
            Assert.That(batch.Entities, Is.Null); Assert.That(gateway.Advance(2).Entities, Is.Null);
            var original = gateway; gateway.ResetForNextRun();
            Assert.That(gateway, Is.SameAs(original)); Assert.That(gateway.Ledger.EntityCount, Is.Zero);
            Assert.That(gateway.CombatStopped, Is.False);
        }
        [Test]
        public void EndedAndTransitioningSteamLobbyStayOpenForExistingConnections()
        {
            Assert.That(SteamLobbyMetadata.IsActiveSession(SteamLobbyMetadata.GameOverState), Is.True);
            Assert.That(SteamLobbyMetadata.IsActiveSession(SteamLobbyMetadata.TransitioningState), Is.True);
        }
    }
}
