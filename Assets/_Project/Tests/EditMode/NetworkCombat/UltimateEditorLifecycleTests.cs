using System;
using System.Collections;
using System.Reflection;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class UltimateEditorLifecycleTests
    {
        private const string BootPath = "Assets/_Project/Scenes/Boot.unity";
        private const string DefinitionPath = "Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Ultimate/MonoBehaviour/UltimateData_Dante.asset";

        [UnityTest]
        public IEnumerator BootLoadedBeforePlay_ChargeAndUltimateSurviveRepeatedEditorSessions()
        {
            // Match manual Play: load the real Boot assets BEFORE Unity reloads the scripting domain.
            // Existing PlayMode fixtures load Boot only after that reload has already happened.
            EditorSceneManager.OpenScene(BootPath);
            Assert.That(AssetDatabase.LoadAssetAtPath<UltimateData>(DefinitionPath), Is.Not.Null);
            yield return new EnterPlayMode();
            yield return UseUltimateInFormalHost();
            yield return new ExitPlayMode();

            yield return new EnterPlayMode();
            yield return UseUltimateInFormalHost();
            yield return new ExitPlayMode();
        }

        private static IEnumerator UseUltimateInFormalHost()
        {
            var manager = UnityEngine.Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            Assert.That(manager, Is.Not.Null);
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp(
                "127.0.0.1", 7964, false, out string error), Is.True, error);
            manager.StartHost();
            yield return WaitFor(() => NetworkClient.localPlayer != null &&
                NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>().HasOwnerBaseline &&
                NetworkClient.localPlayer.GetComponent<PlayerMovement>().IsLocalOwnerBound &&
                NetworkClient.localPlayer.GetComponent<NetworkPlayerUltimate>().OwnerAttack != null,
                "Formal Boot must initialize the Owner before requesting an Ultimate.");
            var player = NetworkClient.localPlayer.GetComponent<PlayerMovement>();
            var ultimate = player.GetComponent<NetworkPlayerUltimate>();
            var source = (DanteUltimateAttack)ultimate.Definition.ultimateAttackWeaponBehaviour;
            var cachedParticles = (ParticleSystem[])typeof(DanteUltimateAttack)
                .GetField("mainParticles", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(source);
            Debug.Log($"[UltimateEditorLifecycle] cached-particles={(cachedParticles == null ? "null" : cachedParticles.Length.ToString())} " +
                $"source-duration={source.SequenceDuration:F6} protection={source.InvulnerabilityDuration:F6}");
            Assert.That(player.RequestDebugUltimateCharge(), Is.True);
            yield return WaitFor(() => ultimate.HasCharge, "The F6 Owner entry must grant one charge through the real Command.");
            int waves = 0;
            ultimate.OwnerAttack.WaveStarted += _ => waves++;
            player.UltimateAction();
            yield return WaitFor(() => ultimate.AcceptedUseCount == 1 && ultimate.OwnerAttack.IsNativeActive,
                "The Q/Y Owner entry must start the charged Ultimate without disconnecting the Host.");
            Assert.That(NetworkClient.isConnected && NetworkClient.ready, Is.True);
            Assert.That(source.SequenceDuration, Is.EqualTo(4.6166666f).Within(0.0001f));
            Assert.That(ultimate.OwnerAttack.SequenceDuration, Is.EqualTo(source.SequenceDuration));
            Assert.That(ultimate.HasCharge, Is.False);
            yield return WaitFor(() => !ultimate.OwnerAttack.IsNativeActive, "The original two-wave sequence must finish.");
            Assert.That(waves, Is.EqualTo(2));
            Assert.That(ultimate.RejectedUseCount, Is.Zero);
            Assert.That(NetworkClient.isConnected && NetworkClient.ready, Is.True);
            manager.StopHost();
            yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "Stop must unload Gameplay.");
        }

        private static IEnumerator WaitFor(Func<bool> condition, string message)
        {
            float deadline = Time.realtimeSinceStartup + 12f;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, message);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (!Application.isPlaying) yield break;
            var manager = UnityEngine.Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            if (manager != null && NetworkServer.active)
            {
                manager.StopHost();
                yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning, "Cleanup must unload Gameplay.");
            }
            yield return new ExitPlayMode();
        }
    }
}
