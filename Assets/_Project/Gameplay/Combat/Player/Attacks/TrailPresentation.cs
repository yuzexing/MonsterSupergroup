using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
    /// <summary>One frozen trail per root; point positions are source world samples.</summary>
    public readonly struct TrailPresentationSpawn
    {
        public TrailPresentationSpawn(uint weaponId, ulong attackEventId, ulong dashUseId, AttackElement element,
            Vector2 origin, float samplingDuration, float segmentDuration, float hitInterval, ProjectilePresentationStats stats)
        {
            WeaponId = weaponId; AttackEventId = attackEventId; DashUseId = dashUseId; Element = element;
            Origin = origin; SamplingDuration = samplingDuration; SegmentDuration = segmentDuration;
            HitInterval = hitInterval; Stats = stats;
        }
        public uint WeaponId { get; }
        public ulong AttackEventId { get; }
        public ulong DashUseId { get; }
        public AttackElement Element { get; }
        public Vector2 Origin { get; }
        public float SamplingDuration { get; }
        public float SegmentDuration { get; }
        public float HitInterval { get; }
        public ProjectilePresentationStats Stats { get; }
        public bool IsValid => AttackEventId != 0 && DashUseId != 0 &&
            (Element == AttackElement.Default || Element == AttackElement.Poison || Element == AttackElement.Fire) &&
            IsFinite(Origin.x) && IsFinite(Origin.y) && IsFinite(SamplingDuration) && SamplingDuration >= 0 &&
            IsFinite(SegmentDuration) && SegmentDuration >= 0 && IsFinite(HitInterval) && HitInterval > 0 && Stats.IsFinite;
        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public readonly struct TrailPresentationPoint
    {
        public TrailPresentationPoint(uint weaponId, ulong attackEventId, uint pointIndex, Vector2 worldPosition, float elapsedSeconds)
        { WeaponId = weaponId; AttackEventId = attackEventId; PointIndex = pointIndex; WorldPosition = worldPosition; ElapsedSeconds = elapsedSeconds; }
        public uint WeaponId { get; }
        public ulong AttackEventId { get; }
        public uint PointIndex { get; }
        public Vector2 WorldPosition { get; }
        public float ElapsedSeconds { get; }
    }

    public readonly struct TrailPresentationSamplingEnded
    {
        public TrailPresentationSamplingEnded(uint weaponId, ulong attackEventId, float samplingElapsedSeconds)
        { WeaponId = weaponId; AttackEventId = attackEventId; SamplingElapsedSeconds = samplingElapsedSeconds; }
        public uint WeaponId { get; }
        public ulong AttackEventId { get; }
        public float SamplingElapsedSeconds { get; }
    }

    public readonly struct TrailPresentationTermination
    {
        public TrailPresentationTermination(uint weaponId, ulong attackEventId)
        { WeaponId = weaponId; AttackEventId = attackEventId; }
        public uint WeaponId { get; }
        public ulong AttackEventId { get; }
    }
}

