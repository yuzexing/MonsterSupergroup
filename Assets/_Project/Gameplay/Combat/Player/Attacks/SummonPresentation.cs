using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
    public enum SummonPhase : byte { Cocoon, Birth, Positioning, AttackEnter, AttackMain, AttackExit }

    /// <summary>A view supplied by the network enemy world, never a legacy EnemyAI instance.</summary>
    public readonly struct SummonTarget
    {
        public SummonTarget(Transform hurtBox, bool isBoss) { HurtBox = hurtBox; IsBoss = isBoss; }
        public Transform HurtBox { get; }
        public bool IsBoss { get; }
        public bool IsAvailable => HurtBox != null && HurtBox.gameObject.activeInHierarchy;
        public Vector2 Position => HurtBox.position;
    }

    [Serializable]
    public struct SummonPose
    {
        public Vector3 Position;
        public Vector3 RotationPivotEuler;
        public Vector3 IsoLocalPosition;
        public float MoveAnimationSpeed;
        public bool IsFinite => Finite(Position) && Finite(RotationPivotEuler) && Finite(IsoLocalPosition) &&
            Finite(MoveAnimationSpeed) && MoveAnimationSpeed >= 0f;
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }

    [Serializable]
    public struct SummonPresentationState
    {
        public uint WeaponId;
        public ulong PetId;
        public uint PhaseSequence;
        public SummonPhase Phase;
        public float PhaseElapsedSeconds;
        public ulong AttackEventId;
        public AttackElement Element;
        public ProjectilePresentationStats Stats;
        public SummonPose Pose;
        public bool IsValid => WeaponId != 0 && PetId != 0 && PhaseSequence != 0 &&
            Phase <= SummonPhase.AttackExit && SummonPose.Finite(PhaseElapsedSeconds) && PhaseElapsedSeconds >= 0f &&
            ((Phase >= SummonPhase.AttackEnter) == (AttackEventId != 0)) && Stats.IsFinite && Pose.IsFinite &&
            Element >= AttackElement.Default && Element <= AttackElement.Fire;
    }

    [Serializable]
    public struct SummonPresentationPose
    {
        public uint WeaponId;
        public ulong PetId;
        public uint PhaseSequence;
        public uint PoseSequence;
        public SummonPose Pose;
    }

    public readonly struct SummonPresentationTermination
    {
        public SummonPresentationTermination(uint weaponId, ulong petId) { WeaponId = weaponId; PetId = petId; }
        public uint WeaponId { get; }
        public ulong PetId { get; }
    }
}