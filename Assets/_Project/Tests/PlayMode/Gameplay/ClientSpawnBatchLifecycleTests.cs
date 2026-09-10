using System.Collections;
using System.Reflection;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class ClientSpawnBatchLifecycleTests
    {
        private const uint AssetId = 0xcac40001;

        [SetUp]
        public void SetUp()
        {
            Assert.That(NetworkServer.active || NetworkClient.active, Is.False);
            NetworkClient.Shutdown();
            Invoke("OnObjectSpawnStarted", new ObjectSpawnStartedMessage());
            NetworkClient.RegisterSpawnHandler(AssetId,
                (SpawnMessage _) => new GameObject("Early remote spawn").AddComponent<NetworkIdentity>().gameObject,
                instance => Object.Destroy(instance));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            NetworkClient.Shutdown();
            yield return null;
        }

        [TestCase(1), TestCase(2), Category("SpawnBatchLifecycle")]
        public void EarlySpawnRetainsPayloadAcrossBatchStart(int starts)
        {
            SpawnEarly();
            var identity = NetworkClient.spawned[42];
            Assert.That(identity.netId, Is.Zero, "The payload is deferred until the batch finishes.");
            for (int i = 0; i < starts; i++)
                Invoke("OnObjectSpawnStarted", new ObjectSpawnStartedMessage());
            Invoke("OnObjectSpawnFinished", new ObjectSpawnFinishedMessage());
            Assert.That(identity.netId, Is.EqualTo(42),
                "A legal remote Spawn before the owner's initial batch lost its deferred identity.");
            Assert.That(identity.isClient, Is.True);
            Assert.That(identity.transform.position, Is.EqualTo(new Vector3(3, 4, 0)));
            Assert.That(PendingCount, Is.Zero);
        }

        [Test, Category("SpawnBatchLifecycle")]
        public void ShutdownReleasesUnfinishedSpawnPayloads()
        {
            SpawnEarly();
            Assert.That(PendingCount, Is.EqualTo(1));
            NetworkClient.Shutdown();
            Assert.That(PendingCount, Is.Zero);
            Assert.That(NetworkClient.spawned, Is.Empty);
        }

        private static void SpawnEarly() => Invoke("OnSpawn", new SpawnMessage
        {
            netId = 42, assetId = AssetId, position = new Vector3(3, 4, 0),
            rotation = Quaternion.identity, scale = Vector3.one
        });

        private static int PendingCount => ((IDictionary)typeof(NetworkClient)
            .GetField("pendingSpawns", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Count;

        private static void Invoke(string method, object message) => typeof(NetworkClient)
            .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { message });
    }
}
