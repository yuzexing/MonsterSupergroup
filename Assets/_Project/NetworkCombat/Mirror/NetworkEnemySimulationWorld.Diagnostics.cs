using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [Serializable]
    public sealed class EnemyMotionDiagnosticSample
    {
        public int objects, localSimulators, replicas, frozen, localDeadServerAlive, confirmedCorpses, finishedDeathsStillVisible;
        public int deathCleanupTimeouts, omittedCorrections;
        public int[] handoffsByReason;
        public EnemyMotionCorrection[] corrections;
    }

    [Serializable]
    public struct EnemyMotionCorrection
    {
        public uint enemy, epoch, simulator;
        public string reason;
        public bool wasLocalSimulator, isLocalSimulator;
        public double time, checkpointAge;
        public float distance;
        public Vector2 from, to;
    }

    public sealed partial class NetworkEnemySimulationWorld
    {
        private readonly int[] diagnosticHandoffs = new int[(int)EnemyTargetChangeReason.ReferenceReposition + 1];
        private readonly List<EnemyMotionCorrection> diagnosticCorrections = new(32);
        private int diagnosticOmittedCorrections, diagnosticDeathTimeouts;

        internal void RecordMotionCorrection(NetworkEnemySimulationAgent enemy, EnemySimulationHandoff handoff,
            bool wasLocal, Vector2 from)
        {
            if (!NetworkDiagnosticsObservation.Enabled) return;
            int reason = (int)handoff.Reason;
            if (reason < diagnosticHandoffs.Length) diagnosticHandoffs[reason]++;
            if (handoff.Reason == EnemyTargetChangeReason.Spawn) return;
            if (diagnosticCorrections.Count == 32) { diagnosticOmittedCorrections++; return; }
            diagnosticCorrections.Add(new EnemyMotionCorrection {
                enemy = enemy.netId, epoch = handoff.Assignment.Epoch, simulator = handoff.Assignment.SimulationOwnerPlayerId,
                reason = handoff.Reason.ToString(), time = EnemySimulationClock.Now,
                checkpointAge = Math.Max(0, EnemySimulationClock.Now - handoff.Checkpoint.Movement.SampleNetworkTime),
                wasLocalSimulator = wasLocal, isLocalSimulator = enemy.Authority.RunsNavigation,
                from = from, to = enemy.transform.position, distance = Vector2.Distance(from, enemy.transform.position) });
        }

        internal void RecordDeathCleanupTimeout() { if (NetworkDiagnosticsObservation.Enabled) diagnosticDeathTimeouts++; }

        public EnemyMotionDiagnosticSample CaptureMotionDiagnostics()
        {
            var sample = new EnemyMotionDiagnosticSample {
                handoffsByReason = (int[])diagnosticHandoffs.Clone(), corrections = diagnosticCorrections.ToArray(),
                omittedCorrections = diagnosticOmittedCorrections, deathCleanupTimeouts = diagnosticDeathTimeouts };
            diagnosticCorrections.Clear(); diagnosticOmittedCorrections = 0;
            foreach (var enemy in enemies.Values)
            {
                if (enemy == null) continue;
                sample.objects++;
                if (!enemy.IsLocallyAlive && enemy.IsCanonicalAlive) sample.localDeadServerAlive++;
                if (!enemy.IsCanonicalAlive) sample.confirmedCorpses++;
                if (enemy.IsLocallyAlive && enemy.Authority.RunsNavigation) sample.localSimulators++;
                if (enemy.Authority.ConsumesSnapshots) sample.replicas++;
                if (enemy.Authority.Role == EnemySimulationRole.Frozen) sample.frozen++;
                var controller = enemy.GetComponent<EnemyController>();
                if (controller == null || !controller.DeathPresentationComplete || controller.enemyAnimator == null) continue;
                foreach (var renderer in controller.enemyAnimator.Renderers)
                    if (renderer != null && renderer.enabled && !renderer.forceRenderingOff && renderer.gameObject.activeInHierarchy)
                    { sample.finishedDeathsStillVisible++; break; }
            }
            return sample;
        }
    }
}
