using System;
using System.Collections;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.CameraFX;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;
using UnityEngine.Rendering;

namespace MonsterSupergroup.NetworkCombat
{
    // Explicit short fixture, never installed by normal Boot or Full. No screenshot capture.
    public sealed class EnemyMotionObservation : MonoBehaviour
    {
        private LimboObservationLog log;
        private WaveParameters catalog;
        private ReferenceSpawnDefinition[] choices;
        private int selected, samples, caseMovingFrames, invalidCases, limit = 14000;
        private bool legacy, automatic, moving;
        private NetworkEnemySimulationAgent observed;
        private GameplayCameraRig cameraRig;
        private float began;
        private uint lastRenderedId;
        private Vector2 lastRenderedPosition;

        private IEnumerator Start()
        {
            Directory.CreateDirectory(LimboReferenceLaunch.OutputDirectory);
            log = new LimboObservationLog(Path.Combine(LimboReferenceLaunch.OutputDirectory, "enemy-motion.jsonl"));
            Resources.Load<GameplayWaveRules>("LimboReference/Full").TryCapture(out catalog, out var error);
            if (catalog == null) throw new InvalidOperationException(error);
            choices = catalog.Reference.Clips.GroupBy(c => c.SourceEnemy + "/" + c.Variant + "/" + c.PrefabIndex).Select(g => g.First()).ToArray();
            automatic = LimboReferenceLaunch.Argument("--limbo-motion-case=") == "smoke";
            Note("configuration", "Explicit enemy spawn, weapons suppressed, heal below 300, optional ordinary movement. 50 Hz physics unchanged. Render-frame logging capped at 14000; no screenshots; not pressure evidence.");
            RenderPipelineManager.beginCameraRendering += Render;
            while (NetworkClient.localPlayer == null || NetworkEnemySimulationWorld.Instance == null ||
                NetworkServer.active && !NetworkEnemySimulationWorld.Instance.HasEligiblePlayer) yield return null;
            // Preparation readiness precedes remote avatar creation in additive Gameplay loading.
            while (NetworkCombatWorld.Instance == null || string.IsNullOrEmpty(NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot.RunId) ||
                NetworkServer.active && LimboReferenceLaunch.Argument("--limbo-motion-target=") == "client" &&
                !NetworkServer.spawned.Values.Any(x => x != null && x.netId != NetworkClient.localPlayer.netId && x.GetComponent<PlayerMovement>() != null)) yield return null;
            began = Time.realtimeSinceStartup;
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            Spawn();
            if (!automatic) yield break;
            foreach (int fps in new[] { 60, 144 })
            foreach (bool old in new[] { true, false })
            foreach (bool walk in new[] { false, true })
            {
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = fps;
                legacy = old; moving = walk;
                caseMovingFrames = 0;
                Note("case", $"fps={fps};legacy={legacy};moving={moving}");
                // Restart far enough away for actual navigation, not only the stopped attack pose.
                Spawn();
                yield return new WaitForSecondsRealtime(5);
                Note("case-result", $"fps={fps};legacy={legacy};moving={moving};movingRenderFrames={caseMovingFrames}");
                if (caseMovingFrames < 20) invalidCases++;
            }
            moving = false;
            Note(invalidCases == 0 ? "completed" : "incomplete", $"renderSamples={samples};invalidCases={invalidCases}");
            log.Flush();
            if (LimboReferenceLaunch.Argument("--limbo-wait-for=") == "2")
            {
                var directory = Directory.GetParent(LimboReferenceLaunch.OutputDirectory).FullName;
                string role = LimboReferenceLaunch.Argument("--limbo-role=");
                File.WriteAllText(Path.Combine(directory, role + "-motion-finished"), "done");
                double deadline = Time.realtimeSinceStartupAsDouble + 30;
                while (!File.Exists(Path.Combine(directory, (role == "host" ? "client" : "host") + "-motion-finished")) &&
                    Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            }
            var manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            if (NetworkServer.active) manager.StopHost(); else manager.StopClient();
            yield return new WaitForSecondsRealtime(2);
            Application.Quit(samples > 100 && invalidCases == 0 ? 0 : 1);
        }

        private void Update()
        {
            if (NetworkClient.localPlayer == null) return;
            var owner = NetworkClient.localPlayer;
            owner.GetComponent<PlayerBuildRuntime>()?.SetWeaponExecutionEnabled(false);
            var player = owner.GetComponent<PlayerMovement>();
            if (owner.GetComponent<CombatantBehaviour>().CurrentHealth < 300) player.IncreaseHealth(500);
            if (automatic)
            {
                var selection = owner.GetComponent<ModifierSelectionController>();
                if (selection != null && selection.Offers.Count > 0 && selection.IsPresentationReady && !selection.IsRequestPending)
                { Note("test-card", "first legal card; explicit automated fixture only"); selection.Select(0); }
            }
            if (automatic) player.SetDirection(moving ? new Vector2(Mathf.Cos((Time.realtimeSinceStartup - began) * .6f), Mathf.Sin((Time.realtimeSinceStartup - began) * .6f)) : Vector2.zero);
            if (observed == null) observed = FindObjectsByType<NetworkEnemySimulationAgent>(FindObjectsSortMode.None).FirstOrDefault();
            if (observed != null) observed.GetComponent<EnemySnapshotInterpolator>().ObserveWithoutInterpolation = legacy;
        }

        private void Spawn()
        {
            if (!NetworkServer.active || NetworkClient.localPlayer == null) return;
            if (observed != null) NetworkServer.Destroy(observed.gameObject);
            var clip = choices[selected]; var stats = clip.Stats;
            Vector2 position = (Vector2)NetworkClient.localPlayer.transform.position + Vector2.right * 10;
            if (GameplayMapContext.Active != null) position = GameplayMapContext.Active.FindSpawn(position, 2);
            var instance = Instantiate(catalog.Prefabs[clip.PrefabIndex], position, Quaternion.identity);
            observed = instance.GetComponent<NetworkEnemySimulationAgent>();
            observed.ConfigureBirth(new EnemyBirthParameters { Enabled = true, DefinitionId = clip.DefinitionId,
                SourceEnemy = clip.SourceEnemy, Variant = clip.Variant, Health = stats.BaseHealth, Damage = stats.BaseDamage,
                Speed = stats.BaseSpeed, SpeedMultiplier = 1, Xp = stats.BaseXP, Knockback = stats.KnockBackMultiplier,
                Stun = stats.StunTime, Wind = stats.WindMultiplier, ContactRadius = clip.ContactRadius, ResetOnReposition = clip.ResetOnReposition });
            uint target = NetworkClient.localPlayer.netId;
            if (LimboReferenceLaunch.Argument("--limbo-motion-target=") == "client")
                target = NetworkServer.spawned.Values.First(x => x.netId != target && x.GetComponent<PlayerMovement>() != null).netId;
            observed.ConfigureInitialServerTarget(target);
            NetworkServer.Spawn(instance);
            Note("spawn", clip.SourceEnemy + "/" + clip.Variant + "; test speed multiplier=1; no damage/stat changes");
        }

        private void Render(ScriptableRenderContext context, Camera camera)
        {
            if (samples >= limit || observed == null || !observed.ProductEnemyInitialized) return;
            if (cameraRig == null) cameraRig = FindFirstObjectByType<GameplayCameraRig>();
            if (cameraRig == null || camera != cameraRig.GameCamera) return;
            var body = observed.GetComponent<Rigidbody2D>();
            Vector2 renderedPosition = observed.transform.position;
            if (body.linearVelocity.sqrMagnitude > .01f ||
                lastRenderedId == observed.netId && (renderedPosition - lastRenderedPosition).sqrMagnitude > .000001f) caseMovingFrames++;
            lastRenderedId = observed.netId; lastRenderedPosition = renderedPosition;
            var row = new Frame { frame = Time.frameCount, real = Time.realtimeSinceStartupAsDouble, delta = Time.unscaledDeltaTime,
                fixedTime = Time.fixedTimeAsDouble, physics = body.position, rendered = observed.transform.position,
                velocity = body.linearVelocity, camera = camera.transform.position, player = NetworkClient.localPlayer.transform.position,
                interpolation = body.interpolation.ToString(), role = observed.Authority.Role.ToString(), id = observed.netId,
                epoch = observed.Assignment.Epoch, width = Screen.width, height = Screen.height, targetFps = Application.targetFrameRate,
                legacy = legacy, moving = moving, phase = observed.GetComponent<EnemyController>().CurrentAttackPresentationPhase.ToString() };
            log.WriteLine(JsonUtility.ToJson(row)); samples++;
        }
        private void Note(string kind, string detail) => log?.WriteLine(JsonUtility.ToJson(new Event { kind = kind, detail = detail, real = Time.realtimeSinceStartupAsDouble }));
        private void OnGUI()
        {
            if (automatic || choices == null) return;
            GUILayout.BeginArea(new Rect(15, 90, 410, 200), GUI.skin.box);
            GUILayout.Label("Enemy motion comparison (assisted; no pressure result)");
            GUILayout.Label(choices[selected].SourceEnemy + " v" + choices[selected].Variant);
            if (NetworkServer.active && GUILayout.Button("Next enemy / respawn")) { selected = (selected + 1) % choices.Length; Spawn(); }
            if (GUILayout.Button(legacy ? "Old: no interpolation — switch to repaired" : "Repaired interpolation — switch to old")) { legacy = !legacy; Note("manual-mode", legacy.ToString()); }
            GUILayout.BeginHorizontal();
            foreach (int fps in new[] { 60, 144 }) if (GUILayout.Button(fps + " FPS")) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = fps; Note("manual-fps", fps.ToString()); }
            GUILayout.EndHorizontal(); GUILayout.Label("Move normally, or stand still. Host selects/spawns enemies.");
            GUILayout.EndArea();
        }
        private void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= Render;
            if (observed != null) observed.GetComponent<EnemySnapshotInterpolator>().ObserveWithoutInterpolation = false;
            log?.Dispose();
        }
        [Serializable] private class Event { public string kind, detail; public double real; }
        [Serializable] private class Frame
        {
            public string kind = "render", interpolation, role, phase;
            public int frame, width, height, targetFps; public uint id, epoch;
            public double real, fixedTime; public float delta; public bool legacy, moving;
            public Vector2 physics, velocity; public Vector3 rendered, camera, player;
        }
    }
}
