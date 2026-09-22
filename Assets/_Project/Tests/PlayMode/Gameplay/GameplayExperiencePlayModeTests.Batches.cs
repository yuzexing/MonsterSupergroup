using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class GameplayExperiencePlayModeTests
    {
        private const BindingFlags PickupPrivate = BindingFlags.Instance | BindingFlags.NonPublic;
        private NetworkExperienceGem SmallXpDrop(Vector2 position, float amount = .01f) =>
            (NetworkExperienceGem)typeof(NetworkExperienceWorld).GetMethod("SpawnPickup", PickupPrivate)
                .Invoke(World, new object[] { PickupEffect.Experience, amount, position, Owner.gameObject.scene });
        private static void ScanPickups(NetworkExperienceCollector collector, bool advanceInterval = true)
        {
            if (advanceInterval) typeof(NetworkExperienceCollector).GetField("nextRequest", PickupPrivate).SetValue(collector, 0f);
            typeof(NetworkExperienceCollector).GetMethod("Update", PickupPrivate).Invoke(collector, null);
        }
        private static void HoldPickupScan(NetworkExperienceCollector collector) =>
            typeof(NetworkExperienceCollector).GetField("nextRequest", PickupPrivate).SetValue(collector, float.MaxValue);
        private static void SendPickupBatch(NetworkExperienceCollector collector, string run, params ulong[] drops) =>
            typeof(NetworkExperienceCollector).GetMethod("CmdCollectBatch", PickupPrivate)
                .Invoke(collector, new object[] { run, drops, null });

        [UnityTest]
        public IEnumerator PickupBatches_SkipPendingAcrossScans_AndCollectBeyondOneBatchExactlyOnce()
        {
            yield return StartHost();
            int count = NetworkExperienceCollector.MaximumCollectionsPerBatch * 2 + 6;
            for (int i = 0; i < count; i++) SmallXpDrop(Owner.transform.position);
            var far = SmallXpDrop(Owner.transform.position + Vector3.right * 100);
            yield return WaitFor(() => NetworkExperienceGem.ClientGems.Count == count + 1, "client pickup spawn baseline");
            var collector = Owner.GetComponent<NetworkExperienceCollector>();
            collector.enabled = true;
            int before = collector.SubmittedCollectionBatchCount;
            // Do not pump Mirror between scans: every request remains unconfirmed.
            ScanPickups(collector);
            Assert.That(collector.PendingCollectionCount, Is.EqualTo(32));
            ScanPickups(collector, false);
            Assert.That(collector.SubmittedCollectionBatchCount - before, Is.EqualTo(1), "Keep the scan interval.");
            ScanPickups(collector);
            Assert.That(collector.PendingCollectionCount, Is.EqualTo(64), "Pending drops must not monopolize the next batch.");
            ScanPickups(collector);
            Assert.That(collector.PendingCollectionCount, Is.EqualTo(count));
            ScanPickups(collector);
            Assert.That(collector.SubmittedCollectionBatchCount - before, Is.EqualTo(3), "No repeat requests while awaiting confirmation.");
            float expected = count * .01f * Owner.GetComponent<PlayerMovement>().PlayerStats.currentStats.xpModifier;
            yield return WaitFor(() => World.UnclaimedCount == 1 && collector.PendingCollectionCount == 0, "all pickup batches confirmed");
            Assert.That(Progression.Experience, Is.EqualTo(expected).Within(.0001f));
            Assert.That(World.Unclaimed.Single(), Is.SameAs(far), "Out-of-range drops stay on the ground.");
            Assert.That(collector.SubmittedCollectionBatchCount - before, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator PickupBatches_ServerRejectionReleasesPending_AndAllowsLaterRetry()
        {
            yield return StartHost();
            var gem = SmallXpDrop(Owner.transform.position);
            yield return WaitFor(() => NetworkExperienceGem.ClientGems.Contains(gem), "client pickup spawn");
            var collector = Owner.GetComponent<NetworkExperienceCollector>(); collector.enabled = true;
            ScanPickups(collector);
            int submitted = collector.SubmittedCollectionBatchCount;
            collector.RequestCollection(World.RunId, gem.DropId);
            Assert.That(collector.SubmittedCollectionBatchCount, Is.EqualTo(submitted));
            gem.transform.position += Vector3.right * 100; // Leave range while the command is in transit.
            HoldPickupScan(collector);
            yield return WaitFor(() => collector.PendingCollectionCount == 0, "server rejection acknowledgement");
            Assert.That(World.UnclaimedCount, Is.EqualTo(1));
            Assert.That(Progression.Experience, Is.Zero);
            gem.transform.position = Owner.transform.position;
            ScanPickups(collector);
            yield return WaitFor(() => World.UnclaimedCount == 0 && collector.PendingCollectionCount == 0, "retry after rejection");
            Assert.That(collector.SubmittedCollectionBatchCount, Is.EqualTo(submitted + 1));
            Assert.That(Progression.Experience, Is.EqualTo(.02f).Within(.00001f));
        }

        [UnityTest]
        public IEnumerator PickupBatches_LevelUpStopsFurtherAwards_AndReleasesUncollectedRequests()
        {
            yield return StartHost();
            for (int i = 0; i < 10; i++) SmallXpDrop(Owner.transform.position, 2);
            yield return WaitFor(() => NetworkExperienceGem.ClientGems.Count == 10, "client pickup spawn baseline");
            var collector = Owner.GetComponent<NetworkExperienceCollector>(); collector.enabled = true;
            ScanPickups(collector);
            Assert.That(collector.PendingCollectionCount, Is.EqualTo(10));
            yield return WaitFor(() => Progression.IsSelecting && collector.PendingCollectionCount == 0, "selection and remaining pickup rejections");
            Assert.That(Progression.Level, Is.EqualTo(2));
            Assert.That(Progression.Experience, Is.EqualTo(1), "Five drops award 20 XP, crossing the 19 XP threshold.");
            Assert.That(World.UnclaimedCount, Is.EqualTo(5));
            Assert.That(World.Unclaimed.All(gem => !gem.Claimed), Is.True);
        }

        [UnityTest]
        public IEnumerator PickupBatches_ServerChecksSizeRunDistanceAndDuplicatesPerDrop()
        {
            yield return StartHost();
            var oversized = SmallXpDrop(Owner.transform.position);
            var stale = SmallXpDrop(Owner.transform.position);
            var valid = SmallXpDrop(Owner.transform.position);
            var far = SmallXpDrop(Owner.transform.position + Vector3.right * 100);
            var collector = Owner.GetComponent<NetworkExperienceCollector>(); collector.enabled = true;
            // Initialize the run before blocking automatic scans; spawn messages are still queued.
            ScanPickups(collector);
            HoldPickupScan(collector);
            SendPickupBatch(collector, World.RunId, Enumerable.Repeat(oversized.DropId, 33).ToArray());
            SendPickupBatch(collector, "previous-run", stale.DropId);
            SendPickupBatch(collector, World.RunId, valid.DropId, valid.DropId, far.DropId);
            // Reliable commands above are handled in order; the last successful grant is the barrier.
            yield return WaitFor(() => Progression.Experience > 0, "batch validation");
            Assert.That(Progression.Experience, Is.EqualTo(.02f).Within(.00001f));
            Assert.That(World.UnclaimedCount, Is.EqualTo(3));
            Assert.That(World.Unclaimed, Has.Member(oversized).And.Member(stale).And.Member(far));
        }

        private sealed class PickupFixtureConnection : NetworkConnectionToClient
        {
            public PickupFixtureConnection(int id) : base(id) { isAuthenticated = true; }
            protected override void SendToTransport(ArraySegment<byte> data, int channelId = Channels.Reliable) { }
            public override void Disconnect() { isReady = false; }
        }

        [UnityTest]
        public IEnumerator PickupBatches_CompetingPlayersCannotReceiveTheSameDrop()
        {
            yield return StartHost();
            var connection = new PickupFixtureConnection(901);
            Assert.That(manager.Session.TryConnect("pickup-batch-competitor", connection.connectionId, out var member, out string error), Is.True, error);
            Assert.That(NetworkServer.AddConnection(connection), Is.True);
            var other = Object.Instantiate(manager.playerPrefab, Owner.transform.position, Quaternion.identity);
            SceneManager.MoveGameObjectToScene(other, Owner.gameObject.scene);
            typeof(NetworkRunParticipant).GetMethod("Prepare", PickupPrivate).Invoke(other.GetComponent<NetworkRunParticipant>(), new object[] { World.RunId, member });
            Assert.That(NetworkServer.AddPlayerForConnection(connection, other), Is.True);
            var identity = other.GetComponent<NetworkIdentity>();
            manager.Session.AttachAvatar(connection.connectionId, identity.netId, other.GetComponent<MirrorNetworkCombatBridge>().ConnectionEpoch);
            other.GetComponent<PlayerBuildRuntime>().SetWeaponExecutionEnabled(false);
            var contested = SmallXpDrop(Owner.transform.position);
            SmallXpDrop(Owner.transform.position);
            yield return WaitFor(() => NetworkExperienceGem.ClientGems.Count == 2, "contested pickup spawn baseline");
            var collector = Owner.GetComponent<NetworkExperienceCollector>(); collector.enabled = true;
            ScanPickups(collector); // Both requests are in transit when the other player wins one.
            Assert.That(collector.PendingCollectionCount, Is.EqualTo(2));
            var otherCollector = other.GetComponent<NetworkExperienceCollector>();
            var receive = typeof(NetworkExperienceCollector).GetMethods(PickupPrivate).Single(m => m.Name.StartsWith("UserCode_CmdCollectBatch"));
            // Cross the server command boundary with the authenticated second connection.
            receive.Invoke(otherCollector, new object[] { World.RunId, new[] { contested.DropId, contested.DropId }, connection });
            yield return WaitFor(() => World.UnclaimedCount == 0 && collector.PendingCollectionCount == 0, "contested batch acknowledgements");
            Assert.That(Progression.Experience, Is.EqualTo(.02f).Within(.00001f));
            Assert.That(other.GetComponent<NetworkModifierSelection>().Experience, Is.EqualTo(.02f).Within(.00001f));
        }

        [UnityTest]
        public IEnumerator PickupBatches_NotReadyDoesNotCreateUnsendablePendingRequests()
        {
            yield return StartHost();
            var gem = SmallXpDrop(Owner.transform.position);
            yield return WaitFor(() => NetworkExperienceGem.ClientGems.Contains(gem), "client pickup spawn");
            var collector = Owner.GetComponent<NetworkExperienceCollector>(); collector.enabled = true;
            int before = collector.SubmittedCollectionBatchCount;
            NetworkClient.ready = false;
            try
            {
                ScanPickups(collector);
                collector.RequestCollection(World.RunId, gem.DropId);
                Assert.That(collector.PendingCollectionCount, Is.Zero);
                Assert.That(collector.SubmittedCollectionBatchCount, Is.EqualTo(before));
            }
            finally { NetworkClient.ready = true; }
            ScanPickups(collector);
            yield return WaitFor(() => World.UnclaimedCount == 0 && collector.PendingCollectionCount == 0, "pickup after client readiness");
        }

        [UnityTest]
        public IEnumerator PickupBatches_CancelledHealthReservationDoesNotLeavePendingForever()
        {
            yield return StartHost();
            GameplayWavePlayModeTests.SetCanonicalHealth(Owner.netId, 200);
            var gem = HealthDrop();
            yield return WaitFor(() => NetworkExperienceGem.ClientGems.Contains(gem), "client health pickup spawn");
            var collector = Owner.GetComponent<NetworkExperienceCollector>(); collector.enabled = true;
            ScanPickups(collector);
            Assert.That(collector.PendingCollectionCount, Is.EqualTo(1));
            int before = collector.SubmittedCollectionBatchCount;
            // Reserve and return the item before a client scan can observe Claimed=true.
            Assert.That(Collect(gem, out string reason), Is.True, reason);
            Assert.That(World.RejectHealthGrant(Owner.connectionToClient, World.RunId, gem.DropId, gem.ClaimVersion), Is.True);
            Assert.That(gem.Claimed, Is.False);
            ScanPickups(collector);
            Assert.That(collector.SubmittedCollectionBatchCount, Is.EqualTo(before + 1), "The changed claim version releases the old pending reservation.");
            yield return WaitFor(() => World.HealthCount == 0 && collector.PendingCollectionCount == 0, "returned health pickup");
            Assert.That(Owner.GetComponent<CombatantBehaviour>().CurrentHealth, Is.EqualTo(400));
        }
    }
}
