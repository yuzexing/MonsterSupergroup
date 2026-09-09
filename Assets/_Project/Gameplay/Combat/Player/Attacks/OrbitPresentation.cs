using System;

namespace AstralShift.HellMaiden.Player.Attacks
{
    [Serializable]
    public struct OrbitPresentationKey : IEquatable<OrbitPresentationKey>
    {
        public ulong AttackEventId;
        public ushort OrbIndex;
        public OrbitPresentationKey(ulong attackEventId, ushort orbIndex)
        { AttackEventId = attackEventId; OrbIndex = orbIndex; }
        public bool IsValid => AttackEventId != 0;
        public bool Equals(OrbitPresentationKey other) => AttackEventId == other.AttackEventId && OrbIndex == other.OrbIndex;
        public override bool Equals(object obj) => obj is OrbitPresentationKey other && Equals(other);
        public override int GetHashCode() => unchecked((AttackEventId.GetHashCode() * 397) ^ OrbIndex);
    }

    /// <summary>Frozen visual orbit data. InitialPhaseRadians is this orb's own phase, including its ordinal.</summary>
    public readonly struct OrbitPresentationSpawn
    {
        public OrbitPresentationSpawn(uint weaponId, OrbitPresentationKey key, int orbCount,
            float initialPhaseRadians, float radius, float angularSpeedRadians, float orbitDuration,
            ProjectilePresentationStats stats)
        {
            WeaponId = weaponId; Key = key; OrbCount = orbCount; InitialPhaseRadians = initialPhaseRadians;
            Radius = radius; AngularSpeedRadians = angularSpeedRadians; OrbitDuration = orbitDuration; Stats = stats;
        }
        public uint WeaponId { get; }
        public OrbitPresentationKey Key { get; }
        public int OrbCount { get; }
        public float InitialPhaseRadians { get; }
        public float Radius { get; }
        public float AngularSpeedRadians { get; }
        public float OrbitDuration { get; }
        public ProjectilePresentationStats Stats { get; }

        // Evaluate in double and check the float range explicitly. Some Mono backends keep
        // float intermediates at higher precision until they are assigned to a Transform.
        public static bool IsFinitePhase(float initialPhase, float angularSpeed, float elapsed)
        {
            double phase = (double)initialPhase + (double)angularSpeed * elapsed;
            return !double.IsNaN(phase) && !double.IsInfinity(phase) && Math.Abs(phase) <= float.MaxValue;
        }
    }

    public readonly struct OrbitPresentationHiding
    {
        public OrbitPresentationHiding(uint weaponId, OrbitPresentationKey key, float orbitElapsedSeconds)
        { WeaponId = weaponId; Key = key; OrbitElapsedSeconds = orbitElapsedSeconds; }
        public uint WeaponId { get; }
        public OrbitPresentationKey Key { get; }
        public float OrbitElapsedSeconds { get; }
    }

    public readonly struct OrbitPresentationTermination
    {
        public OrbitPresentationTermination(uint weaponId, OrbitPresentationKey key) { WeaponId = weaponId; Key = key; }
        public uint WeaponId { get; }
        public OrbitPresentationKey Key { get; }
    }
}
