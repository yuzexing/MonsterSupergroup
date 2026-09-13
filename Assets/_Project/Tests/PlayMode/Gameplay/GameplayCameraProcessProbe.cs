using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.CameraFX;
using AstralShift.HellMaiden.Player;
using Com.LuisPedroFonseca.ProCamera2D;
using Mirror;
using MonsterSupergroup.NetworkCombat;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.Gameplay.Tests
{
    // Opt-in test build only. Markers coordinate the harness; gameplay uses real Boot, KCP and admission.
    [DefaultExecutionOrder(500)]
    public sealed class GameplayCameraProcessProbe : MonoBehaviour
    {
        private string role, artifacts;
        private bool host, finished, captureFrames;
        private float deadline;
        private BootGameplayNetworkManager manager;
        private GameplayCameraRig view;
        private PlayerMovement player;
        private ProCamera2D rig;
        private readonly List<int> shakes = new List<int>();
        private bool drive;
        [Serializable] private sealed class AnimationTraffic { public string role; public int ownerCommands, mirrorMessageBytes, statePayloadBytes, stateChanges; public float seconds; }
        private readonly AnimationTraffic animationTraffic = new AnimationTraffic();
        private float trafficStarted;
        private void RecordAnimationTraffic(NetworkDiagnostics.MessageInfo info)
        {
            if (NetworkClient.localPlayer == null || !(info.message is CommandMessage command)) return;
            var presentation = NetworkClient.localPlayer.GetComponent<NetworkPlayerPresentation>();
            if (presentation == null || command.netId != NetworkClient.localPlayer.netId || command.componentIndex != presentation.ComponentIndex) return;
            animationTraffic.ownerCommands += info.count; animationTraffic.mirrorMessageBytes += info.bytes * info.count;
        }
        private void LateUpdate()
        {
            if (drive && player != null) player.SetDirection(host ? Vector2.right : Vector2.left);
            if (finished || view == null || view.BoundPlayer == null) return;
            try { CheckView(true); }
            catch (Exception error) { Debug.LogException(error); Finish(false); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string roleArgument = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--camera-role="));
            if (roleArgument == null) return;
            FindFirstObjectByType<BootGameplayNetworkManager>().ConfigurePreparationFlow(false);
            var probe = new GameObject("Gameplay camera process probe").AddComponent<GameplayCameraProcessProbe>();
            probe.role = roleArgument.Substring("--camera-role=".Length);
            probe.host = probe.role == "host";
            probe.captureFrames = Environment.GetCommandLineArgs().Contains("--camera-capture-frames");
            probe.artifacts = Environment.GetCommandLineArgs().First(a => a.StartsWith("--camera-artifacts="))
                .Substring("--camera-artifacts=".Length);
            DontDestroyOnLoad(probe.gameObject);
        }

        private IEnumerator Start()
        {
            deadline = Time.realtimeSinceStartup + 120;
            trafficStarted=Time.realtimeSinceStartup; NetworkDiagnostics.OutMessageEvent += RecordAnimationTraffic;
            Application.runInBackground = true;
            SceneManager.sceneLoaded += PrepareGameplay;
            IEnumerator run = Run();
            // Flatten nested enumerators so assertions in any phase fail the process, not just a coroutine.
            var stack = new Stack<IEnumerator>();
            stack.Push(run);
            while (stack.Count > 0)
            {
                object next;
                try
                {
                    if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                    next = stack.Peek().Current;
                    if (next is IEnumerator nested) { stack.Push(nested); continue; }
                }
                catch (Exception error) { Debug.LogException(error); Finish(false); yield break; }
                yield return next;
            }
            Finish(true);
        }

        private void Update()
        {
            if (deadline > 0 && Time.realtimeSinceStartup > deadline) Finish(false);
        }

        private IEnumerator Run()
        {
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            Require(manager != null, "Must start from formal Boot.");
            manager.ConfigurePreparationFlow(false);
            gameObject.AddComponent<WeaponAttackAdmissionFixtureGate>();
            Require(manager.GetComponent<NetworkBackendBootstrap>().TryPrepareKcp("127.0.0.1", 7905, false, out var error), error);
            if (host) manager.StartHost(); else manager.StartClient();
            yield return AwaitOwner();
            Mark(role + "-ready");
            while (!Has("host-ready") || !Has("client-ready") || Players().Count() != 2) yield return null;
            MoveOwner(new Vector2(host ? -2 : 2, 0));
            drive = true;
            Mark(role + "-walking");
            while (!Has("host-walking") || !Has("client-walking")) yield return null;
            yield return new WaitForSeconds(.6f);
            var remote = Players().Single(p => p != NetworkClient.localPlayer);
            var remoteAnimator = remote.GetComponentInChildren<NordicPlayerAnimator>();
            Require(remoteAnimator.Motion == NordicMotion.Walk && remoteAnimator.FacingLeft == host, "Remote Axeldor walk/facing state was not delivered.");
            Require(!remote.GetComponent<PlayerMovement>().enabled, "Remote player enabled local movement.");
            Capture("multiplayer-walk");
            Mark(role + "-saw-walk");
            while (!Has("host-saw-walk") || !Has("client-saw-walk")) yield return null;
            drive = false; player.StopMovement(); player.SetDirection(Vector2.zero);
            yield return new WaitForSeconds(.3f);
            Require(NetworkClient.localPlayer.GetComponent<NetworkPlayerPresentation>().SentChanges <= 16, "Animation sent continuously while a movement state was unchanged.");
            using (var writer = NetworkWriterPool.Get()) { writer.Write(NetworkClient.localPlayer.GetComponent<NetworkPlayerPresentation>().Visual); animationTraffic.statePayloadBytes=writer.Position; }
            animationTraffic.stateChanges=NetworkClient.localPlayer.GetComponent<NetworkPlayerPresentation>().SentChanges;
            Debug.Log("[NordicGameplay] Remote walk, facing, disabled input and state-change traffic PASS");
            MoveOwner(new Vector2(host ? -20 : 20, host ? -8 : 8));
            yield return new WaitForSeconds(2);
            CheckView();
            Require(Vector2.Distance(view.transform.position, player.transform.position) < .05f, "Remote target changed follow midpoint.");
            Capture("independent");
            player.DecreaseHealth(1); // Real Owner health path replicates to the other endpoint.
            Mark(role + "-moved");
            while (!Has("host-moved") || !Has("client-moved")) yield return null;
            yield return new WaitForSeconds(.5f);
            Require(shakes.SequenceEqual(new[] { 3 }), "Local damage must shake once; remote health replication must not shake.");
            shakes.Clear();
            if (host)
            {
                var ultimate = NetworkClient.localPlayer.GetComponent<NetworkPlayerUltimate>();
                Require(ultimate.ServerGrantCharge(), "Host grant failed.");
                yield return CastAtBoundary(ultimate);
                Mark("host-cast-done");
                while (!Has("client-saw-host")) { CheckView(); yield return null; }
                Require(Players().Single(p => p != NetworkClient.localPlayer).GetComponent<NetworkPlayerUltimate>().ServerGrantCharge(), "Client grant failed.");
                Mark("client-granted");
                while (!Has("client-cast-done")) { CheckView(); yield return null; }
                Require(shakes.SequenceEqual(new[] { 2, 2 }), "Remote Ultimate shook Host or Host consumed both paths.");
                Mark("host-saw-client");
                while (!Has("client-disconnected")) { CheckView(); yield return null; }
                Require(view.BoundPlayer == player, "Remote disconnect released Host target.");
                Mark("host-saw-disconnect");
                while (!Has("client-reconnected") || Players().Count() != 2) { CheckView(); yield return null; }
                CheckView();
                Mark("host-saw-reconnect");
                while (!Has("client-done")) { CheckView(); yield return null; }
                manager.StopHost();
            }
            else
            {
                while (!Has("host-cast-done")) { CheckView(); yield return null; }
                Require(shakes.Count == 0, "Remote Host Ultimate shook Client.");
                Mark("client-saw-host");
                while (!Has("client-granted")) yield return null;
                yield return CastAtBoundary(NetworkClient.localPlayer.GetComponent<NetworkPlayerUltimate>());
                Mark("client-cast-done");
                while (!Has("host-saw-client")) yield return null;
                uint oldId = NetworkClient.localPlayer.netId;
                Transform oldContainer = view.transform.parent;
                manager.StopClient();
                while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning) yield return null;
                Require(view == null && oldContainer == null, "View/container leaked on disconnect.");
                Require(FindObjectsByType<GameplayCameraRig>(FindObjectsSortMode.None).Length == 0, "Stale camera after unload.");
                Mark("client-disconnected");
                while (!Has("host-saw-disconnect")) yield return null;
                manager.StartClient();
                yield return AwaitOwner();
                Require(NetworkClient.localPlayer.netId != oldId, "Reconnect reused destroyed avatar.");
                MoveOwner(new Vector2(-15, 15));
                yield return new WaitForSeconds(2);
                CheckView();
                Require(shakes.Count == 0, "Old animation callback replayed on new camera.");
                Capture("reconnected");
                Mark("client-reconnected");
                while (!Has("host-saw-reconnect")) yield return null;
                manager.StopClient();
            }
            while (manager.IsGameplayLoaded || manager.IsGameplayTransitioning) yield return null;
            Require(FindObjectsByType<GameplayCameraRig>(FindObjectsSortMode.None).Length == 0, "Gameplay camera leaked.");
            if (captureFrames)
                Require(Directory.GetFiles(artifacts, role + "-*.png").Length > 0, "Requested frame capture produced no images; visual validation is incomplete.");
            if (!host) Mark("client-done");
        }

        private IEnumerator AwaitOwner()
        {
            while (NetworkClient.localPlayer == null || FindFirstObjectByType<GameplayCameraRig>()?.BoundPlayer == null) yield return null;
            player = NetworkClient.localPlayer.GetComponent<PlayerMovement>();
            view = FindFirstObjectByType<GameplayCameraRig>();
            rig = view.GetComponent<ProCamera2D>();
            shakes.Clear();
            view.ShakePlayed += OnShake;
            CheckView();
        }

        private void OnShake(int index)
        {
            shakes.Add(index);
            Debug.Log($"[GameplayCameraProcess] role={role} event=shake index={index} count={shakes.Count}");
            Capture("shake-preset-" + index + "-" + shakes.Count);
        }

        private IEnumerator CastAtBoundary(NetworkPlayerUltimate ultimate)
        {
            Vector2 position = GameplayMapContext.Active.FindSpawn(new Vector2(host ? -55 : 55, GameplayMapContext.Active.Bounds.max.y - .2f), .1f);
            MoveOwner(position);
            yield return new WaitForSeconds(2);
            while (!ultimate.HasCharge || ultimate.OwnerAttack == null) yield return null;
            Require(ultimate.RequestUse(), "Admitted real Ultimate could not start.");
            float until = Time.realtimeSinceStartup + 6;
            float maxShakeOffset = 0;
            while (Time.realtimeSinceStartup < until)
            {
                CheckView();
                Require(Vector2.Distance(player.transform.position, position) < .02f, "Camera changed Owner position.");
                Vector3 offset = view.transform.parent.localPosition;
                maxShakeOffset = Mathf.Max(maxShakeOffset, offset.magnitude);
                Require(float.IsFinite(offset.x) && float.IsFinite(offset.y) && offset.magnitude < 5, "Unbounded shake offset.");
                yield return null;
            }
            Require(shakes.SequenceEqual(new[] { 2, 2 }), "Expected exactly two source animation shakes.");
            Require(maxShakeOffset > .01f, "Trigger did not produce actual camera movement.");
            Require(view.transform.parent.localPosition.magnitude < .01f, "Shake did not settle.");
            Debug.Log($"[GameplayCameraProcess] role={role} event=combination maxShakeOffset={maxShakeOffset:F3} localCamera={view.transform.localPosition}");
            Capture("boundary-settled");
        }

        private void CheckView(bool renderedBounds = false)
        {
            Require(view != null && view.BoundPlayer == player && rig.CameraTargets.Count == 1 &&
                rig.CameraTargets[0].TargetTransform == player.transform, "Wrong or duplicate local follow target.");
            Require(!view.GameCamera.orthographic && Mathf.Abs(view.GameCamera.fieldOfView - 80) < .001f && Mathf.Abs(view.transform.position.z + 10) < .001f, "Nordic fixed lens changed.");
            Require(FindObjectsByType<Camera>(FindObjectsSortMode.None).Count(c => c.enabled && c.CompareTag("MainCamera")) == 1, "Duplicate main camera.");
            Require(FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(c => c.enabled) == 1, "Duplicate AudioListener.");
            var bounds = view.GetComponent<ProCamera2DNumericBoundaries>();
            Bounds actual = GameplayCameraGeometry.ViewBounds(view.GameCamera);
            // Shake coroutines run between Update and LateUpdate. Check the final rendered pose.
            Require(!renderedBounds || (actual.min.x >= bounds.LeftBoundary - .03f && actual.max.x <= bounds.RightBoundary + .03f &&
                actual.min.y >= bounds.BottomBoundary - .03f && actual.max.y <= bounds.TopBoundary + .03f), "Actual shaken view escaped Ground limits.");
        }

        private void MoveOwner(Vector2 position)
        {
            position = GameplayMapContext.Active.FindSpawn(position, .1f);
            player.transform.position = new Vector3(position.x, position.y, player.transform.position.z);
            player.GetComponent<Rigidbody2D>().position = position;
        }
        private IEnumerable<NetworkIdentity> Players() => NetworkClient.spawned.Values.Where(p => p != null && p.GetComponent<NetworkPlayerBootstrap>() != null);
        private void Capture(string phase)
        {
            if (captureFrames) ScreenCapture.CaptureScreenshot(Path.Combine(artifacts, role + "-" + phase + ".png"));
        }
        private void Mark(string name) { File.WriteAllText(Path.Combine(artifacts, name + ".ok"), name); Debug.Log($"[GameplayCameraProcess] event={name}"); }
        private bool Has(string name) => File.Exists(Path.Combine(artifacts, name + ".ok"));
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void PrepareGameplay(Scene scene, LoadSceneMode mode)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (var spawner in root.GetComponentsInChildren<NetworkGameplayEnemySpawner>(true)) spawner.enabled = false;
        }
        private void Finish(bool passed)
        {
            if (finished) return;
            finished = true;
            NetworkDiagnostics.OutMessageEvent -= RecordAnimationTraffic;
            animationTraffic.role=role; animationTraffic.seconds=Time.realtimeSinceStartup-trafficStarted;
            File.WriteAllText(Path.Combine(artifacts,role+"-animation-traffic.json"),JsonUtility.ToJson(animationTraffic,true));
            SceneManager.sceneLoaded -= PrepareGameplay;
            Debug.Log($"[GameplayCameraProcess] role={role} result={(passed ? "PASS" : "FAIL")}");
            Application.Quit(passed ? 0 : 1);
        }
    }
}
