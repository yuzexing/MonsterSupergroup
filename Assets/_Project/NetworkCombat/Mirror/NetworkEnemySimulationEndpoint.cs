using System;
using System.Collections.Generic;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class NetworkEnemySimulationEndpoint : NetworkBehaviour
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
        private double nextSnapshotTime;
        private uint batchSequence;
        private uint attackPresentationBatchSequence;
        private CombatantBehaviour combatant;

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
                    BatchSequence = attackPresentationBatchSequence,
                    Edges = edges
                });
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
                NetworkTime.time,
                snapshotBuffer);
            for (int offset = 0; offset < snapshotBuffer.Count;
                 offset += maximumSnapshotsPerBatch)
            {
                int count = Math.Min(
                    maximumSnapshotsPerBatch,
                    snapshotBuffer.Count - offset);
                var snapshots = new EnemySimulationSnapshot[count];
                snapshotBuffer.CopyTo(offset, snapshots, 0, count);
                batchSequence = NextSequence(batchSequence);
                CmdSubmitSnapshots(new EnemySimulationSnapshotBatch
                {
                    BatchSequence = batchSequence,
                    Snapshots = snapshots
                });
            }
        }

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
    }
}
