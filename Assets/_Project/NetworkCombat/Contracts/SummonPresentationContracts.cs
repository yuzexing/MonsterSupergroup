using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;

namespace MonsterSupergroup.NetworkCombat
{
    [Serializable]
    public struct NetworkSummonPresentationState
    {
        public uint SourcePlayerId;
        public int SlotIndex;
        public double EventNetworkTime;
        public SummonPresentationState State;
        public bool IsValid => SourcePlayerId != 0 && (uint)SlotIndex < PlayerBuildRuntime.HandSlotCount &&
            FiniteTime(EventNetworkTime) && State.IsValid;
        public static bool FiniteTime(double time) => !double.IsNaN(time) && !double.IsInfinity(time) && time >= 0d;
    }

    [Serializable]
    public struct NetworkSummonPresentationPose
    {
        public uint SourcePlayerId;
        public double EventNetworkTime;
        public SummonPresentationPose Sample;
        public bool IsValid => SourcePlayerId != 0 && NetworkSummonPresentationState.FiniteTime(EventNetworkTime) &&
            Sample.WeaponId != 0 && Sample.PetId != 0 && Sample.PhaseSequence != 0 && Sample.PoseSequence != 0 && Sample.Pose.IsFinite;
    }

    [Serializable]
    public struct NetworkSummonPresentationTermination
    {
        public uint SourcePlayerId, WeaponId;
        public ulong PetId;
        public double EventNetworkTime;
        public bool IsValid => SourcePlayerId != 0 && WeaponId != 0 && PetId != 0 &&
            NetworkSummonPresentationState.FiniteTime(EventNetworkTime);
    }

    /// <summary>Current public pet views for deduplication and reconnect spectators; no AI or damage state.</summary>
    public sealed class SummonPresentationHistory
    {
        private readonly Dictionary<ulong, Pet> pets = new Dictionary<ulong, Pet>();
        private readonly HashSet<ulong> seenPetIds = new HashSet<ulong>();
        private readonly ulong[] slots = new ulong[PlayerBuildRuntime.HandSlotCount];
        private readonly uint[] lastPetSequences = new uint[PlayerBuildRuntime.HandSlotCount];
        public int PetCount => pets.Count;

        // The adapter separately validates identity/epoch, current Build, maturity and attack admission.
        public bool CanApplyState(NetworkSummonPresentationState edge)
        {
            if (!edge.IsValid) return false;
            SummonPresentationState state = edge.State;
            if (!pets.TryGetValue(state.PetId, out Pet pet))
            {
                var id = new CombatEventId(state.PetId);
                return slots[edge.SlotIndex] == 0 && !seenPetIds.Contains(state.PetId) &&
                    id.Sequence > lastPetSequences[edge.SlotIndex];
            }
            if (pet.State.SourcePlayerId != edge.SourcePlayerId || pet.State.SlotIndex != edge.SlotIndex ||
                pet.State.State.WeaponId != state.WeaponId || edge.EventNetworkTime < pet.State.EventNetworkTime ||
                !ProjectilePresentationSequence.IsNewer(state.PhaseSequence, pet.State.State.PhaseSequence)) return false;
            if (state.AttackEventId != 0 && state.AttackEventId == pet.State.State.AttackEventId &&
                (!state.Stats.Equals(pet.State.State.Stats) || state.Element != pet.State.State.Element)) return false;
            return true;
        }

        public bool TryApplyState(NetworkSummonPresentationState edge)
        {
            if (!CanApplyState(edge)) return false;
            SummonPresentationState state = edge.State;
            if (!pets.TryGetValue(state.PetId, out Pet pet))
            {
                pet = new Pet();
                pets.Add(state.PetId, pet);
                seenPetIds.Add(state.PetId);
                slots[edge.SlotIndex] = state.PetId;
                lastPetSequences[edge.SlotIndex] = new CombatEventId(state.PetId).Sequence;
            }
            pet.State = edge;
            pet.LastPoseSequence = 0;
            pet.LastPoseTime = edge.EventNetworkTime;
            return true;
        }

        public bool TryApplyPose(NetworkSummonPresentationPose edge)
        {
            if (!edge.IsValid || !pets.TryGetValue(edge.Sample.PetId, out Pet pet) ||
                pet.State.SourcePlayerId != edge.SourcePlayerId || pet.State.State.WeaponId != edge.Sample.WeaponId ||
                pet.State.State.PhaseSequence != edge.Sample.PhaseSequence || edge.EventNetworkTime < pet.LastPoseTime ||
                !ProjectilePresentationSequence.IsNewer(edge.Sample.PoseSequence, pet.LastPoseSequence)) return false;
            pet.LastPoseSequence = edge.Sample.PoseSequence;
            pet.LastPoseTime = edge.EventNetworkTime;
            var state = pet.State.State;
            state.Pose = edge.Sample.Pose;
            pet.State.State = state;
            return true;
        }

        public bool TryTerminate(NetworkSummonPresentationTermination edge)
        {
            if (!edge.IsValid || !pets.TryGetValue(edge.PetId, out Pet pet) ||
                pet.State.SourcePlayerId != edge.SourcePlayerId || pet.State.State.WeaponId != edge.WeaponId) return false;
            slots[pet.State.SlotIndex] = 0;
            return pets.Remove(edge.PetId);
        }

        public NetworkSummonPresentationState[] CaptureStates()
        {
            var result = new NetworkSummonPresentationState[pets.Count];
            int index = 0;
            foreach (Pet pet in pets.Values) result[index++] = pet.State;
            return result;
        }

        public bool TryGetState(ulong petId, out NetworkSummonPresentationState state)
        {
            if (pets.TryGetValue(petId, out Pet pet)) { state = pet.State; return true; }
            state = default;
            return false;
        }
        public void Clear()
        { pets.Clear(); seenPetIds.Clear(); Array.Clear(slots, 0, slots.Length); Array.Clear(lastPetSequences, 0, lastPetSequences.Length); }
        private sealed class Pet
        {
            public NetworkSummonPresentationState State;
            public uint LastPoseSequence;
            public double LastPoseTime;
        }
    }
}
