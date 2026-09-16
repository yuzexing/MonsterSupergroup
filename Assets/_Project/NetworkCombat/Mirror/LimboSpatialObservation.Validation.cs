using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.Combat.Traps;
using AstralShift.Managers;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class LimboSpatialObservation
    {
        private readonly HashSet<string> validationSteps = new HashSet<string>();
        private readonly Dictionary<string, double> phaseFirstSeen = new Dictionary<string, double>();
        private bool validationWaiting;
        private PauseManager validationPause;
        private string ValidationCase => LimboReferenceLaunch.Argument("--limbo-spatial-case=") ?? "observe";
        private void ResetValidation() { validationSteps.Clear(); phaseFirstSeen.Clear(); validationWaiting = false; }

        // Explicit fixture actions only. The ordinary spatial observer remains passive.
        private void TickValidation(NetworkEnemySimulationWorld world, WaveProgressSnapshot progress)
        {
            if (!LimboReferenceLaunch.Profile.StartsWith("spatial-", StringComparison.Ordinal) || !NetworkServer.active || ValidationCase == "observe" || validationWaiting || completed ||
                BootGameplayNetworkManager.CombatHasEnded || progress.Elapsed < .1) return;
            string phase = world.ReferenceTraps.Count == 0 ? (progress.Elapsed >= 1.25 && progress.Elapsed < 2 ? "Delay" : "") :
                world.ReferenceTraps.Values.First().Phase.ToString();
            if (string.IsNullOrEmpty(phase) || validationSteps.Contains(phase)) return;
            if (!phaseFirstSeen.TryGetValue(phase, out var first)) phaseFirstSeen[phase] = first = EnemySimulationClock.CombatNow;
            // Let the phase produce an actual rendered state before freezing it.
            if (phase != "Delay" && EnemySimulationClock.CombatNow - first < .12) return;
            bool cancel = ValidationCase == "cancel-" + phase;
            if (!cancel && ValidationCase != "pause-all") return;
            validationSteps.Add(phase);
            StartCoroutine(ValidatePauseOrCancel(phase, cancel));
        }

        private IEnumerator ValidatePauseOrCancel(string phase, bool cancel)
        {
            validationWaiting = true;
            validationPause = PauseManager.Instance;
            if (validationPause == null) { validationWaiting = false; yield break; }
            validationPause.PauseGame();
            yield return null; // Drain a wait already eligible in the initiating frame.
            var world = NetworkEnemySimulationWorld.Instance;
            double before = EnemySimulationClock.CombatNow;
            var states = CaptureValidationStates(world);
            LogValidation("pause-start", phase, before, states, true);
            if (cancel)
            {
                // The Client must receive the paused phase before its removal. This is
                // fixture timing, not a delay added to production cancellation.
                yield return new WaitForSecondsRealtime(.6f);
                foreach (var spawner in FindObjectsByType<NetworkGameplayEnemySpawner>(FindObjectsSortMode.None)) spawner.StopWaveRun();
                (NetworkManager.singleton as BootGameplayNetworkManager)?.CompleteReferenceStage("Explicit spatial cancellation fixture: " + phase);
                LogValidation("cancel-paused", phase, before, world.ReferenceTraps.Values.ToArray(), Time.timeScale == 0);
            }
            yield return new WaitForSecondsRealtime(1.2f);
            bool unchanged = Math.Abs(EnemySimulationClock.CombatNow - before) < .001 &&
                (cancel ? world.ReferenceTraps.Count == 0 : states.SequenceEqual(CaptureValidationStates(world)));
            LogValidation("pause-end", phase, before, CaptureValidationStates(world), unchanged);
            if (!unchanged) Debug.LogError("[LimboSpatialFixture] paused state changed: " + phase);
            if (validationPause != null) validationPause.ResumeGame();
            validationPause = null;
            LogValidation("resume", phase, before, world.ReferenceTraps.Values.ToArray(), true);
            validationWaiting = false;
        }

        private static ReferenceTrapSnapshot[] CaptureValidationStates(NetworkEnemySimulationWorld world) =>
            world.ReferenceTraps.OrderBy(pair => pair.Key).Select(pair =>
                world.TryGetReferenceBarrier(pair.Key, out var barrier)
                    ? CaptureBarrierState(pair.Value, barrier) : pair.Value).ToArray();

        internal static ReferenceTrapSnapshot CaptureBarrierState(ReferenceTrapSnapshot published, BarrierTrap barrier)
        {
            // The SyncDictionary publishes at 20 Hz using communication time. A
            // pre-pause change may arrive after the clock has stopped. Measure the
            // native object, and retain the published value separately for audit.
            published.Phase = barrier.NetworkPhase;
            published.Collision = barrier.NetworkCollisionEnabled;
            if (published.Phase != BarrierPhase.Framing) published.Radius = barrier.NetworkInnerRadius;
            published.Count = barrier.NetworkGroupCount;
            published.Visible = barrier.NetworkVisibleGroups;
            return published;
        }

        [Serializable] private class ValidationRecord
        {
            public string kind, phase, run, role, fixture;
            public double combat, before, realtime;
            public float scale;
            public bool passed;
            public ReferenceTrapSnapshot[] states;
            public ReferenceTrapSnapshot[] publishedStates;
        }
        private void LogValidation(string kind, string phase, double before, ReferenceTrapSnapshot[] states, bool passed)
        {
            var value = new ValidationRecord { kind = kind, phase = phase, run = run,
                role = LimboReferenceLaunch.Argument("--limbo-role="), fixture = ValidationCase,
                combat = EnemySimulationClock.CombatNow, before = before, realtime = Time.realtimeSinceStartupAsDouble,
                scale = Time.timeScale, passed = passed, states = states,
                publishedStates = NetworkEnemySimulationWorld.Instance.ReferenceTraps.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray() };
            File.AppendAllText(Path.Combine(LimboReferenceLaunch.OutputDirectory, "spatial-actions.jsonl"), JsonUtility.ToJson(value) + "\n");
            ScreenCapture.CaptureScreenshot(Path.Combine(LimboReferenceLaunch.OutputDirectory, $"fixture-{run}-{phase}-{kind}.png"));
        }
    }
}
