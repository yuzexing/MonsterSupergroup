using System;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    // RPC data copies the trusted preset. No owner command accepts configurable knockback values.
    [Serializable]
    public struct EnemyKnockbackCurveKey
    {
        public float Time, Value, InTangent, OutTangent, InWeight, OutWeight;
        public int WeightedMode;
        public bool IsValid => EnemyKnockbackSettings.Finite(Time) && EnemyKnockbackSettings.Finite(Value) &&
            EnemyKnockbackSettings.Finite(InTangent) && EnemyKnockbackSettings.Finite(OutTangent) &&
            EnemyKnockbackSettings.Finite(InWeight) && EnemyKnockbackSettings.Finite(OutWeight) &&
            InWeight >= 0f && OutWeight >= 0f && WeightedMode >= 0 && WeightedMode <= 3;
        public Keyframe ToKeyframe() => new Keyframe(Time, Value, InTangent, OutTangent, InWeight, OutWeight)
            { weightedMode = (UnityEngine.WeightedMode)WeightedMode };
        public static EnemyKnockbackCurveKey From(Keyframe key) => new EnemyKnockbackCurveKey
        {
            Time = key.time, Value = key.value, InTangent = key.inTangent, OutTangent = key.outTangent,
            InWeight = key.inWeight, OutWeight = key.outWeight, WeightedMode = (int)key.weightedMode
        };
    }

    [Serializable]
    public struct EnemyKnockbackSettings
    {
        public float Distance, SpeedMultiplier, StaggerTime;
        public bool FixedDirection;
        public Vector2 Direction;
        public int PreWrapMode, PostWrapMode;
        public EnemyKnockbackCurveKey[] CurveKeys;
        public bool IsValid
        {
            get
            {
                if (!Finite(Distance) || Distance < 0f || !Finite(SpeedMultiplier) || SpeedMultiplier <= 0f ||
                    !Finite(StaggerTime) || StaggerTime < 0f || (Distance == 0f && StaggerTime == 0f) ||
                    !Finite(Direction.x) || !Finite(Direction.y) || !Finite(Direction.sqrMagnitude) ||
                    (FixedDirection && Direction.sqrMagnitude <= 0f) ||
                    !Enum.IsDefined(typeof(WrapMode), PreWrapMode) || !Enum.IsDefined(typeof(WrapMode), PostWrapMode) ||
                    CurveKeys == null || CurveKeys.Length == 0 || CurveKeys.Length > 64) return false;
                for (int index = 0; index < CurveKeys.Length; index++)
                    if (!CurveKeys[index].IsValid || (index > 0 && CurveKeys[index].Time <= CurveKeys[index - 1].Time)) return false;
                return true;
            }
        }

        public static EnemyKnockbackSettings From(KnockbackSettings source)
        {
            if (source == null || source.speedCurve == null) throw new ArgumentNullException(nameof(source));
            Keyframe[] keys = source.speedCurve.keys;
            var result = new EnemyKnockbackSettings
            {
                Distance = source.distance, SpeedMultiplier = source.speedMultiplier, StaggerTime = source.staggerTime,
                FixedDirection = source.fixedDirection, Direction = source.direction,
                PreWrapMode = (int)source.speedCurve.preWrapMode, PostWrapMode = (int)source.speedCurve.postWrapMode,
                CurveKeys = new EnemyKnockbackCurveKey[keys.Length]
            };
            for (int index = 0; index < keys.Length; index++) result.CurveKeys[index] = EnemyKnockbackCurveKey.From(keys[index]);
            if (!result.IsValid) throw new ArgumentException("Invalid authoritative knockback preset.", nameof(source));
            return result;
        }

        public KnockbackSettings CreateRuntimePreset()
        {
            if (!IsValid) throw new InvalidOperationException("Cannot materialize an invalid knockback preset.");
            var result = ScriptableObject.CreateInstance<KnockbackSettings>();
            result.hideFlags = HideFlags.HideAndDontSave;
            result.distance = Distance;
            result.speedMultiplier = SpeedMultiplier;
            result.staggerTime = StaggerTime;
            result.fixedDirection = FixedDirection;
            result.direction = Direction;
            var keys = new Keyframe[CurveKeys.Length];
            for (int index = 0; index < keys.Length; index++) keys[index] = CurveKeys[index].ToKeyframe();
            result.speedCurve = new AnimationCurve(keys)
                { preWrapMode = (WrapMode)PreWrapMode, postWrapMode = (WrapMode)PostWrapMode };
            return result;
        }

        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    [Serializable]
    public struct EnemyKnockbackCommand
    {
        public uint EnemyEntityId, AssignmentEpoch, SourcePlayerId, AbilityCombatId;
        public ulong RootEventId, CommandId;
        public double IssuedAt;
        public Vector2 Origin;
        public EnemyKnockbackSettings Settings;
        public bool IsValid => EnemyEntityId != 0 && AssignmentEpoch != 0 && SourcePlayerId != 0 && CommandId != 0 &&
            (AbilityCombatId & 0x80000000u) != 0 && new CombatEventId(RootEventId).IsValid &&
            new CombatEventId(RootEventId).Sequence != 0 && EnemyKnockbackSettings.Finite(IssuedAt) && IssuedAt >= 0d &&
            EnemyKnockbackSettings.Finite(Origin.x) && EnemyKnockbackSettings.Finite(Origin.y) && Settings.IsValid;

        public bool IsTimely(double now) => EnemyKnockbackSettings.Finite(now) &&
            IssuedAt <= now + .1d && IssuedAt >= now - 2d;

        public bool MatchesSimulator(EnemySimulationAssignment assignment, uint receivingPlayerId, bool serverLocal) =>
            EnemyEntityId == assignment.EnemyEntityId && AssignmentEpoch == assignment.Epoch &&
            (serverLocal
                ? assignment.Host == EnemySimulationHost.ServerFallback || assignment.Host == EnemySimulationHost.ServerAuthoritative
                : assignment.Host == EnemySimulationHost.ClientPlayer && receivingPlayerId != 0 &&
                  assignment.SimulationOwnerPlayerId == receivingPlayerId);
    }

    /// <summary>Per-enemy receiver ordering. Assignment changes cannot make an old command replayable.</summary>
    public sealed class EnemyKnockbackCommandHistory
    {
        public ulong LastCommandId { get; private set; }
        public bool TryAccept(EnemyKnockbackCommand command, EnemySimulationAssignment assignment,
            uint receivingPlayerId, bool serverLocal, double now)
        {
            if (!command.IsValid || !command.IsTimely(now) || command.CommandId <= LastCommandId ||
                !command.MatchesSimulator(assignment, receivingPlayerId, serverLocal)) return false;
            LastCommandId = command.CommandId;
            return true;
        }
        public void Clear() => LastCommandId = 0;
    }
}
