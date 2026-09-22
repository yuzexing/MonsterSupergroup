using System;
using System.Collections.Generic;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed partial class NetworkEnemySimulationEndpoint : NetworkBehaviour
    {
        [SerializeField, Min(0.01f)] private float snapshotInterval = 0.05f;
        [SerializeField, Range(1, 32)] private int maximumSnapshotsPerBatch = 20;
        [SerializeField, Range(1, 64)]
        private int maximumAttackPresentationEdgesPerBatch = 32;

        private readonly List<EnemySimulationSnapshot> snapshotBuffer =
            new List<EnemySimulationSnapshot>(128);
        private readonly List<EnemyAttackPresentationEdge>
            attackPresentationBuffer =
                new List<EnemyAttackPresentationEdge>(64);
        private readonly List<EnemyProjectileLaunch> projectileLaunchBuffer = new List<EnemyProjectileLaunch>();
        private readonly List<EnemyProjectileTermination> projectileTerminationBuffer = new List<EnemyProjectileTermination>();
        private double nextSnapshotTime;
        private uint batchSequence;
        private uint attackPresentationBatchSequence;
        private CombatantBehaviour combatant;
        private readonly List<uint> readyFallbackEnemies = new List<uint>();
        private double nextReadyReport;

        public uint PlayerEntityId => netId;

        public bool IsEligibleSimulationOwner =>
            combatant == null || combatant.IsAlive;

        private void Awake()
        {
            combatant = GetComponent<CombatantBehaviour>();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            serverView = default;
            NetworkEnemySimulationWorld.Instance?.RegisterPlayer(this);
        }

        public override void OnStartAuthority()
        {
            base.OnStartAuthority();
            nextSnapshotTime = NetworkTime.time + snapshotInterval;
        }

        public override void OnStopServer()
        {
            NetworkEnemySimulationWorld.Instance?.UnregisterPlayer(this);
            base.OnStopServer();
        }

        [ClientCallback]
        private void Update()
        {
            if (!isOwned)
            {
                return;
            }

            FlushAttackPresentations();
            ReportLocalView();
            if (NetworkTime.time >= nextReadyReport)
            {
                nextReadyReport = NetworkTime.time + .25;
                NetworkEnemySimulationWorld.Instance?.CollectReadyFallbackEnemies(netId, readyFallbackEnemies);
                if (readyFallbackEnemies.Count > 0) CmdReportSimulationReady(readyFallbackEnemies.GetRange(0, Math.Min(256, readyFallbackEnemies.Count)).ToArray());
            }
            if (NetworkTime.time < nextSnapshotTime)
            {
                return;
            }
            nextSnapshotTime = NetworkTime.time + snapshotInterval;
            FlushSnapshots();
        }

        private void FlushAttackPresentations()
        {
            NetworkEnemySimulationWorld world = NetworkEnemySimulationWorld.Instance;
            if (world == null)
            {
                return;
            }

            attackPresentationBuffer.Clear();
            FlushProjectileMessages(world);
            world.CollectClientOwnedAttackPresentations(
                PlayerEntityId,
                attackPresentationBuffer);
            for (int offset = 0; offset < attackPresentationBuffer.Count;
                 offset += maximumAttackPresentationEdgesPerBatch)
            {
                int count = Math.Min(
                    maximumAttackPresentationEdgesPerBatch,
                    attackPresentationBuffer.Count - offset);
                var edges = new EnemyAttackPresentationEdge[count];
                attackPresentationBuffer.CopyTo(offset, edges, 0, count);
                attackPresentationBatchSequence =
                    NextSequence(attackPresentationBatchSequence);
                CmdSubmitAttackPresentations(new EnemyAttackPresentationBatch
                {
                    Round = NetworkEnemySimulationWorld.CurrentRound,
                    BatchSequence = attackPresentationBatchSequence,
                    Edges = edges
                });
            }
        }

        private void FlushProjectileMessages(NetworkEnemySimulationWorld world)
        {
            world.CollectClientProjectileLaunches(PlayerEntityId, projectileLaunchBuffer);
            world.CollectClientProjectileTerminations(projectileTerminationBuffer);
            int maximum = Math.Max(projectileLaunchBuffer.Count, projectileTerminationBuffer.Count);
            for (int offset = 0; offset < maximum; offset += maximumAttackPresentationEdgesPerBatch)
            {
                var launches = projectileLaunchBuffer.GetRange(Math.Min(offset, projectileLaunchBuffer.Count), Math.Min(maximumAttackPresentationEdgesPerBatch, Math.Max(0, projectileLaunchBuffer.Count - offset))).ToArray();
                var terminals = projectileTerminationBuffer.GetRange(Math.Min(offset, projectileTerminationBuffer.Count), Math.Min(maximumAttackPresentationEdgesPerBatch, Math.Max(0, projectileTerminationBuffer.Count - offset))).ToArray();
                CmdSubmitAttackPresentations(new EnemyAttackPresentationBatch { Round = NetworkEnemySimulationWorld.CurrentRound, BatchSequence = attackPresentationBatchSequence = NextSequence(attackPresentationBatchSequence), ProjectileLaunches = launches, ProjectileTerminations = terminals });
            }
        }

        private void FlushSnapshots()
        {
            NetworkEnemySimulationWorld world = NetworkEnemySimulationWorld.Instance;
            if (world == null)
            {
                return;
            }

            snapshotBuffer.Clear();
            world.CollectClientOwnedSnapshots(
                PlayerEntityId,
                EnemySimulationClock.Now,
                snapshotBuffer);
            EnemySimulationWire.SendBatches(snapshotBuffer, maximumSnapshotsPerBatch, (batch, reliable) =>
            {
                batch.Round = NetworkEnemySimulationWorld.CurrentRound;
                batch.BatchSequence = batchSequence = NextSequence(batchSequence);
                if (MonsterSupergroup.GAS.CombatEvidence.Enabled) MonsterSupergroup.GAS.CombatEvidence.Event("Owner", "movement.submit", "Sent", reliable ? "ReliableFallback" : "Unreliable",
                    source: PlayerEntityId, input: batch, batch: batch.BatchSequence, bytes: batch.Snapshots.Length * 4096 + 2048);
                if (reliable) CmdSubmitLargeSnapshot(batch); else CmdSubmitSnapshots(batch);
            });
        }

        [Command(channel = Channels.Reliable)]
        private void CmdSubmitLargeSnapshot(EnemySimulationSnapshotBatch batch) =>
            NetworkEnemySimulationWorld.Instance?.SubmitClientSnapshots(this, batch);

        [Command(channel = Channels.Unreliable)]
        private void CmdSubmitSnapshots(
            EnemySimulationSnapshotBatch batch,
            NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender != connectionToClient)
            {
                return;
            }

            NetworkEnemySimulationWorld.Instance?.SubmitClientSnapshots(
                this,
                batch);
        }

        [Command(channel = Channels.Reliable)]
        private void CmdSubmitAttackPresentations(
            EnemyAttackPresentationBatch batch,
            NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender != connectionToClient)
            {
                return;
            }

            NetworkEnemySimulationWorld.Instance?.SubmitClientAttackPresentations(
                this,
                batch);
        }

        private static uint NextSequence(uint value)
        {
            value = unchecked(value + 1u);
            return value == 0u ? 1u : value;
        }

        [Command(channel = Channels.Reliable)]
        private void CmdReportSimulationReady(uint[] ids) => NetworkEnemySimulationWorld.Instance?.ReportSimulationReady(this, ids);

        [Command(channel = Channels.Reliable)]
        internal void CmdSubmitRuntimeCheckpoint(EnemySimulationCheckpoint checkpoint) =>
            NetworkEnemySimulationWorld.Instance?.SubmitRuntimeCheckpoint(this, checkpoint);

        [TargetRpc(channel = Channels.Reliable)]
        internal void TargetApplyKnockback(NetworkConnectionToClient target, EnemyKnockbackCommand command)
        {
            if (isOwned)
                NetworkEnemySimulationWorld.Instance?.ReceiveKnockback(command, PlayerEntityId);
        }
    }
}
