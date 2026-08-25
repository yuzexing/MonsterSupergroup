using System;
using System.Collections.Generic;

namespace MonsterSupergroup.GAS
{
    public interface ICombatTimeSource
    {
        double CurrentTimeSeconds { get; }
    }

    public sealed class MonotonicCombatTimeSource : ICombatTimeSource
    {
        public static readonly MonotonicCombatTimeSource Instance =
            new MonotonicCombatTimeSource();

        private MonotonicCombatTimeSource()
        {
        }

        public double CurrentTimeSeconds =>
            System.Diagnostics.Stopwatch.GetTimestamp() /
            (double)System.Diagnostics.Stopwatch.Frequency;
    }

    public readonly struct CombatChainLimits
    {
        public CombatChainLimits(ushort maxChainDepth, ushort maxTriggersPerRootEvent)
        {
            if (maxChainDepth == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxChainDepth));
            }

            if (maxTriggersPerRootEvent == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxTriggersPerRootEvent));
            }

            MaxChainDepth = maxChainDepth;
            MaxTriggersPerRootEvent = maxTriggersPerRootEvent;
        }

        public static CombatChainLimits Default => new CombatChainLimits(32, 256);

        public ushort MaxChainDepth { get; }
        public ushort MaxTriggersPerRootEvent { get; }
    }

    public readonly struct BuildTriggerPolicy
    {
        public BuildTriggerPolicy(
            bool allowSelfTrigger,
            bool oncePerRootEvent,
            bool oncePerTargetPerRootEvent,
            float internalCooldownSeconds = 0f)
        {
            if (float.IsNaN(internalCooldownSeconds) ||
                float.IsInfinity(internalCooldownSeconds) ||
                internalCooldownSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(internalCooldownSeconds));
            }

            AllowSelfTrigger = allowSelfTrigger;
            OncePerRootEvent = oncePerRootEvent;
            OncePerTargetPerRootEvent = oncePerTargetPerRootEvent;
            InternalCooldownSeconds = internalCooldownSeconds;
        }

        public static BuildTriggerPolicy Default => new BuildTriggerPolicy(
            allowSelfTrigger: false,
            oncePerRootEvent: false,
            oncePerTargetPerRootEvent: false);

        public bool AllowSelfTrigger { get; }
        public bool OncePerRootEvent { get; }
        public bool OncePerTargetPerRootEvent { get; }
        public float InternalCooldownSeconds { get; }
    }

    public sealed class CombatTriggerGuard
    {
        private readonly CombatChainLimits limits;
        private readonly Dictionary<CombatEventId, RootState> roots =
            new Dictionary<CombatEventId, RootState>();
        private readonly Dictionary<CooldownKey, double> cooldownUntil =
            new Dictionary<CooldownKey, double>();

        public CombatTriggerGuard()
            : this(CombatChainLimits.Default)
        {
        }

        public CombatTriggerGuard(CombatChainLimits limits)
        {
            if (limits.MaxChainDepth == 0 || limits.MaxTriggersPerRootEvent == 0)
            {
                throw new ArgumentException("Combat chain limits must be initialized.", nameof(limits));
            }

            this.limits = limits;
        }

        public bool TryEnter(
            CombatContext context,
            EquipmentModifierID triggerId,
            uint targetEntityId,
            BuildTriggerPolicy policy,
            double currentTimeSeconds)
        {
            if (!context.IsValid || !triggerId.IsValid)
            {
                return false;
            }

            if (double.IsNaN(currentTimeSeconds) || double.IsInfinity(currentTimeSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(currentTimeSeconds));
            }

            if (context.ChainDepth > limits.MaxChainDepth)
            {
                return false;
            }

            if (!policy.AllowSelfTrigger && context.BuildId == triggerId.Value)
            {
                return false;
            }

            var cooldownKey = new CooldownKey(triggerId, targetEntityId);
            if (policy.InternalCooldownSeconds > 0f &&
                cooldownUntil.TryGetValue(cooldownKey, out double availableAt) &&
                currentTimeSeconds < availableAt)
            {
                return false;
            }

            if (!roots.TryGetValue(context.RootEventId, out RootState state))
            {
                state = new RootState();
                roots.Add(context.RootEventId, state);
            }

            if (state.TriggerCount >= limits.MaxTriggersPerRootEvent)
            {
                return false;
            }

            if (policy.OncePerRootEvent && state.Triggers.Contains(triggerId))
            {
                return false;
            }

            var targetKey = new RootTargetKey(triggerId, targetEntityId);
            if (policy.OncePerTargetPerRootEvent && state.TargetTriggers.Contains(targetKey))
            {
                return false;
            }

            state.TriggerCount++;
            if (policy.OncePerRootEvent)
            {
                state.Triggers.Add(triggerId);
            }

            if (policy.OncePerTargetPerRootEvent)
            {
                state.TargetTriggers.Add(targetKey);
            }

            if (policy.InternalCooldownSeconds > 0f)
            {
                cooldownUntil[cooldownKey] = currentTimeSeconds + policy.InternalCooldownSeconds;
            }

            return true;
        }

        public void BeginRootScope(CombatEventId rootEventId)
        {
            if (!rootEventId.IsValid)
            {
                throw new ArgumentException("Root event ID must be valid.", nameof(rootEventId));
            }

            if (!roots.TryGetValue(rootEventId, out RootState state))
            {
                state = new RootState();
                roots.Add(rootEventId, state);
            }

            state.ActiveScopes++;
        }

        public void EndRootScope(CombatEventId rootEventId)
        {
            if (!roots.TryGetValue(rootEventId, out RootState state))
            {
                return;
            }

            state.ActiveScopes--;
            if (state.ActiveScopes <= 0)
            {
                roots.Remove(rootEventId);
            }
        }

        public bool ReleaseRoot(CombatEventId rootEventId)
        {
            return roots.Remove(rootEventId);
        }

        public void Clear()
        {
            roots.Clear();
            cooldownUntil.Clear();
        }

        private sealed class RootState
        {
            public int ActiveScopes;
            public int TriggerCount;
            public readonly HashSet<EquipmentModifierID> Triggers =
                new HashSet<EquipmentModifierID>();
            public readonly HashSet<RootTargetKey> TargetTriggers =
                new HashSet<RootTargetKey>();
        }

        private readonly struct RootTargetKey : IEquatable<RootTargetKey>
        {
            public RootTargetKey(EquipmentModifierID triggerId, uint targetEntityId)
            {
                TriggerId = triggerId;
                TargetEntityId = targetEntityId;
            }

            private EquipmentModifierID TriggerId { get; }
            private uint TargetEntityId { get; }

            public bool Equals(RootTargetKey other) =>
                TriggerId == other.TriggerId && TargetEntityId == other.TargetEntityId;

            public override bool Equals(object obj) =>
                obj is RootTargetKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (TriggerId.GetHashCode() * 397) ^ (int)TargetEntityId;
                }
            }
        }

        private readonly struct CooldownKey : IEquatable<CooldownKey>
        {
            public CooldownKey(EquipmentModifierID triggerId, uint targetEntityId)
            {
                TriggerId = triggerId;
                TargetEntityId = targetEntityId;
            }

            private EquipmentModifierID TriggerId { get; }
            private uint TargetEntityId { get; }

            public bool Equals(CooldownKey other) =>
                TriggerId == other.TriggerId && TargetEntityId == other.TargetEntityId;

            public override bool Equals(object obj) =>
                obj is CooldownKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (TriggerId.GetHashCode() * 397) ^ (int)TargetEntityId;
                }
            }
        }
    }
}
