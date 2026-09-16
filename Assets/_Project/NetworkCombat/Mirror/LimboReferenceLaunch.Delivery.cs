using System;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.Options;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class LimboReferenceLaunch
    {
        private static LimboReferenceLaunch activeObservation;
        private string deliveryRun, optionsToken;
        private uint deliveryRound, baselinePlayer;
        private bool deliveryEnded, deliveryClosed, wasSelecting;
        private double selectStarted, nextDeliverySample, lastWriteMs, lastFlushMs;
        private long lastLines;
        private float lastScale = -1;
        private readonly float[] frameTimes = new float[512];
        private int frameCount, sampledAlivePeak;
        private double frameSum;
        private float frameMaximum;
        private WaveProgressSnapshot lastDeliveryProgress;

        private void BeginDeliveryObservation()
        {
            activeObservation = this;
            WriteDelivery("process-start", Manual ? "Manual Full: all test inputs disabled" : "Explicit reference/technical launch",
                new LaunchRow { arguments = LaunchArguments, version = Argument("--limbo-version="), session = Argument("--limbo-session="),
                    detail = Light ? "light" : "detailed", device = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString(),
                    unity = Application.unityVersion });
        }

        private void ObserveDelivery()
        {
            if (audit == null || deliveryClosed) return;
            float milliseconds = Time.unscaledDeltaTime * 1000;
            frameTimes[frameCount++ % frameTimes.Length] = milliseconds;
            frameSum += milliseconds; frameMaximum = Mathf.Max(frameMaximum, milliseconds);
            var progress = NetworkCombatWorld.Instance != null ? NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot : default;
            if (!string.IsNullOrEmpty(progress.RunId) && deliveryRun != progress.RunId)
            {
                if (deliveryRun != null && !deliveryEnded) WriteDelivery("run-ended", "interrupted-before-next-run");
                deliveryRun = progress.RunId; deliveryRound = manager != null ? manager.RoomSnapshot.Round : 0;
                baselinePlayer = 0; sampledAlivePeak = 0; deliveryEnded = false; wasSelecting = false;
                WriteDelivery("run-start", "RunId and Round group this process's event files", progress);
            }
            if (!string.IsNullOrEmpty(progress.RunId)) lastDeliveryProgress = progress;
            var local = NetworkClient.localPlayer;
            // Avatar teardown can precede returning to the menu. End the selection interval
            // at that observed boundary, not when the user eventually closes the process.
            if (local == null) InterruptSelectionObservation("local-avatar-unavailable");
            if (local != null && local.TryGetComponent<PlayerMovement>(out var player) && player.IsRuntimeInitialized &&
                local.TryGetComponent<PlayerBuildRuntime>(out var build) && build.IsBuildActive)
            {
                if (baselinePlayer != local.netId)
                {
                    baselinePlayer = local.netId;
                    RecordPlayerBuild("player-initialized");
                }
                bool selecting = local.GetComponent<NetworkModifierSelection>().IsSelecting;
                if (selecting != wasSelecting)
                {
                    if (selecting) selectStarted = Time.realtimeSinceStartupAsDouble;
                    WriteDelivery(selecting ? "selection-open" : "selection-close", "Measured wall time; combat pause recorded separately",
                        new DurationRow { seconds = selecting ? 0 : Time.realtimeSinceStartupAsDouble - selectStarted });
                    wasSelecting = selecting;
                }
            }
            if (lastScale != Time.timeScale)
            {
                lastScale = Time.timeScale;
                WriteDelivery("time-scale", "Existing pause/slow-motion state", new DurationRow { seconds = lastScale });
            }
            if (!deliveryEnded && deliveryRun != null && (BootGameplayNetworkManager.CombatHasEnded || manager != null && !manager.IsGameplayLoaded && !manager.IsGameplayTransitioning))
            {
                deliveryEnded = true;
                InterruptSelectionObservation("run-ended");
                WriteDelivery("run-ended", progress.Phase == WavePhase.Completed ? "completed" : BootGameplayNetworkManager.CombatHasEnded ? "failed" : "left-gameplay", lastDeliveryProgress);
                LimboObservationLog.FlushAll();
            }
            if (Time.realtimeSinceStartupAsDouble < nextDeliverySample) return;
            nextDeliverySample = Time.realtimeSinceStartupAsDouble + 1;
            sampledAlivePeak = Math.Max(sampledAlivePeak, progress.Alive);
            string settings = JsonUtility.ToJson(new DisplayRow { width = Screen.width, height = Screen.height, fullscreen = Screen.fullScreenMode.ToString(),
                targetFps = Application.targetFrameRate, vsync = QualitySettings.vSyncCount,
                settings = GameOptionsService.Instance != null ? GameOptionsService.Instance.Current : null });
            if (optionsToken != settings) { optionsToken = settings; WriteDelivery("display-settings", "Runtime values take precedence over stored preferences", settings); }
            int retained = Math.Min(frameCount, frameTimes.Length);
            Array.Sort(frameTimes, 0, retained);
            WriteDelivery("performance", "1-second samples; frame p95 uses at most the latest 512 frames, alive peak is sampled, not event peak", new PerformanceRow {
                snapshot = progress, sampledAlivePeak = sampledAlivePeak, frames = frameCount, meanFrameMs = frameCount > 0 ? frameSum / frameCount : 0,
                maxFrameMs = frameMaximum, p95FrameMs = retained > 0 ? frameTimes[Math.Max(0, (int)Math.Ceiling(retained * .95) - 1)] : 0,
                logWriteMs = LimboObservationLog.WriteMilliseconds - lastWriteMs, logFlushMs = LimboObservationLog.FlushMilliseconds - lastFlushMs,
                logLines = LimboObservationLog.Lines - lastLines, openWriters = LimboObservationLog.OpenCount, managedBytes = GC.GetTotalMemory(false) });
            lastWriteMs = LimboObservationLog.WriteMilliseconds; lastFlushMs = LimboObservationLog.FlushMilliseconds; lastLines = LimboObservationLog.Lines;
            frameCount = 0; frameSum = 0; frameMaximum = 0;
        }

        private void RecordPlayerBuild(string kind)
        {
            var local = NetworkClient.localPlayer;
            if (local == null || local.GetComponent<PlayerMovement>() is not { IsRuntimeInitialized: true } player) return;
            var combatant = local.GetComponent<CombatantBehaviour>();
            WriteDelivery(kind, "Actual current stats and accepted build, not prefab defaults", new PlayerRow {
                player = local.netId, health = combatant.CurrentHealth, maximum = combatant.MaxHealth,
                speed = player.PlayerStats.currentStats.moveSpeed, xpModifier = player.PlayerStats.currentStats.xpModifier,
                build = local.GetComponent<PlayerBuildRuntime>().CaptureState() });
        }

        public static void ObserveAcceptedSelection(uint player, ulong eventId, UpgradeSelectionStage stage, ModifierOffer offer)
        {
            if (activeObservation == null) return;
            activeObservation.WriteDelivery("selection-accepted", "Server accepted choice; equipment-target selection is distinct from applying an upgrade",
                new ChoiceRow { player = player, eventId = eventId, stage = stage.ToString(), offer = offer.OfferId,
                    kind = offer.Kind.ToString(), content = offer.ContentId, slot = offer.TargetSlotIndex, level = offer.LevelIndex, rarity = offer.Rarity.ToString() });
        }

        private void WriteDelivery(string kind, string detail, object payload = null) => audit?.WriteLine(JsonUtility.ToJson(new DeliveryRow {
            kind = kind, detail = detail, run = deliveryRun, round = deliveryRound, role = role,
            realtime = Time.realtimeSinceStartupAsDouble, combat = EnemySimulationClock.CombatNow,
            payload = payload == null ? null : payload is string text ? text : JsonUtility.ToJson(payload) }));

        private void FinishDeliveryObservation(string reason)
        {
            if (deliveryClosed) return;
            if (!deliveryEnded && deliveryRun != null) WriteDelivery("run-ended", "interrupted: " + reason, lastDeliveryProgress);
            InterruptSelectionObservation(reason);
            WriteDelivery("process-closed", reason + "; missing close record means incomplete, not completed");
            audit?.Flush(); deliveryClosed = true;
            if (activeObservation == this) activeObservation = null;
        }

        private void InterruptSelectionObservation(string reason)
        {
            if (!wasSelecting) return;
            WriteDelivery("selection-interrupted", reason, new DurationRow { seconds = Time.realtimeSinceStartupAsDouble - selectStarted });
            wasSelecting = false;
        }

        [Serializable] private class DeliveryRow { public string kind, detail, run, role, payload; public uint round; public double realtime, combat; }
        [Serializable] private class LaunchRow { public string[] arguments; public string version, session, detail, device, api, unity; }
        [Serializable] private class DurationRow { public double seconds; }
        [Serializable] private class PlayerRow { public uint player; public int health, maximum; public float speed, xpModifier; public PlayerBuildSnapshot build; }
        [Serializable] private class ChoiceRow { public uint player, content; public ulong eventId, offer; public int slot, level; public string stage, kind, rarity; }
        [Serializable] private class DisplayRow { public int width, height, targetFps, vsync; public string fullscreen; public GameOptionsData settings; }
        [Serializable] private class PerformanceRow { public WaveProgressSnapshot snapshot; public int sampledAlivePeak, frames, openWriters; public double meanFrameMs, logWriteMs, logFlushMs; public float maxFrameMs, p95FrameMs; public long logLines, managedBytes; }
    }
}
