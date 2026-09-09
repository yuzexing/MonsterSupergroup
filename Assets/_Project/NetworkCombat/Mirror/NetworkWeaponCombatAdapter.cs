using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MirrorNetworkCombatBridge))]
    [RequireComponent(typeof(PlayerBuildRuntime))]
    public sealed partial class NetworkWeaponCombatAdapter : NetworkBehaviour
    {
        [SerializeField] private MirrorNetworkCombatBridge bridge;
        [SerializeField] private CombatRuntimeServiceProvider serviceProvider;
        [SerializeField] private PlayerBuildRuntime playerBuildRuntime;
        [SerializeField] private NetworkPlayerBootstrap playerBootstrap;
        [SerializeField] private PlayerMovement playerMovement;
        [SerializeField, Range(1, 32)]
        private int maximumPresentationEdgesPerBatch = 32;
        [SerializeField, Min(0.1f)] private float maximumReplayDelay = 2f;

        private readonly List<NetworkProjectilePresentationEdge>
            outgoingPresentations =
                new List<NetworkProjectilePresentationEdge>(64);
        private ProjectilePresentationReplica presentationReplica;
        private uint outgoingBatchSequence;
        private uint lastServerBatchSequence;
        private uint lastClientBatchSequence;
        private uint lastCooldownReportSequence;
        private readonly PlayerWeaponCooldownSnapshot[] serverCooldowns =
            new PlayerWeaponCooldownSnapshot[PlayerBuildRuntime.HandSlotCount];
        private readonly WeaponBehaviour[] serverCooldownWeapons = new WeaponBehaviour[PlayerBuildRuntime.HandSlotCount];
        private readonly WeaponBehaviour[] ownerBaselineWeapons = new WeaponBehaviour[PlayerBuildRuntime.HandSlotCount];

        public int SentPresentationCount { get; private set; }
        public int ReceivedPresentationCount { get; private set; }
        public int ReplicaSpawnCount { get; private set; }
        public int ReplicaTerminationCount { get; private set; }
        public int RejectedPresentationCount { get; private set; }
        public int AcceptedCooldownReportCount { get; private set; }
        public int RejectedAttackCount { get; private set; }
        public CombatRejectionReason LastAttackRejection { get; private set; }
        public int ReplicaActiveProjectileCount =>
            presentationReplica?.ActiveProjectileCount ?? 0;

        private void Awake()
        {
            if (bridge == null)
            {
                bridge = GetComponent<MirrorNetworkCombatBridge>();
            }

            if (serviceProvider == null)
            {
                serviceProvider = GetComponent<CombatRuntimeServiceProvider>();
            }

            if (serviceProvider == null)
            {
                serviceProvider = gameObject.AddComponent<
                    CombatRuntimeServiceProvider>();
            }

            if (playerBuildRuntime == null)
            {
                playerBuildRuntime = GetComponent<PlayerBuildRuntime>();
            }

            if (playerBootstrap == null)
            {
                playerBootstrap = GetComponent<NetworkPlayerBootstrap>();
            }

            if (playerMovement == null)
            {
                playerMovement = GetComponent<PlayerMovement>();
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            NetworkCombatWorld.Instance?.Gateway.Attacks.RegisterPlayer(netId);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            lastClientBatchSequence = 0u;
            lastClientMeleeBatchSequence = 0u;
            lastClientBeamSequence = 0u;
            lastClientBeamAimSequence = 0u;
            lastClientOrbitSequence = 0u;
            lastClientTrailSequence = 0u;
            if (!isOwned) CmdRequestSummonViews();
        }

        public override void OnStartAuthority()
        {
            base.OnStartAuthority();
            DisposeMeleePresentationReplica();
            SubscribeMeleePresentations();
            DisposeBeamPresentationReplica();
            SubscribeBeamPresentations();
            DisposeOrbitPresentationReplica();
            SubscribeOrbitPresentations();
            DisposeTrailPresentationReplica();
            SubscribeTrailPresentations();
            DisposeSummonPresentationReplica();
            bridge.OwnerCollectorReady -= HandleCollectorReady;
            bridge.OwnerCollectorReady += HandleCollectorReady;
            if (bridge.Collector != null)
            {
                HandleCollectorReady(bridge.Collector, bridge.EventIds);
            }

            if (playerBuildRuntime != null)
            {
                playerBuildRuntime.NativeAttackStarted -= HandleNativeAttackStarted;
                playerBuildRuntime.NativeAttackStarted += HandleNativeAttackStarted;
                playerBuildRuntime.NativeAttackCompleted -= HandleNativeAttackCompleted;
                playerBuildRuntime.NativeAttackCompleted += HandleNativeAttackCompleted;
                playerBuildRuntime.ProjectilePresentationSpawned -= HandlePresentationSpawned;
                playerBuildRuntime.ProjectilePresentationTerminated -= HandlePresentationTerminated;
                playerBuildRuntime.ProjectilePresentationSpawned +=
                    HandlePresentationSpawned;
                playerBuildRuntime.ProjectilePresentationTerminated +=
                    HandlePresentationTerminated;
            }
        }

        private void HandleNativeAttackStarted(int slotIndex, uint weaponId,
            MonsterSupergroup.GAS.CombatEventId eventId)
        {
            if (isOwned && NetworkClient.active)
            {
                uint revision = GetComponent<NetworkModifierSelection>().OwnerBuildRevision;
                if (playerBuildRuntime.GetWeaponAtSlot(slotIndex) is DashAttackBehaviour dashWeapon)
                    CmdObserveDashWeaponAttack(slotIndex, weaponId, eventId.Value, dashWeapon.CurrentDashUseId, revision);
                else
                    CmdObserveWeaponAttack(slotIndex, weaponId, eventId.Value, NetworkTime.time, revision);
            }
        }

        private void HandleNativeAttackCompleted(int slotIndex, uint weaponId,
            MonsterSupergroup.GAS.CombatEventId eventId)
        {
            if (!isOwned || !NetworkClient.active) return;
            // Reliable commands on this connection preserve this order: all outcomes first,
            // then release the admission record. A visual termination never decides damage.
            FlushPresentations();
            FlushMeleePresentations();
            FlushBeamPresentations();
            FlushOrbitPresentations();
            FlushTrailPresentations();
            // Drain uses bounded batches. A large area attack can leave more than one batch.
            while (isOwned && NetworkClient.active && bridge.Collector != null &&
                (bridge.Collector.PendingResultCount > 0 || bridge.Collector.PendingStatusMutationCount > 0 ||
                 bridge.Collector.PendingPlayerHealthReportCount > 0))
                bridge.Flush();
            CmdCompleteWeaponAttack(eventId.Value);
        }

        [Command(channel = Channels.Reliable)]
        private void CmdCompleteWeaponAttack(ulong rootEventId, NetworkConnectionToClient sender = null)
        {
            if (sender != null && sender == connectionToClient)
            {
                NetworkCombatWorld.Instance?.Gateway.Attacks.Retire(netId, rootEventId);
                serverMeleePresentationHistory.RetireAttack(rootEventId);
                serverBeamHistory.RetireAttack(rootEventId);
                serverOrbitHistory.RetireAttack(rootEventId);
                serverTrailHistory.RetireAttack(rootEventId);
                serverDashWeaponUses.Remove(rootEventId);
                serverSummonRootSlots.Remove(rootEventId);
            }
        }

        [Command(channel = Channels.Reliable)]
        private void CmdObserveWeaponAttack(int slotIndex, uint weaponId, ulong attackEventId,
            double ownerAttackTime, uint ownerBuildRevision, NetworkConnectionToClient sender = null)
        {
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            var eventId = new MonsterSupergroup.GAS.CombatEventId(attackEventId);
            if (sender == null || sender != connectionToClient || world == null ||
                (uint)slotIndex >= PlayerBuildRuntime.HandSlotCount ||
                eventId.Sequence <= lastCooldownReportSequence ||
                !world.Gateway.ClientIdentities.Validate(bridge.OwnerPlayerId, attackEventId, eventId.Sequence))
            { RejectAttack(CombatRejectionReason.InvalidSequence); return; }
            var selection = GetComponent<NetworkModifierSelection>();
            if (selection == null || ownerBuildRevision == 0 || ownerBuildRevision != selection.BuildRevision)
            { RejectAttack(CombatRejectionReason.StaleAttackBuild); return; }
            if (world.Gateway.Ledger.IsPlayerSelectingUpgrade(netId))
            { RejectAttack(CombatRejectionReason.SourceSelectingUpgrade); return; }
            if (!world.Gateway.Ledger.TryGetState(netId, out var playerState) || !playerState.Alive)
            { RejectAttack(CombatRejectionReason.TargetCanonicalDead); return; }
            RefreshServerCooldownWeapon(slotIndex);
            WeaponBehaviour weapon = serverCooldownWeapons[slotIndex];
            if (weapon == null || weapon is DashAttackBehaviour || weapon.WeaponData.ID != weaponId)
            { RejectAttack(CombatRejectionReason.SourceNotOwned); return; }
            if (weapon is SummonAttackBehaviour summon)
            {
                RefreshServerSummonWeapon(slotIndex);
                double readyAt = serverSummonMaturities[slotIndex].MaturityAt + summon.BirthPresentationDuration;
                if (!serverCooldowns[slotIndex].IsValid) readyAt += weapon.GetCooldown();
                if (ownerAttackTime + .05d < readyAt)
                { RejectAttack(CombatRejectionReason.InvalidAttackRate); return; }
            }
            if (!PlayerWeaponCooldownSnapshot.TryAdmit(slotIndex, weaponId, attackEventId,
                    ownerAttackTime, NetworkTime.time, weapon.GetCooldown(), serverCooldowns[slotIndex],
                    out var next, weapon.GetAttackSequenceDuration()))
            { RejectAttack(CombatRejectionReason.InvalidAttackRate); return; }
            CombatRejectionReason admitted = world.Gateway.Attacks.Admit(netId, bridge.SourceEntityId,
                weaponId, ownerBuildRevision, attackEventId);
            if (admitted != CombatRejectionReason.None) { RejectAttack(admitted); return; }
            serverCooldowns[slotIndex] = next;
            if (weapon is SummonAttackBehaviour) serverSummonRootSlots.Add(attackEventId, slotIndex);
            lastCooldownReportSequence = eventId.Sequence;
            AcceptedCooldownReportCount++;
        }

        [Command(channel = Channels.Reliable)]
        private void CmdObserveDashWeaponAttack(int slotIndex, uint weaponId, ulong attackEventId,
            ulong dashUseId, uint ownerBuildRevision, NetworkConnectionToClient sender = null)
        {
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            var eventId = new MonsterSupergroup.GAS.CombatEventId(attackEventId);
            if (sender == null || sender != connectionToClient || world == null ||
                (uint)slotIndex >= PlayerBuildRuntime.HandSlotCount ||
                !world.Gateway.ClientIdentities.Validate(bridge.OwnerPlayerId, attackEventId, eventId.Sequence))
            { RejectAttack(CombatRejectionReason.InvalidSequence); return; }
            var selection = GetComponent<NetworkModifierSelection>();
            if (selection == null || ownerBuildRevision == 0 || ownerBuildRevision != selection.BuildRevision)
            { RejectAttack(CombatRejectionReason.StaleAttackBuild); return; }
            if (world.Gateway.Ledger.IsPlayerSelectingUpgrade(netId))
            { RejectAttack(CombatRejectionReason.SourceSelectingUpgrade); return; }
            if (!world.Gateway.Ledger.TryGetState(netId, out var state) || !state.Alive)
            { RejectAttack(CombatRejectionReason.TargetCanonicalDead); return; }
            var dash = GetComponent<NetworkPlayerDash>();
            if (!(playerBuildRuntime.GetWeaponAtSlot(slotIndex) is DashAttackBehaviour weapon) ||
                weapon.WeaponData.ID != weaponId || dash == null ||
                !dash.CanAdmitWeapon(dashUseId, ownerBuildRevision, slotIndex, weaponId))
            { RejectAttack(CombatRejectionReason.InvalidAttackRate); return; }
            CombatRejectionReason admitted = world.Gateway.Attacks.Admit(netId, bridge.SourceEntityId,
                weaponId, ownerBuildRevision, attackEventId);
            if (admitted != CombatRejectionReason.None) { RejectAttack(admitted); return; }
            dash.MarkWeaponAdmitted(dashUseId, slotIndex);
            serverDashWeaponUses.Add(attackEventId, dashUseId);
        }

        private void RejectAttack(CombatRejectionReason reason)
        {
            LastAttackRejection = reason;
            RejectedAttackCount++;
            NetworkCombatWorld.Instance?.Gateway.Metrics.Reject(reason);
        }

        /// <summary>Copy server-observed gameplay timing before the avatar is destroyed.</summary>
        public PlayerWeaponCooldownSnapshot[] CaptureCooldowns()
        {
            var result = new List<PlayerWeaponCooldownSnapshot>();
            for (int slot = 0; slot < serverCooldowns.Length; slot++)
            {
                RefreshServerCooldownWeapon(slot);
                if (serverCooldowns[slot].IsValid) result.Add(serverCooldowns[slot]);
            }
            return result.ToArray();
        }

        public void PrepareServerRestore(PlayerWeaponCooldownSnapshot[] snapshots)
        {
            if (netIdentity != null && netId != 0)
                throw new InvalidOperationException("Cooldown restoration must be prepared before spawning the avatar.");
            ValidateCooldownSnapshots(snapshots);
            Array.Clear(serverCooldowns, 0, serverCooldowns.Length);
            Array.Clear(serverCooldownWeapons, 0, serverCooldownWeapons.Length);
            foreach (PlayerWeaponCooldownSnapshot snapshot in snapshots)
                serverCooldowns[snapshot.SlotIndex] = snapshot;
        }

        /// <summary>Called after Build reconciliation and before enabling owner execution, including Host.</summary>
        public void ApplyOwnerCooldownBaseline(PlayerWeaponCooldownSnapshot[] snapshots, double networkTime)
        {
            ValidateCooldownSnapshots(snapshots);
            if (double.IsNaN(networkTime) || double.IsInfinity(networkTime))
                throw new ArgumentOutOfRangeException(nameof(networkTime));
            for (int slot = 0; slot < ownerBaselineWeapons.Length; slot++)
            {
                WeaponBehaviour weapon = playerBuildRuntime.GetWeaponAtSlot(slot);
                if (ReferenceEquals(ownerBaselineWeapons[slot], weapon)) continue;
                if (weapon is SummonAttackBehaviour summon && !summon.HasSimulationBinding) continue;
                if (weapon != null)
                    foreach (PlayerWeaponCooldownSnapshot snapshot in snapshots)
                        if (snapshot.SlotIndex == slot && snapshot.WeaponId == weapon.WeaponData.ID)
                            weapon.RestoreCooldownRemaining((float)Math.Min(weapon.GetCooldown() + snapshot.SequenceSeconds,
                                snapshot.WithCooldown(weapon.GetCooldown()).RemainingAt(networkTime)));
                ownerBaselineWeapons[slot] = weapon;
            }
        }

        private void RefreshServerCooldownWeapon(int slot)
        {
            WeaponBehaviour weapon = playerBuildRuntime != null ? playerBuildRuntime.GetWeaponAtSlot(slot) : null;
            if (weapon == null || weapon is DashAttackBehaviour || (!ReferenceEquals(serverCooldownWeapons[slot], null) &&
                !ReferenceEquals(serverCooldownWeapons[slot], weapon)) ||
                (serverCooldowns[slot].WeaponId != 0 && serverCooldowns[slot].WeaponId != weapon.WeaponData.ID))
                serverCooldowns[slot] = default;
            else if (serverCooldowns[slot].IsValid)
                serverCooldowns[slot] = serverCooldowns[slot].WithCooldown(weapon.GetCooldown());
            serverCooldownWeapons[slot] = weapon;
        }

        private static void ValidateCooldownSnapshots(PlayerWeaponCooldownSnapshot[] snapshots)
        {
            if (snapshots == null) throw new ArgumentNullException(nameof(snapshots));
            var occupied = new bool[PlayerBuildRuntime.HandSlotCount];
            foreach (PlayerWeaponCooldownSnapshot snapshot in snapshots)
            {
                if (!snapshot.IsValid || occupied[snapshot.SlotIndex])
                    throw new ArgumentException("Invalid or duplicate saved weapon cooldown.", nameof(snapshots));
                occupied[snapshot.SlotIndex] = true;
            }
        }

        [ClientCallback]
        private void Update()
        {
            if (isOwned)
            {
                FlushPresentations();
                FlushMeleePresentations();
                FlushBeamPresentations();
                FlushBeamAims();
                FlushOrbitPresentations();
                FlushTrailPresentations();
                FlushSummonPoses();
            }
            else
            {
                beamPresentationReplica?.Tick(Time.deltaTime);
                orbitPresentationReplica?.Tick(Time.deltaTime);
                summonPresentationReplica?.Tick(Time.deltaTime);
            }
        }

        private void HandlePresentationSpawned(
            ProjectilePresentationSpawn spawn)
        {
            outgoingPresentations.Add(new NetworkProjectilePresentationEdge
            {
                SourcePlayerId = netId,
                WeaponId = spawn.WeaponId,
                AttackEventId = spawn.Key.AttackEventId,
                ProjectileIndex = spawn.Key.ProjectileIndex,
                EventNetworkTime = NetworkTime.time,
                Phase = ProjectilePresentationPhase.Spawn,
                Position = spawn.Position,
                Direction = spawn.Direction,
                Element = spawn.Element,
                RotateToMovement = spawn.RotateToMovement,
                Stats = spawn.Stats
            });
        }

        private void HandlePresentationTerminated(
            ProjectilePresentationTermination termination)
        {
            outgoingPresentations.Add(new NetworkProjectilePresentationEdge
            {
                SourcePlayerId = netId,
                WeaponId = termination.WeaponId,
                AttackEventId = termination.Key.AttackEventId,
                ProjectileIndex = termination.Key.ProjectileIndex,
                EventNetworkTime = NetworkTime.time,
                Phase = termination.Phase,
                Position = termination.Position
            });
        }

        private void FlushPresentations()
        {
            if (outgoingPresentations.Count == 0)
            {
                return;
            }

            for (int offset = 0; offset < outgoingPresentations.Count;
                 offset += maximumPresentationEdgesPerBatch)
            {
                int count = Math.Min(
                    maximumPresentationEdgesPerBatch,
                    outgoingPresentations.Count - offset);
                var edges = new NetworkProjectilePresentationEdge[count];
                outgoingPresentations.CopyTo(offset, edges, 0, count);
                outgoingBatchSequence = NextSequence(outgoingBatchSequence);
                CmdSubmitProjectilePresentations(
                    new NetworkProjectilePresentationBatch
                    {
                        BatchSequence = outgoingBatchSequence,
                        Edges = edges
                    });
                SentPresentationCount += count;
            }

            outgoingPresentations.Clear();
        }

        [Command(channel = Channels.Reliable)]
        private void CmdSubmitProjectilePresentations(
            NetworkProjectilePresentationBatch batch,
            NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender != connectionToClient ||
                !IsValidPresentationBatch(
                    batch,
                    netId,
                    lastServerBatchSequence,
                    maximumPresentationEdgesPerBatch))
            {
                RejectedPresentationCount += batch.Edges?.Length ?? 1;
                return;
            }

            lastServerBatchSequence = batch.BatchSequence;
            var admittedEdges = new List<NetworkProjectilePresentationEdge>();
            NetworkCombatWorld world = NetworkCombatWorld.Instance;
            foreach (NetworkProjectilePresentationEdge edge in batch.Edges)
            {
                if (edge.Phase == ProjectilePresentationPhase.Spawn &&
                    (world == null || !world.Gateway.Attacks.Contains(netId, edge.AttackEventId, edge.WeaponId)))
                    RejectedPresentationCount++;
                else
                    admittedEdges.Add(edge);
            }
            if (admittedEdges.Count == 0) return;
            batch.Edges = admittedEdges.ToArray();
            if (NetworkCombatWorld.Instance != null &&
                NetworkCombatWorld.Instance.Gateway.Ledger.IsPlayerSelectingUpgrade(netId))
            {
                var terminations = new List<NetworkProjectilePresentationEdge>();
                foreach (NetworkProjectilePresentationEdge edge in batch.Edges)
                {
                    if (edge.Phase == ProjectilePresentationPhase.Spawn)
                        RejectedPresentationCount++;
                    else
                        terminations.Add(edge);
                }
                if (terminations.Count == 0) return;
                batch.Edges = terminations.ToArray();
            }
            RpcApplyProjectilePresentations(batch);
        }

        [ClientRpc(channel = Channels.Reliable)]
        private void RpcApplyProjectilePresentations(
            NetworkProjectilePresentationBatch batch)
        {
            if (isOwned)
            {
                return;
            }

            ApplyRemotePresentations(batch, NetworkTime.time);
        }

        private void ApplyRemotePresentations(
            NetworkProjectilePresentationBatch batch,
            double currentNetworkTime)
        {
            if (!IsValidPresentationBatch(
                    batch,
                    netId,
                    lastClientBatchSequence,
                    maximumPresentationEdgesPerBatch))
            {
                RejectedPresentationCount += batch.Edges?.Length ?? 1;
                return;
            }

            lastClientBatchSequence = batch.BatchSequence;
            RuntimeDB database = playerBootstrap != null
                ? playerBootstrap.ResolveSharedRuntimeDatabase()
                : null;
            if (database == null || playerMovement == null)
            {
                RejectedPresentationCount += batch.Edges.Length;
                return;
            }

            presentationReplica ??=
                new ProjectilePresentationReplica(playerMovement, database);
            for (int i = 0; i < batch.Edges.Length; i++)
            {
                NetworkProjectilePresentationEdge edge = batch.Edges[i];
                ReceivedPresentationCount++;
                if (edge.Phase == ProjectilePresentationPhase.Spawn)
                {
                    double elapsed = Math.Max(
                        0d,
                        currentNetworkTime - edge.EventNetworkTime);
                    if (elapsed > maximumReplayDelay ||
                        !presentationReplica.TrySpawn(
                            edge.ToSpawn(),
                            (float)elapsed))
                    {
                        RejectedPresentationCount++;
                        continue;
                    }

                    ReplicaSpawnCount++;
                    continue;
                }

                if (presentationReplica.TryTerminate(edge.ToTermination()))
                {
                    ReplicaTerminationCount++;
                }
            }
        }

        public static bool IsValidPresentationBatch(
            NetworkProjectilePresentationBatch batch,
            uint expectedSourcePlayerId,
            uint previousBatchSequence,
            int maximumEdges)
        {
            if (expectedSourcePlayerId == 0u || maximumEdges <= 0 ||
                batch.Edges == null || batch.Edges.Length == 0 ||
                batch.Edges.Length > maximumEdges ||
                !ProjectilePresentationSequence.IsNewer(
                    batch.BatchSequence,
                    previousBatchSequence))
            {
                return false;
            }

            for (int i = 0; i < batch.Edges.Length; i++)
            {
                if (!batch.Edges[i].IsValid ||
                    batch.Edges[i].SourcePlayerId != expectedSourcePlayerId)
                {
                    return false;
                }
            }

            return true;
        }

        public override void OnStopAuthority()
        {
            DetachOwnerCallbacks();
            base.OnStopAuthority();
        }

        private void DetachOwnerCallbacks()
        {
            UnbindOwnerSummons();
            Array.Clear(ownerBaselineWeapons, 0, ownerBaselineWeapons.Length);
            UnsubscribeMeleePresentations();
            UnsubscribeBeamPresentations();
            UnsubscribeOrbitPresentations();
            UnsubscribeTrailPresentations();
            if (bridge != null)
            {
                bridge.OwnerCollectorReady -= HandleCollectorReady;
            }

            if (playerBuildRuntime != null)
            {
                playerBuildRuntime.NativeAttackStarted -= HandleNativeAttackStarted;
                playerBuildRuntime.NativeAttackCompleted -= HandleNativeAttackCompleted;
                playerBuildRuntime.ProjectilePresentationSpawned -=
                    HandlePresentationSpawned;
                playerBuildRuntime.ProjectilePresentationTerminated -=
                    HandlePresentationTerminated;
            }

            outgoingPresentations.Clear();
        }

        public override void OnStopClient()
        {
            OnStopAuthority();
            Array.Clear(ownerBaselineWeapons, 0, ownerBaselineWeapons.Length);
            presentationReplica?.Dispose();
            presentationReplica = null;
            DisposeMeleePresentationReplica();
            DisposeBeamPresentationReplica();
            DisposeOrbitPresentationReplica();
            base.OnStopClient();
            DisposeTrailPresentationReplica();
            DisposeSummonPresentationReplica();
        }

        public override void OnStopServer()
        {
            ClearServerSummons();
            NetworkCombatWorld.Instance?.Gateway.Attacks.UnregisterPlayer(netId);
            Array.Clear(serverCooldowns, 0, serverCooldowns.Length);
            Array.Clear(serverCooldownWeapons, 0, serverCooldownWeapons.Length);
            lastCooldownReportSequence = 0;
            lastServerMeleeBatchSequence = 0u;
            serverMeleePresentationHistory.Clear();
            lastServerBeamSequence = 0u;
            lastServerBeamAimSequence = 0u;
            serverBeamHistory.Clear();
            lastServerOrbitSequence = 0u;
            serverOrbitHistory.Clear();
            lastServerTrailSequence = 0;
            serverTrailHistory.Clear();
            serverDashWeaponUses.Clear();
            base.OnStopServer();
        }

        private void HandleCollectorReady(
            ClientCombatCollector collector,
            MonsterSupergroup.GAS.ICombatEventIdSource eventIds)
        {
            var services = new CombatRuntimeServices(
                bridge.OwnerPlayerId,
                bridge.SourceEntityId,
                eventIds,
                collector);
            serviceProvider.Configure(services);
        }

        private static uint NextSequence(uint value)
        {
            value = unchecked(value + 1u);
            return value == 0u ? 1u : value;
        }
    }
}
