using System.Collections;
using System.Linq;
using Mirror;
using MonsterSupergroup.Gameplay.Local;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class NetworkCombatSandboxPlayModeTests
    {
        private const string SandboxScenePath =
            "Assets/_Project/Scenes/Development/NetworkCombatSandbox.unity";

        [UnityTest]
        public IEnumerator Host_StartsOwnerPlayerAndOneHundredTwentyCanonicalEnemies()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                SandboxScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(SandboxScenePath, LoadSceneMode.Single);
#endif
            NetworkManager manager = Object.FindFirstObjectByType<NetworkManager>();
            Assert.That(manager, Is.Not.Null);

            try
            {
                manager.StartHost();
                float deadline = Time.realtimeSinceStartup + 8f;
                while ((NetworkClient.localPlayer == null ||
                        Object.FindObjectsByType<NetworkEnemyServerDriver>(
                            FindObjectsSortMode.None).Length < 120) &&
                       Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                NetworkEnemyServerDriver[] enemies =
                    Object.FindObjectsByType<NetworkEnemyServerDriver>(
                        FindObjectsSortMode.None);
                PlayerLoader ownerLoader = NetworkClient.localPlayer != null
                    ? NetworkClient.localPlayer.GetComponent<PlayerLoader>()
                    : null;
                NetworkCombatWorld world = NetworkCombatWorld.Instance;

                Assert.That(NetworkServer.active, Is.True);
                Assert.That(NetworkClient.isConnected, Is.True);
                Assert.That(NetworkClient.localPlayer, Is.Not.Null);
                Assert.That(ownerLoader, Is.Not.Null);
                Assert.That(ownerLoader.IsLoaded, Is.True);
                Assert.That(enemies, Has.Length.EqualTo(120));
                Assert.That(world, Is.Not.Null);
                Assert.That(world.Gateway.Ledger.EntityCount, Is.EqualTo(121));
                Assert.That(
                    enemies.All(enemy =>
                        enemy.GetComponent<NetworkCombatantAdapter>() != null),
                    Is.True);
            }
            finally
            {
                if (manager != null && (NetworkServer.active || NetworkClient.active))
                {
                    manager.StopHost();
                }
            }

            float shutdownDeadline = Time.realtimeSinceStartup + 3f;
            while ((NetworkServer.active || NetworkClient.active) &&
                   Time.realtimeSinceStartup < shutdownDeadline)
            {
                yield return null;
            }

            Assert.That(NetworkServer.active, Is.False);
            Assert.That(NetworkClient.active, Is.False);
        }
    }
}
