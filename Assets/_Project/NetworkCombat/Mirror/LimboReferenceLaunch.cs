using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Opt-in development launch through the existing Boot/preparation/Mirror lifecycle.</summary>
    public sealed partial class LimboReferenceLaunch : MonoBehaviour
    {
        private static readonly string[] LaunchArguments = Environment.GetCommandLineArgs();
        public static string Argument(string prefix) => LaunchArguments.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?.Substring(prefix.Length);
        public static bool Manual { get; } = Argument("--limbo-manual=") == "true";
        public static bool SuppressDebugPanels => Profile.StartsWith("audio-", StringComparison.Ordinal) || Manual || Enabled && Argument("--limbo-performance-preset=") != null;
        public static bool Light { get; } = Argument("--limbo-log-detail=") == "light";
        public static bool Enabled => Argument("--limbo-role=") != null;
        public static string OutputDirectory => Argument("--limbo-output=") ?? Path.Combine(Application.persistentDataPath, "LimboReference");
        public static string Profile => Argument("--limbo-profile=") ?? "opening";
        public static GameplayWaveRules Rules => Profile.StartsWith("audio-", StringComparison.Ordinal) ? Resources.Load<GameplayWaveRules>("LimboReference/AudioObservation") : Profile.StartsWith("full", StringComparison.Ordinal) ? Resources.Load<GameplayWaveRules>("LimboReference/" + (Profile == "full" ? "Full" : Profile == "full-validation" ? "FullValidation" : LimboFullObservation.FixtureRulesName)) : Profile.StartsWith("ghoul", StringComparison.Ordinal) ? Resources.Load<GameplayWaveRules>("LimboReference/"+(Profile=="ghoul"?"Ghoul":Profile=="ghoul-motion"?"GhoulMotion":Profile=="ghoul-validation"?"GhoulValidation":LimboGhoulObservation.FixtureRulesName)) : Profile.StartsWith("lostsoul", StringComparison.Ordinal) ? Resources.Load<GameplayWaveRules>("LimboReference/"+(Profile=="lostsoul"?"LostSoul":Profile=="lostsoul-validation"?"LostSoulValidation":LimboLostSoulObservation.FixtureRulesName)) : Profile == "art-effects" ? Resources.Load<GameplayWaveRules>("LimboReference/ArtEffects") : Resources.Load<GameplayWaveRules>("LimboReference/" + (Profile == "dash" ? "Dash" : Profile == "dash-validation" ? "DashValidation" : Profile == "dash-fixture" ? (Argument("--limbo-dash-case=")=="reuse"?"DashReuse":(Argument("--limbo-dash-case=")=="boundary"?"DashBoundary":"DashFixture") + (Argument("--limbo-dash-variant=") ?? "0")) : Profile == "spatial-reposition" ? LimboRepositionFixture.RulesName : Profile == "spatial-b" ? "SpatialB" : Profile == "spatial-barrier" ? "SpatialBarrier" : Profile == "spatial-overlap" ? (Argument("--limbo-spatial-case=") == "occupancy" ? "SpatialOverlapWait" : "SpatialOverlap") : Profile == "stage2" ? "Stage2" : Profile == "stage2-validation" ? "Stage2Validation" : Profile == "stage2-fixture" ? "Stage2" + (Argument("--limbo-fixture-mode=")?.StartsWith("l-")==true?"RusherWave":Argument("--limbo-fixture-enemy=") ?? "Skeleton0") : Profile == "full" ? "Full" : Profile == "imp" ? "ImpOpening" : Profile == "imp-validation" ? "ImpValidation" : Profile == "imp-v0" ? "ImpFixture0" : Profile == "imp-v1" ? "ImpFixture1" : "Opening"));
        private BootGameplayNetworkManager manager;
        private readonly HashSet<uint> observed = new HashSet<uint>();
        private LimboObservationLog audit;
        private float nextAudit;
        private string role, failure;
        private bool completed;
        private int previousHealth = -1;
        private int previousMaximum = -1;
        private CombatantBehaviour auditedPlayer;
        private Vector2 walkOrigin;
        private bool hasWalkOrigin;
        private uint auditRound;
        private bool windowConfigured;
        private string lastSelectionState;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Enabled) return;
            AstralShift.DebugTools.DBL.VerboseEnabled = !Light;
            if (Light) Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            var runner = new GameObject("Limbo reference launch").AddComponent<LimboReferenceLaunch>();
            DontDestroyOnLoad(runner.gameObject);
            runner.failure = LimboManualOptions.Validate(LaunchArguments);
            if (runner.failure != null) { Debug.LogError(runner.failure); return; }
            runner.gameObject.AddComponent<LimboPerformanceObservation>();
            if (Profile.StartsWith("audio-", StringComparison.Ordinal)) runner.gameObject.AddComponent<WeaponAudioObservation>();
            if (!Light) runner.gameObject.AddComponent<LimboAttackTimelineObservation>();
            if (Argument("--limbo-art-observe=") == "true") runner.gameObject.AddComponent<LimboArtObservation>();
            if (Profile == "art-effects")
            {
                var visuals = Instantiate(Resources.Load<GameObject>("LimboReference/ArtEffectDisplay"));
                DontDestroyOnLoad(visuals);
            }
            if (Profile.StartsWith("imp", StringComparison.Ordinal) || Profile == "stage2" || Profile == "stage2-validation")
                runner.gameObject.AddComponent<LimboImpObservation>();
            if (Profile.StartsWith("stage2", StringComparison.Ordinal) || Profile.StartsWith("dash", StringComparison.Ordinal)) runner.gameObject.AddComponent<LimboStage2Observation>();
            if (Profile.StartsWith("dash", StringComparison.Ordinal)) runner.gameObject.AddComponent<LimboDashObservation>();
            if (Profile.StartsWith("full", StringComparison.Ordinal)) { runner.gameObject.AddComponent<LimboFullObservation>(); runner.gameObject.AddComponent<LimboStage2Observation>(); runner.gameObject.AddComponent<LimboSpatialObservation>(); }
            if (Profile.StartsWith("ghoul", StringComparison.Ordinal))
            {
                runner.gameObject.AddComponent<LimboGhoulObservation>();
                if (Profile != "ghoul-motion") { runner.gameObject.AddComponent<LimboStage2Observation>(); runner.gameObject.AddComponent<LimboSpatialObservation>(); }
            }
            if (Profile.StartsWith("lostsoul", StringComparison.Ordinal)) { runner.gameObject.AddComponent<LimboLostSoulObservation>(); runner.gameObject.AddComponent<LimboStage2Observation>(); runner.gameObject.AddComponent<LimboSpatialObservation>(); }
            if (Profile.StartsWith("spatial-", StringComparison.Ordinal) || Profile == "dash" || Profile == "dash-validation") runner.gameObject.AddComponent<LimboSpatialObservation>();
            if (Profile == "spatial-reposition") runner.gameObject.AddComponent<LimboRepositionFixture>();
        }

        private IEnumerator Start()
        {
            if (failure != null) yield break;
            role = Argument("--limbo-role=");
            if (role != "host" && role != "client") { failure = "Use --limbo-role=host or client."; yield break; }
            Application.runInBackground = true; Application.targetFrameRate = 60;
            Directory.CreateDirectory(OutputDirectory);
            audit = new LimboObservationLog(Path.Combine(OutputDirectory, role + "-audit.jsonl"));
            BeginDeliveryObservation();
            yield return null;
            manager = FindFirstObjectByType<BootGameplayNetworkManager>();
            if (manager == null || Rules == null) { failure = "Boot manager or Limbo reference assets missing."; Debug.LogError(failure); yield break; }
            if (!Rules.TryCapture(out var captured, out var configurationError) ||
                (configurationError = captured.Reference?.ReadinessError()) != null)
            {
                failure = "参考配置尚未就绪：\n" + configurationError;
                manager.ShowMenuNotice("参考配置尚未就绪，详见左上角的适配／验证状态与日志。");
                Debug.LogWarning("[LimboGate] " + configurationError);
                yield break;
            }
            foreach (var prefab in captured.Prefabs)
                if (prefab != null && !manager.spawnPrefabs.Contains(prefab)) manager.spawnPrefabs.Add(prefab);
            manager.ConfigurePreparationFlow(true);
            var backend = manager.GetComponent<NetworkBackendBootstrap>();
            if (!backend.TryPrepareKcp("127.0.0.1", ushort.Parse(Argument("--limbo-port=") ?? "7993"), false, out var error))
            { failure = error; Debug.LogError(error); yield break; }
            if (role == "host") manager.StartHost(); else manager.StartClient();
            float deadline = Time.realtimeSinceStartup + 150;
            while (manager.RoomSnapshot.Members == null || manager.RoomSnapshot.SelfId == 0)
            { if (Time.realtimeSinceStartup > deadline) { failure = "Preparation connection timeout."; yield break; } yield return null; }
            manager.SetOwnLoadout(1);
            while (!manager.RoomSnapshot.Members.Any(m => m.ParticipantId == manager.RoomSnapshot.SelfId && m.WeaponId == 1))
            { if (Time.realtimeSinceStartup > deadline) { failure = "Loadout acknowledgement timeout."; yield break; } yield return null; }
            manager.SetOwnReady(true);
            if (role == "host")
            {
                int players = int.Parse(Argument("--limbo-wait-for=") ?? "1");
                while (manager.RoomSnapshot.Members.Length < players || !manager.RoomSnapshot.Members.All(m => m.Ready))
                { if (Time.realtimeSinceStartup > deadline) { failure = "Preparation ready timeout."; yield break; } yield return null; }
                manager.StartPreparedGame();
            }
        }

        private void Update()
        {
            LimboObservationLog.FlushDue();
            ObserveDelivery();
            if (manager == null || !manager.IsGameplayLoaded) return;
            if (!windowConfigured && (Argument("--limbo-windowed=") == "true" || Argument("--limbo-performance-preset=") != null))
            {
                // Opt-in observation window; do not write SettingsManager or player preferences.
                windowConfigured = true;
                bool high = Argument("--limbo-performance-preset=") == "4k144";
                Screen.SetResolution(high ? 3840 : 1280, high ? 2160 : 720, high ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
                if (Manual || Argument("--limbo-performance-preset=") != null)
                { QualitySettings.vSyncCount = 0; Application.targetFrameRate = high ? 144 : 60; }
            }
            var world = NetworkCombatWorld.Instance;
            if (world == null) return;
            var snapshot = world.GetComponent<NetworkWaveProgress>().Snapshot;
            if (auditRound != manager.RoomSnapshot.Round)
            {
                auditRound = manager.RoomSnapshot.Round; observed.Clear(); completed = false; hasWalkOrigin = false;
                if (auditedPlayer != null) auditedPlayer.HealthChanged -= RecordHealth;
                auditedPlayer = null; previousHealth = previousMaximum = -1;
                lastSelectionState = null;
                audit?.WriteLine(JsonUtility.ToJson(new RoundAudit { kind = "round", run = snapshot.RunId, round = auditRound }));
            }
            if (auditedPlayer == null && NetworkClient.localPlayer != null)
            {
                auditedPlayer = NetworkClient.localPlayer.GetComponent<CombatantBehaviour>();
                previousHealth = auditedPlayer.CurrentHealth;
                previousMaximum = auditedPlayer.MaxHealth;
                auditedPlayer.HealthChanged += RecordHealth;
            }
            foreach (var pair in NetworkClient.spawned)
            {
                var identity = pair.Value;
                if (identity == null || !identity.TryGetComponent<NetworkEnemySimulationAgent>(out var agent) ||
                    !agent.Birth.Enabled || !agent.ProductEnemyInitialized || !observed.Add(pair.Key)) continue;
                var stats = identity.GetComponent<EnemyController>().stats;
                var combatant = identity.GetComponent<CombatantBehaviour>();
                var birth = agent.Birth;
                float expectedSpeed = birth.Speed * (agent.ReferenceResetVersion == 0 ? birth.SpeedMultiplier : 1);
                bool match = combatant.MaxHealth == birth.Health && stats.Damage == birth.Damage && Mathf.Abs(stats.Speed - expectedSpeed) < .0001;
                audit?.WriteLine(JsonUtility.ToJson(new BirthAudit { kind = "birth", role = role, id = pair.Key,
                    run = snapshot.RunId, round = auditRound, birth = birth,
                    source = birth.SourceEnemy, hp = combatant.MaxHealth, damage = stats.Damage, speed = stats.Speed,
                    xp = stats.XP, match = match }));
                if (!match) { failure = "Replicated birth attributes mismatch: " + pair.Key; Debug.LogError("[LimboAudit] " + failure); }
            }
            if (Time.realtimeSinceStartup >= nextAudit)
            {
                nextAudit = Time.realtimeSinceStartup + 1;
                int health = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<CombatantBehaviour>().CurrentHealth : -1;
                audit?.WriteLine(JsonUtility.ToJson(new FrameAudit { kind = "frame", role = role, snapshot = snapshot, health = health, observed = observed.Count,
                    realtime = Time.realtimeSinceStartupAsDouble,
                    position = NetworkClient.localPlayer != null ? (Vector2)NetworkClient.localPlayer.transform.position : default }));
            }
            var selection = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<NetworkModifierSelection>() : null;
            if (selection != null)
            {
                string token = selection.IsSelecting + "/" + selection.Level + "/" + selection.LocalEventId + "/" + selection.OwnerBuildRevision;
                if (token != lastSelectionState)
                {
                    lastSelectionState = token;
                    audit?.WriteLine(JsonUtility.ToJson(new SelectionAudit { kind = "selection", role = role,
                        elapsed = snapshot.Elapsed, realtime = Time.realtimeSinceStartupAsDouble,
                        selecting = selection.IsSelecting, level = selection.Level,
                        offer = selection.LocalEventId, buildRevision = selection.OwnerBuildRevision }));
                    RecordPlayerBuild("selection-state");
                }
            }
            if (!completed && snapshot.Phase == WavePhase.Completed)
            {
                completed = true;
                Debug.Log($"[LimboAudit] reached-preview-end role={role} observed={observed.Count} spawned={snapshot.TotalSpawned} health={previousHealth}");
            }
        }

        private void RecordHealth(int current, int maximum)
        {
            if (current == previousHealth && maximum == previousMaximum) return;
            double elapsed = NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot.Elapsed;
            audit?.WriteLine(JsonUtility.ToJson(new HealthAudit { kind = "health", role = role, elapsed = elapsed,
                run = deliveryRun, round = auditRound,
                realtime = Time.realtimeSinceStartupAsDouble, previous = previousHealth, current = current, maximum = maximum }));
            if (current < previousHealth) Debug.Log($"[LimboHit] role={role} elapsed={elapsed:F3} lost={previousHealth - current} hp={current}");
            previousHealth = current;
            previousMaximum = maximum;
        }

        // Explicit test input through the production movement component. No teleport, damage or health overrides.
        private void LateUpdate()
        {
            if (Argument("--limbo-autowalk=") != "true" || manager == null || !manager.IsGameplayLoaded ||
                NetworkClient.localPlayer == null || completed) return;
            var player = NetworkClient.localPlayer.GetComponent<PlayerMovement>();
            if (player == null || player.IsUpgradeSelectionLocked) return;
            Vector2 position = player.transform.position;
            if (!hasWalkOrigin) { walkOrigin = position; hasWalkOrigin = true; }
            Vector2 offset = position - walkOrigin;
            Vector2 direction = offset.sqrMagnitude < 4 ? Vector2.right : new Vector2(-offset.y, offset.x).normalized + offset.normalized * Mathf.Clamp((12 - offset.magnitude) * .3f, -1, 1);
            Vector2 avoidance = Vector2.zero;
            foreach (var pair in NetworkClient.spawned)
            {
                if (pair.Value == null || !pair.Value.TryGetComponent<NetworkEnemySimulationAgent>(out var enemy) || !enemy.Birth.Enabled) continue;
                Vector2 away = position - (Vector2)enemy.transform.position;
                if (away.sqrMagnitude > .001f && away.sqrMagnitude < 16) avoidance += away.normalized * (4 - away.magnitude);
            }
            player.SetDirection((direction + avoidance).normalized);
        }

        private void OnGUI()
        {
            if (Manual && failure == null)
            {
                GUI.Label(new Rect(12, Screen.height - 24, Screen.width - 24, 24),
                    $"Limbo {Argument("--limbo-version=")} | {Argument("--limbo-session=")} | {role} | round {auditRound}");
                return;
            }
            if (failure != null && manager == null) GUI.Box(new Rect(12, 12, 650, 150), failure);
            if (manager == null) return;
            var previousMatrix = GUI.matrix;
            float scale = Mathf.Max(1, Screen.width / 1920f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
            var progress = NetworkCombatWorld.Instance != null ? NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot : default;
            GUI.Box(new Rect(12, 12, 510, failure == null ? 130 : 240), "");
            GUI.Label(new Rect(24, 20, 480, 28), "Limbo reference / " + role + " / " + progress.Phase, labelStyle);
            GUI.Label(new Rect(24, 48, 480, 28), $"{progress.Elapsed:F1}/{progress.StageEndTime:F1}s  spawned {progress.TotalSpawned}  alive {progress.Alive}", labelStyle);
            GUI.Label(new Rect(24, 78, 480, failure == null ? 55 : 165), failure ?? $"{Profile}. Source combat-pressure comparison pending.", labelStyle);
            GUI.matrix = previousMatrix;
        }
        private void OnApplicationQuit() { FinishDeliveryObservation("process-exit"); LimboObservationLog.FlushAll(); }
        private void OnDestroy() { FinishDeliveryObservation("observer-destroy"); if (auditedPlayer != null) auditedPlayer.HealthChanged -= RecordHealth; audit?.Dispose(); audit = null; }
        [Serializable] private class RoundAudit { public string kind, run; public uint round; }
        [Serializable] private class BirthAudit { public string kind, role, source, run; public uint id, round; public EnemyBirthParameters birth; public int hp, damage; public float speed, xp; public bool match; }
        [Serializable] private class FrameAudit { public string kind, role; public WaveProgressSnapshot snapshot; public int health, observed; public double realtime; public Vector2 position; }
        [Serializable] private class SelectionAudit { public string kind, role; public double elapsed, realtime; public bool selecting; public int level; public ulong offer; public uint buildRevision; }
        [Serializable] private class HealthAudit { public string kind, role, run, source = "unknown"; public uint round; public double elapsed, realtime; public int previous, current, maximum; }
    }
}
