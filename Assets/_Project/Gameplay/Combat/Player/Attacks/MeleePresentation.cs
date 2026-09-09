using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
    [Serializable]
    public struct MeleePresentationKey : IEquatable<MeleePresentationKey>
    {
        public ulong AttackEventId;
        public ushort SlashIndex;

        public MeleePresentationKey(ulong attackEventId, ushort slashIndex)
        {
            AttackEventId = attackEventId;
            SlashIndex = slashIndex;
        }

        public bool IsValid => AttackEventId != 0UL;

        public bool Equals(MeleePresentationKey other) =>
            AttackEventId == other.AttackEventId && SlashIndex == other.SlashIndex;

        public override bool Equals(object obj) =>
            obj is MeleePresentationKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked { return (AttackEventId.GetHashCode() * 397) ^ SlashIndex; }
        }
    }

    /// <summary>Visual data for one slash, relative to its weapon emitter.</summary>
    public readonly struct MeleePresentationSpawn
    {
        public MeleePresentationSpawn(uint weaponId, MeleePresentationKey key,
            Vector3 localPosition, Vector2 direction, float animationDuration,
            ProjectilePresentationStats stats)
        {
            WeaponId = weaponId;
            Key = key;
            LocalPosition = localPosition;
            Direction = direction.normalized;
            AnimationDuration = animationDuration;
            Stats = stats;
        }

        public uint WeaponId { get; }
        public MeleePresentationKey Key { get; }
        public Vector3 LocalPosition { get; }
        public Vector2 Direction { get; }
        /// <summary>-1 plays the authored clip; a positive value overrides its duration.</summary>
        public float AnimationDuration { get; }
        public ProjectilePresentationStats Stats { get; }
    }

    public readonly struct MeleePresentationTermination
    {
        public MeleePresentationTermination(uint weaponId, MeleePresentationKey key)
        {
            WeaponId = weaponId;
            Key = key;
        }

        public uint WeaponId { get; }
        public MeleePresentationKey Key { get; }
    }
}
