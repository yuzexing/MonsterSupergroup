#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.CameraFX;
using AstralShift.HellMaiden.Player;
using Com.LuisPedroFonseca.ProCamera2D;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.Options;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class GameplayCameraTests
    {
        private BootGameplayNetworkManager manager;
        private GameObject[] bootRoots;
        private GameplayCameraRig view;
        private PlayerMovement player;
        private ProCamera2D rig;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            const string boot = "Assets/_Project/Scenes/Boot.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(boot, new LoadSceneParameters(LoadSceneMode.Single));
            bootRoots = BootSceneFixtureObjects.Capture(boot);
            SceneManager.sceneLoaded += PrepareGameplay;
            manager = Object.FindFirstObjectByType<BootGameplayNetworkManager>();
            var gate = new GameObject("Camera fixture weapon gate");
            gate.transform.SetParent(manager.transform);
            gate.AddComponent<WeaponAttackAdmissionFixtureGate>();
            Assert.That(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7904, false, out var error), Is.True, error);
            manager.StartHost();
            yield return WaitFor(() => NetworkClient.localPlayer != null && Object.FindFirstObjectByType<GameplayCameraRig>()?.BoundPlayer != null);
            view = Object.FindFirstObjectByType<GameplayCameraRig>();
            rig = view.GetComponent<ProCamera2D>();
            player = NetworkClient.localPlayer.GetComponent<PlayerMovement>();
        }

        [UnityTest]
        public IEnumerator OptionsDisableOwnerDamageAndExplicitShakeAndRestoreImmediately()
        {
            var options = GameOptionsService.EnsureInitialized();
            bool wasEnabled = options.Current.ScreenShake;
            bool saved = PlayerPrefs.HasKey(GameOptionsService.PreferenceKey);
            string previous = PlayerPrefs.GetString(GameOptionsService.PreferenceKey);
            int calls = 0;
            view.ShakePlayed += _ => calls++;
            try
            {
                options.SetScreenShake(true);
                view.PlayShake(player, 2); yield return null;
                Assert.That(calls, Is.EqualTo(1));
                options.SetScreenShake(false);
                Assert.That(view.transform.parent.localPosition, Is.EqualTo(Vector3.zero));
                view.PlayShake(player, 2);
                player.GetComponent<CombatantBehaviour>().ReceiveDamage(new DamageInfo(1, 1, false));
                yield return null;
                Assert.That(calls, Is.EqualTo(1), "Turning off shake must gate damage and explicit presets.");
                options.SetScreenShake(true); view.PlayShake(player, 2);
                Assert.That(calls, Is.EqualTo(2));
            }
            finally
            {
                options.SetScreenShake(wasEnabled);
                if (saved) PlayerPrefs.SetString(GameOptionsService.PreferenceKey, previous);
                else PlayerPrefs.DeleteKey(GameOptionsService.PreferenceKey);
                PlayerPrefs.Save();
            }
        }

        [UnityTest]
        public IEnumerator AudioListenerAtMapEdges_PreservesOwnerRelativeDistance()
        {
            var ground = GameObject.Find("Ground").GetComponent<SpriteRenderer>().bounds;
            foreach (Vector2 position in new[] { (Vector2)ground.center,
                         new Vector2(ground.max.x - 1, ground.center.y), new Vector2(ground.min.x + 1, ground.center.y),
                         new Vector2(ground.center.x, ground.max.y - 1), new Vector2(ground.center.x, ground.min.y + 1),
                         new Vector2(ground.max.x - 1, ground.max.y - 1), new Vector2(ground.min.x + 1, ground.max.y - 1),
                         new Vector2(ground.max.x - 1, ground.min.y + 1), new Vector2(ground.min.x + 1, ground.min.y + 1) })
            {
                MoveOwner(position);
                rig.Reset();
                yield return null; yield return null;
                Vector3 source = player.transform.position + Vector3.right;
                float distance = Vector3.Distance(source, view.LocalAudioListener.transform.position);
                Debug.Log($"AUDIO-EDGE owner={player.transform.position} camera={view.transform.position} listener={view.LocalAudioListener.transform.position} distance={distance:F6}");
                if (position != (Vector2)ground.center)
                    Assert.That(Vector2.Distance(player.transform.position, view.transform.position), Is.GreaterThan(5), "Must exercise a constrained camera, not a centered one.");
                Assert.That(distance, Is.EqualTo(Mathf.Sqrt(101)).Within(.001f), "Local sounds must not fade as the camera reaches the map boundary.");
                Assert.That(Vector3.Distance(source + Vector3.right * 12, view.LocalAudioListener.transform.position), Is.GreaterThan(distance + 5), "Distant world sounds must retain distance attenuation.");
            }
        }

        [UnityTest]
        public IEnumerator AudioListenerTracksOwner_IndependentOfBarrierAndCameraShake()
        {
            view.SetReferenceTrapFraming(new Vector2(10,5),20,1);
            yield return null; yield return null;
            var audio=view.LocalAudioListener.transform.position;
            Assert.That(audio.x,Is.EqualTo(player.transform.position.x).Within(.001));
            Assert.That(audio.y,Is.EqualTo(player.transform.position.y).Within(.001));
            Assert.That(audio.z,Is.EqualTo(player.transform.position.z-10).Within(.001));
            Assert.That(view.LocalAudioListener.gameObject.scene, Is.EqualTo(view.gameObject.scene));
            view.transform.parent.position += new Vector3(2, 3, 0);
            view.transform.parent.rotation = Quaternion.Euler(0, 0, 20);
            Assert.That(view.LocalAudioListener.transform.position, Is.EqualTo(audio), "Shake hierarchy cannot move the listener between updates.");
            Assert.That(view.LocalAudioListener.transform.rotation, Is.EqualTo(Quaternion.identity));
            view.transform.parent.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            view.ClearReferenceTrapFraming();
            yield return null;
            Assert.That(FMODUnity.StudioListener.ListenerCount,Is.EqualTo(1));
            Assert.That(view.LocalAudioListener.transform.position.z,Is.EqualTo(player.transform.position.z-10).Within(.001));
        }

        [UnityTest]
        public IEnumerator FormalCamera_FollowsOneOwner_UsesGroundEdges_AndConvertsAim()
        {
            Assert.That(view.BoundPlayer, Is.SameAs(player));
            Assert.That(view.gameObject.scene.path, Is.EqualTo(manager.GameplayScene));
            Assert.That(view.transform.parent.gameObject.scene, Is.EqualTo(view.gameObject.scene), "Shake container must unload with Gameplay.");
            Assert.That(Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Count(c => c.enabled && c.CompareTag("MainCamera")), Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(c => c.enabled), Is.EqualTo(1));
            Assert.That(FMODUnity.StudioListener.ListenerCount, Is.EqualTo(1));
            Assert.That(view.LocalAudioListener.isActiveAndEnabled, Is.True);
            Assert.That(player.GetComponent<FMODUnity.StudioListener>(), Is.Null);
            Assert.That(view.LocalAudioListener.transform.position.z, Is.EqualTo(player.transform.position.z - 10).Within(.001));
            Assert.That(rig.HorizontalFollowSmoothness, Is.EqualTo(.15f));
            Assert.That(rig.VerticalFollowSmoothness, Is.EqualTo(.15f));
            Assert.That(view.GetComponent<ProCamera2DShake>().ShakePresets.Select(p => p.name),
                Is.EqualTo(new[] { "SmallExplosion", "GunShot", "LargeExplosion", "PlayerHit" }));
            var zoom = view.GetComponent<ProCamera2DZoomToFitTargets>();
            Assert.That(zoom.enabled, Is.False);
            Assert.That(zoom.MaxZoomOutAmount, Is.EqualTo(12));
            var ground = GameObject.Find("Ground").GetComponent<SpriteRenderer>().bounds;
            var boundaries = view.GetComponent<ProCamera2DNumericBoundaries>();
            Assert.That(ground.size.x, Is.EqualTo(119.3386f).Within(.001));
            Assert.That(ground.size.y, Is.EqualTo(67.128f).Within(.001));
            Assert.That(boundaries.LeftBoundary, Is.EqualTo(ground.min.x));
            Assert.That(boundaries.RightBoundary, Is.EqualTo(ground.max.x));
            Assert.That(boundaries.BottomBoundary, Is.EqualTo(ground.min.y));
            Assert.That(boundaries.TopBoundary, Is.EqualTo(ground.max.y));
            foreach (Vector2 position in new[] { new Vector2(12, 8), new Vector2(-60, -60),
                         new Vector2(-60, 60), new Vector2(60, 60), new Vector2(60, -60) })
            {
                MoveOwner(position);
                yield return new WaitForSeconds(1.5f);
                Assert.That(rig.CameraTargets.Count, Is.EqualTo(1));
                Assert.That(rig.CameraTargets[0].TargetTransform, Is.EqualTo(player.transform));
                Assert.That(view.GameCamera.orthographic, Is.False);
                Assert.That(view.GameCamera.fieldOfView, Is.EqualTo(80).Within(.001));
                Assert.That(view.transform.position.z, Is.EqualTo(-10).Within(.001));
                Bounds visible = GameplayCameraGeometry.ViewBounds(view.GameCamera);
                Assert.That(visible.min.x, Is.GreaterThanOrEqualTo(ground.min.x - .02f));
                Assert.That(visible.max.x, Is.LessThanOrEqualTo(ground.max.x + .02f));
                Assert.That(visible.min.y, Is.GreaterThanOrEqualTo(ground.min.y - .02f));
                Assert.That(visible.max.y, Is.LessThanOrEqualTo(ground.max.y + .02f));
            }
            MoveOwner(Vector2.zero);
            yield return new WaitForSeconds(1.5f);
            typeof(PlayerMovement).GetField("_autoAim", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, false);
            Vector3 aim = player.transform.position + new Vector3(3, 2, 0);
            player.SetAimPosition(view.GameCamera.WorldToScreenPoint(aim));
            Assert.That(Vector2.Dot(player.attackDirection, new Vector2(3, 2).normalized), Is.GreaterThan(.999f));
        }

        [UnityTest]
        public IEnumerator OwnerReleaseAndLateCamera_RebindWithoutDuplicatesOrResidualShake()
        {
            var binding = (LocalPlayerInputBinding)typeof(NetworkPlayerBootstrap).GetField("ownerInput",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player.GetComponent<NetworkPlayerBootstrap>());
            int calls = 0;
            view.ShakePlayed += _ => calls++;
            for (int i = 0; i < 10; i++) binding.Refresh();
            binding.PlayCameraShake(2);
            Assert.That(calls, Is.EqualTo(1));
            yield return null;
            binding.Dispose();
            Assert.That(FMODUnity.StudioListener.ListenerCount, Is.Zero);
            Assert.That(rig.CameraTargets, Is.Empty);
            Assert.That(view.BoundPlayer, Is.Null);
            Assert.That(view.transform.parent.localPosition, Is.EqualTo(Vector3.zero));
            binding.PlayCameraShake(2);
            Assert.That(calls, Is.EqualTo(1));
            view.enabled = false;
            binding.Bind(player); // Owner before an available camera.
            Assert.That(FMODUnity.StudioListener.ListenerCount, Is.Zero, "Wait for the local gameplay camera.");
            Assert.That(rig.CameraTargets, Is.Empty);
            view.enabled = true;
            binding.Refresh();
            Assert.That(view.BoundPlayer, Is.SameAs(player));
            Assert.That(FMODUnity.StudioListener.ListenerCount, Is.EqualTo(1));
            Assert.That(rig.CameraTargets.Count, Is.EqualTo(1));
            binding.PlayCameraShake(2);
            Assert.That(calls, Is.EqualTo(2));
            Object.Destroy(player.gameObject);
            yield return null;
            binding.Refresh(); // Destroyed transforms must not reach the plugin's RemoveCameraTarget.
            Assert.That(rig.CameraTargets, Is.Empty);
        }

        [UnityTest]
        public IEnumerator AdmittedUltimate_UsesOriginalTwoAnimationEvents_OnceOnHost()
        {
            var events = new List<int>();
            view.ShakePlayed += events.Add;
            player.GetComponent<CombatantBehaviour>().ReceiveDamage(new DamageInfo(1, 1, false));
            Assert.That(events, Is.EqualTo(new[] { 3 }), "Approved local damage binding must use PlayerHit once.");
            events.Clear();
            yield return new WaitForSeconds(.2f);
            var ultimate = player.GetComponent<NetworkPlayerUltimate>();
            yield return WaitFor(() => ultimate.OwnerAttack != null);
            Assert.That(ultimate.ServerGrantCharge(), Is.True);
            yield return WaitFor(() => ultimate.HasCharge);
            Assert.That(ultimate.RequestUse(), Is.True);
            yield return WaitFor(() => events.Count >= 2);
            yield return new WaitForSeconds(3);
            Assert.That(events, Is.EqualTo(new[] { 2, 2 }));
            Assert.That(ultimate.AcceptedUseCount, Is.EqualTo(1));
            Assert.That(rig.CameraTargets.Count, Is.EqualTo(1));
            Assert.That(view.transform.parent.localPosition.magnitude, Is.LessThan(.01f));
        }

        [UnityTest]
        public IEnumerator LocalDamage_IgnoresCanonicalEchoAndInvulnerability_AndRebindsOneSubscription()
        {
            var combatant = player.GetComponent<CombatantBehaviour>();
            int calls = 0;
            view.ShakePlayed += index => { Assert.That(index, Is.EqualTo(3)); calls++; };
            player.DecreaseHealth(1);
            Assert.That(calls, Is.EqualTo(1));
            combatant.ApplyCanonicalHealth(combatant.CurrentHealth, combatant.MaxHealth, combatant.StateVersion);
            combatant.RestoreHealth(1);
            combatant.SetUltimateInvulnerable(true);
            player.DecreaseHealth(1);
            combatant.SetUltimateInvulnerable(false);
            Assert.That(calls, Is.EqualTo(1), "Healing, reconciliation and rejected damage cannot shake.");
            view.enabled = false;
            player.DecreaseHealth(1);
            Assert.That(calls, Is.EqualTo(1));
            view.enabled = true;
            yield return null;
            player.DecreaseHealth(1);
            Assert.That(calls, Is.EqualTo(2), "Rebind must restore exactly one damage subscription.");
        }

        [UnityTest]
        public IEnumerator HostRestart_DestroysOldViewAndShakeContainer_ThenBindsNewOwner()
        {
            Transform oldContainer = view.transform.parent;
            manager.StopHost();
            yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning);
            Assert.That(view == null && oldContainer == null, Is.True);
            manager.StartHost();
            yield return WaitFor(() => NetworkClient.localPlayer != null && Object.FindFirstObjectByType<GameplayCameraRig>()?.BoundPlayer != null);
            var next = Object.FindFirstObjectByType<GameplayCameraRig>();
            Assert.That(next.BoundPlayer, Is.EqualTo(NetworkClient.localPlayer.GetComponent<PlayerMovement>()));
            Assert.That(Object.FindObjectsByType<GameplayCameraRig>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Axeldor_DeathHoldsAndCanonicalLifeRestorationReversesClip()
        {
            var visual=player.GetComponentInChildren<NordicPlayerAnimator>();
            var health=player.GetComponent<CombatantBehaviour>(); int before=health.CurrentHealth;
            // The initial owner health report (version 2) must reach the canonical receive path
            // before this presentation-only edit. Otherwise its queued alive baseline replaces
            // the synthetic death, depending on test order/frame timing.
            yield return WaitFor(() => health.StateVersion > 1 &&
                player.GetComponent<MirrorNetworkCombatBridge>().Collector.PendingPlayerHealthReportCount == 0);
            player.enabled=false;
            NordicGameplayProcessProbe.ApplyPresentationHealth(health,0);
            yield return new WaitForSeconds(1.2f);
            var state=visual.GetComponent<Animancer.AnimancerComponent>().States.Current;
            Assert.That(visual.Motion,Is.EqualTo(NordicMotion.Dead), "health="+health.CurrentHealth+" version="+health.StateVersion+" initialized="+player.IsRuntimeInitialized+" scale="+Time.timeScale);
            Assert.That(state.Time,Is.EqualTo(state.Length).Within(.001f));
            Assert.That(state.Speed,Is.Zero);
            NordicGameplayProcessProbe.ApplyPresentationHealth(health,before);
            yield return null; yield return null;
            Assert.That(visual.Motion,Is.EqualTo(NordicMotion.Revive));
            Assert.That(state.Speed,Is.LessThan(0));
            yield return new WaitForSeconds(1.2f);
            Assert.That(visual.Motion,Is.EqualTo(NordicMotion.Idle),"time="+state.Time+" health="+health.CurrentHealth);
            Assert.That(health.CurrentHealth,Is.EqualTo(before));
        }

        private void MoveOwner(Vector2 position)
        {
            player.transform.position = new Vector3(position.x, position.y, player.transform.position.z);
            player.GetComponent<Rigidbody2D>().position = position;
        }

        private static IEnumerator WaitFor(Func<bool> condition)
        {
            float until = Time.realtimeSinceStartup + 20;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(condition(), Is.True, "Camera scenario timed out.");
        }

        private static void PrepareGameplay(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true)) spawner.enabled = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= PrepareGameplay;
            if (manager != null && NetworkServer.active) manager.StopHost();
            if (manager != null) yield return WaitFor(() => !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning);
            BootSceneFixtureObjects.Destroy(bootRoots);
            yield return null;
            NetworkManager.ResetStatics();
        }
    }
}
#endif
