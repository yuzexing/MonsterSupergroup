using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkWeaponCombatAdapter
    {
        private readonly PlayerSummonMaturitySnapshot[] serverSummonMaturities = new PlayerSummonMaturitySnapshot[PlayerBuildRuntime.HandSlotCount];
        private readonly SummonAttackBehaviour[] serverSummonWeapons = new SummonAttackBehaviour[PlayerBuildRuntime.HandSlotCount];
        private readonly SummonBinding[] ownerSummons = new SummonBinding[PlayerBuildRuntime.HandSlotCount];
        private readonly Dictionary<ulong, int> serverSummonRootSlots = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, NetworkSummonPresentationPose> outgoingSummonPoses = new Dictionary<ulong, NetworkSummonPresentationPose>();
        private readonly SummonPresentationHistory serverSummonHistory = new SummonPresentationHistory();
        private readonly SummonPresentationHistory clientSummonHistory = new SummonPresentationHistory();
        private SummonPresentationReplica summonPresentationReplica;
        private double nextSummonPoseTime;
        public int SentSummonStateCount { get; private set; }
        public int ReceivedSummonStateCount { get; private set; }
        public int ReplicaSummonPoseCount { get; private set; }
        public int ReplicaSummonTerminationCount { get; private set; }
        public int RejectedSummonPresentationCount { get; private set; }
        public int DroppedSummonPoseCount { get; private set; }
        public int ReplicaActiveSummonCount => summonPresentationReplica?.ActivePetCount ?? 0;

        // The absolute deadline belongs to the session checkpoint. No pet or AI runs on a server-only peer.
        public PlayerSummonMaturitySnapshot[] CaptureSummonMaturities()
        {
            var result = new List<PlayerSummonMaturitySnapshot>();
            for (int slot = 0; slot < serverSummonMaturities.Length; slot++)
            {
                RefreshServerSummonWeapon(slot);
                if (serverSummonMaturities[slot].IsValid) result.Add(serverSummonMaturities[slot]);
            }
            return result.ToArray();
        }

        public void PrepareServerSummonRestore(PlayerSummonMaturitySnapshot[] snapshots)
        {
            if (netIdentity != null && netId != 0)
                throw new InvalidOperationException("Summon restoration must be prepared before spawning the avatar.");
            ValidateSummonMaturities(snapshots);
            Array.Clear(serverSummonMaturities, 0, serverSummonMaturities.Length);
            Array.Clear(serverSummonWeapons, 0, serverSummonWeapons.Length);
            foreach (var snapshot in snapshots) serverSummonMaturities[snapshot.SlotIndex] = snapshot;
        }

        private void RefreshServerSummonWeapon(int slot)
        {
            var weapon = playerBuildRuntime != null ? playerBuildRuntime.GetWeaponAtSlot(slot) as SummonAttackBehaviour : null;
            if (weapon == null) serverSummonMaturities[slot] = default;
            else if (!serverSummonMaturities[slot].IsValid || serverSummonMaturities[slot].WeaponId != weapon.ID ||
                (!ReferenceEquals(serverSummonWeapons[slot], null) && !ReferenceEquals(serverSummonWeapons[slot], weapon)))
                serverSummonMaturities[slot] = new PlayerSummonMaturitySnapshot
                { SlotIndex = slot, WeaponId = weapon.ID, MaturityAt = NetworkTime.time + weapon.InitialMaturityDelay };
            serverSummonWeapons[slot] = weapon;
        }

        // Called before cooldown restoration and before the reconciled owner Build may execute.
        public void ApplyOwnerSummonBaseline(PlayerSummonMaturitySnapshot[] snapshots)
        {
            ValidateSummonMaturities(snapshots);
            if (!isActiveAndEnabled) return;
            for (int slot = 0; slot < ownerSummons.Length; slot++)
            {
                var weapon = playerBuildRuntime.GetWeaponAtSlot(slot) as SummonAttackBehaviour;
                if (ownerSummons[slot] != null && !ReferenceEquals(ownerSummons[slot].Weapon, weapon)) ReleaseOwnerSummon(slot);
                if (weapon == null || ownerSummons[slot] != null) continue;
                PlayerSummonMaturitySnapshot? matching = null;
                foreach (var snapshot in snapshots)
                    if (snapshot.SlotIndex == slot && snapshot.WeaponId == weapon.ID) matching = snapshot;
                if (!matching.HasValue) throw new InvalidOperationException($"Summon slot {slot} has no authoritative maturity baseline.");
                int boundSlot = slot;
                var binding = new SummonBinding { Weapon = weapon };
                binding.State = state => HandleSummonState(boundSlot, state);
                binding.Pose = HandleSummonPose;
                binding.Termination = HandleSummonTermination;
                ownerSummons[slot] = binding;
                weapon.PresentationStateChanged += binding.State;
                weapon.PresentationPoseChanged += binding.Pose;
                weapon.PresentationTerminated += binding.Termination;
                weapon.ConfigureSimulation(() => NetworkTime.time, () => bridge.EventIds?.Next().Value ?? 0,
                    QuerySummonTargets, matching.Value.MaturityAt);
            }
        }

        private static void ValidateSummonMaturities(PlayerSummonMaturitySnapshot[] snapshots)
        {
            if (snapshots == null) throw new ArgumentNullException(nameof(snapshots));
            var occupied = new bool[PlayerBuildRuntime.HandSlotCount];
            foreach (var snapshot in snapshots)
            {
                if (!snapshot.IsValid || occupied[snapshot.SlotIndex]) throw new ArgumentException("Invalid or duplicate summon maturity.", nameof(snapshots));
                occupied[snapshot.SlotIndex] = true;
            }
        }

        private static void QuerySummonTargets(Vector2 position, float minimum, float maximum, List<SummonTarget> result)
        {
            result.Clear();
            foreach (var team in CombatTeamBehaviour.ActiveTeams)
            {
                if (team == null || team.Team != CombatTeam.Enemy || team.Combatant == null || !team.Combatant.IsAlive) continue;
                var simulation = team.GetComponent<NetworkEnemySimulationAgent>();
                if (simulation == null) continue;
                var controller = team.GetComponent<EnemyController>();
                Transform hurtBox = controller != null && controller.hurtBox != null ? controller.hurtBox.transform : team.transform;
                float distance = ((Vector2)hurtBox.position - position).sqrMagnitude;
                if (distance >= minimum * minimum && distance <= maximum * maximum)
                    result.Add(new SummonTarget(hurtBox, simulation.SimulationMode == EnemySimulationMode.BossServer));
            }
        }

        private void ReleaseOwnerSummon(int slot)
        {
            SummonBinding binding = ownerSummons[slot];
            ownerSummons[slot] = null;
            if (binding == null || binding.Weapon == null) return;
            // Cancellation emits the final public edge before detaching delegates.
            binding.Weapon.UnbindSimulation();
            binding.Weapon.PresentationStateChanged -= binding.State;
            binding.Weapon.PresentationPoseChanged -= binding.Pose;
            binding.Weapon.PresentationTerminated -= binding.Termination;
        }

        private void UnbindOwnerSummons()
        {
            for (int slot = 0; slot < ownerSummons.Length; slot++) ReleaseOwnerSummon(slot);
            outgoingSummonPoses.Clear();
        }

        private void OnDisable()
        {
            for (int slot = 0; slot < ownerSummons.Length; slot++)
                if (ownerSummons[slot] != null) ownerBaselineWeapons[slot] = null;
            UnbindOwnerSummons();
            DisposeSummonPresentationReplica();
        }

        private void OnEnable()
        {
            if (!NetworkClient.active || netId == 0 || !isClient) return;
            if (isOwned) CmdRequestSummonBaseline();
            else CmdRequestSummonViews();
        }

        [Command(channel = Channels.Reliable)]
        private void CmdRequestSummonBaseline(NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender != connectionToClient || !playerBuildRuntime.IsBuildActive) return;
            TargetRebindSummons(sender, GetComponent<NetworkModifierSelection>().BuildRevision,
                CaptureSummonMaturities(), CaptureCooldowns());
        }

        [TargetRpc(channel = Channels.Reliable)]
        private void TargetRebindSummons(NetworkConnectionToClient target, uint revision,
            PlayerSummonMaturitySnapshot[] maturities, PlayerWeaponCooldownSnapshot[] cooldowns)
        {
            if (!isOwned || !isActiveAndEnabled || !playerBuildRuntime.IsBuildActive ||
                revision != GetComponent<NetworkModifierSelection>().OwnerBuildRevision) return;
            ApplyOwnerSummonBaseline(maturities);
            ApplyOwnerCooldownBaseline(cooldowns, NetworkTime.time);
        }

        private void HandleSummonState(int slot, SummonPresentationState state)
        {
            outgoingSummonPoses.Remove(state.PetId);
            if (!isActiveAndEnabled || !isOwned || !NetworkClient.active) return;
            // Reliable commands follow attack admission and precede root completion on this connection.
            CmdSubmitSummonState(new NetworkSummonPresentationState
            { SourcePlayerId = netId, SlotIndex = slot, EventNetworkTime = NetworkTime.time, State = state });
            SentSummonStateCount++;
        }

        private void HandleSummonPose(SummonPresentationPose pose)
        {
            if (isActiveAndEnabled && isOwned && NetworkClient.active)
                outgoingSummonPoses[pose.PetId] = new NetworkSummonPresentationPose
                { SourcePlayerId = netId, EventNetworkTime = NetworkTime.time, Sample = pose };
        }

        private void HandleSummonTermination(SummonPresentationTermination termination)
        {
            outgoingSummonPoses.Remove(termination.PetId);
            if (isOwned && NetworkClient.active) CmdSubmitSummonTermination(new NetworkSummonPresentationTermination
            { SourcePlayerId = netId, WeaponId = termination.WeaponId, PetId = termination.PetId, EventNetworkTime = NetworkTime.time });
        }

        private void FlushSummonPoses()
        {
            if (NetworkTime.time < nextSummonPoseTime || outgoingSummonPoses.Count == 0) return;
            nextSummonPoseTime = NetworkTime.time + .1d;
            var poses = new NetworkSummonPresentationPose[outgoingSummonPoses.Count];
            outgoingSummonPoses.Values.CopyTo(poses, 0);
            outgoingSummonPoses.Clear();
            CmdSubmitSummonPoses(poses);
        }

        [Command(channel = Channels.Reliable)]
        private void CmdSubmitSummonState(NetworkSummonPresentationState edge, NetworkConnectionToClient sender = null)
        {
            var world = NetworkCombatWorld.Instance;
            var pet = new CombatEventId(edge.State.PetId);
            if (sender == null || sender != connectionToClient || !edge.IsValid || edge.SourcePlayerId != netId || world == null ||
                !world.Gateway.ClientIdentities.Validate(bridge.OwnerPlayerId, pet.Value, pet.Sequence) ||
                edge.EventNetworkTime > NetworkTime.time + .1d || edge.EventNetworkTime < NetworkTime.time - 2d)
            { RejectSummonPresentation(); return; }
            RefreshServerSummonWeapon(edge.SlotIndex);
            var weapon = serverSummonWeapons[edge.SlotIndex];
            var state = edge.State;
            serverSummonHistory.TryGetState(state.PetId, out var previous);
            double readyAt = serverSummonMaturities[edge.SlotIndex].MaturityAt;
            if (weapon == null || weapon.ID != state.WeaponId ||
                (state.Phase >= SummonPhase.Birth && edge.EventNetworkTime + .1d < readyAt) ||
                (state.Phase >= SummonPhase.Positioning && edge.EventNetworkTime + .1d < readyAt + weapon.BirthPresentationDuration) ||
                (state.AttackEventId != 0 && (!world.Gateway.Attacks.Contains(netId, state.AttackEventId, state.WeaponId) ||
                    !serverSummonRootSlots.TryGetValue(state.AttackEventId, out int admittedSlot) || admittedSlot != edge.SlotIndex)) ||
                !serverSummonHistory.TryApplyState(edge))
            { RejectSummonPresentation(); return; }
            if (state.Phase == SummonPhase.Positioning && previous.State.Phase == SummonPhase.AttackExit)
                CompleteSummonCooldown(edge.SlotIndex, previous.State.AttackEventId, edge.EventNetworkTime,
                    Math.Max(0f, weapon.GetAttackSequenceDuration() - weapon.DurationValue));
            RpcApplySummonState(edge);
        }

        [Command(channel = Channels.Unreliable)]
        private void CmdSubmitSummonPoses(NetworkSummonPresentationPose[] poses, NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender != connectionToClient || poses == null || poses.Length == 0 || poses.Length > PlayerBuildRuntime.HandSlotCount)
            { RejectSummonPresentation(); return; }
            var accepted = new List<NetworkSummonPresentationPose>(poses.Length);
            foreach (var pose in poses)
            {
                if (!pose.IsValid || pose.SourcePlayerId != netId || pose.EventNetworkTime > NetworkTime.time + .1d ||
                    pose.EventNetworkTime < NetworkTime.time - 2d) { RejectSummonPresentation(); continue; }
                if (serverSummonHistory.TryApplyPose(pose)) accepted.Add(pose);
                // Unreliable poses may cross a reliable phase edge or arrive out of order.
                // They still cannot update history, but this is a normal dropped sample.
                else DroppedSummonPoseCount++;
            }
            if (accepted.Count > 0) RpcApplySummonPoses(accepted.ToArray());
        }

        [Command(channel = Channels.Reliable)]
        private void CmdSubmitSummonTermination(NetworkSummonPresentationTermination edge, NetworkConnectionToClient sender = null)
        {
            serverSummonHistory.TryGetState(edge.PetId, out var previous);
            if (sender == null || sender != connectionToClient || edge.SourcePlayerId != netId ||
                edge.EventNetworkTime > NetworkTime.time + .1d || edge.EventNetworkTime < NetworkTime.time - 2d ||
                !serverSummonHistory.TryTerminate(edge))
            { RejectSummonPresentation(); return; }
            if (previous.State.AttackEventId != 0)
                CompleteSummonCooldown(previous.SlotIndex, previous.State.AttackEventId, edge.EventNetworkTime, 0f);
            RpcApplySummonTermination(edge);
        }

        private void CompleteSummonCooldown(int slot, ulong rootId, double completedAt, float minimumDuration)
        {
            if (serverSummonRootSlots.TryGetValue(rootId, out int admittedSlot) && admittedSlot == slot &&
                serverCooldowns[slot].TryCompleteSequence(rootId, completedAt, minimumDuration, out var completed))
                serverCooldowns[slot] = completed;
        }

        [ClientRpc(channel = Channels.Reliable)]
        private void RpcApplySummonState(NetworkSummonPresentationState edge)
        { if (!isOwned) ApplyRemoteSummonState(edge); }

        [ClientRpc(channel = Channels.Unreliable)]
        private void RpcApplySummonPoses(NetworkSummonPresentationPose[] poses)
        {
            if (isOwned || summonPresentationReplica == null) return;
            foreach (var pose in poses)
                if (pose.SourcePlayerId == netId && clientSummonHistory.TryApplyPose(pose) && summonPresentationReplica.TryApplyPose(pose.Sample))
                    ReplicaSummonPoseCount++;
        }

        [ClientRpc(channel = Channels.Reliable)]
        private void RpcApplySummonTermination(NetworkSummonPresentationTermination edge)
        {
            if (!isOwned && clientSummonHistory.TryTerminate(edge) &&
                summonPresentationReplica != null && summonPresentationReplica.TryTerminate(edge.WeaponId, edge.PetId)) ReplicaSummonTerminationCount++;
        }

        // A reconnecting spectator receives current phases/poses, not a replay of earlier pet attacks.
        [Command(requiresAuthority = false, channel = Channels.Reliable)]
        private void CmdRequestSummonViews(NetworkConnectionToClient sender = null)
        {
            if (sender != null && sender.isAuthenticated && sender.identity != null)
                TargetReceiveSummonViews(sender, serverSummonHistory.CaptureStates());
        }

        [TargetRpc(channel = Channels.Reliable)]
        private void TargetReceiveSummonViews(NetworkConnectionToClient target, NetworkSummonPresentationState[] states)
        { if (!isOwned) foreach (var state in states) ApplyRemoteSummonState(state); }

        private void ApplyRemoteSummonState(NetworkSummonPresentationState edge)
        {
            if (!isActiveAndEnabled || edge.SourcePlayerId != netId || !clientSummonHistory.CanApplyState(edge)) return;
            var database = playerBootstrap != null ? playerBootstrap.ResolveSharedRuntimeDatabase() : null;
            if (database == null || playerMovement == null) { RejectSummonPresentation(); return; }
            summonPresentationReplica ??= new SummonPresentationReplica(playerMovement, database);
            if (summonPresentationReplica.TryApplyState(edge.State, (float)Math.Max(0d, NetworkTime.time - edge.EventNetworkTime)))
            {
                clientSummonHistory.TryApplyState(edge);
                ReceivedSummonStateCount++;
            }
            else RejectSummonPresentation();
        }

        private void DisposeSummonPresentationReplica()
        { summonPresentationReplica?.Dispose(); summonPresentationReplica = null; clientSummonHistory.Clear(); }

        private void ClearServerSummons()
        {
            serverSummonHistory.Clear(); serverSummonRootSlots.Clear();
            Array.Clear(serverSummonMaturities, 0, serverSummonMaturities.Length);
            Array.Clear(serverSummonWeapons, 0, serverSummonWeapons.Length);
        }

        private void RejectSummonPresentation() { RejectedSummonPresentationCount++; RejectedPresentationCount++; }
        private sealed class SummonBinding
        {
            public SummonAttackBehaviour Weapon;
            public Action<SummonPresentationState> State;
            public Action<SummonPresentationPose> Pose;
            public Action<SummonPresentationTermination> Termination;
        }
    }
}
