using System;
using AstralShift.HellMaiden.Combat;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
    [Serializable]
    public struct BeamPresentationKey : IEquatable<BeamPresentationKey>
    {
        public ulong AttackEventId;
        public ushort BeamIndex;
        public BeamPresentationKey(ulong attackEventId, ushort beamIndex)
        { AttackEventId = attackEventId; BeamIndex = beamIndex; }
        public bool IsValid => AttackEventId != 0;
        public bool Equals(BeamPresentationKey other) =>
            AttackEventId == other.AttackEventId && BeamIndex == other.BeamIndex;
        public override bool Equals(object obj) => obj is BeamPresentationKey other && Equals(other);
        public override int GetHashCode() => unchecked((AttackEventId.GetHashCode() * 397) ^ BeamIndex);
    }

    /// <summary>Frozen visual data for one beam; all beams in a root share its heading.</summary>
    public readonly struct BeamPresentationSpawn
    {
        public BeamPresentationSpawn(uint weaponId, BeamPresentationKey key, int beamCount,
            Vector2 rootDirection, AttackElement element, float animationDuration,
            ProjectilePresentationStats stats)
        {
            WeaponId = weaponId; Key = key; BeamCount = beamCount;
            RootDirection = rootDirection.normalized; Element = element;
            AnimationDuration = animationDuration; Stats = stats;
        }
        public uint WeaponId { get; }
        public BeamPresentationKey Key { get; }
        public int BeamCount { get; }
        public Vector2 RootDirection { get; }
        public AttackElement Element { get; }
        public float AnimationDuration { get; }
        public ProjectilePresentationStats Stats { get; }
    }

    public readonly struct BeamPresentationAim
    {
        public BeamPresentationAim(uint weaponId, ulong attackEventId, Vector2 rootDirection)
        { WeaponId = weaponId; AttackEventId = attackEventId; RootDirection = rootDirection.normalized; }
        public uint WeaponId { get; }
        public ulong AttackEventId { get; }
        public Vector2 RootDirection { get; }
    }

    public readonly struct BeamPresentationTermination
    {
        public BeamPresentationTermination(uint weaponId, BeamPresentationKey key)
        { WeaponId = weaponId; Key = key; }
        public uint WeaponId { get; }
        public BeamPresentationKey Key { get; }
    }
}
