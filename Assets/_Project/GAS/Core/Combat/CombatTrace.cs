using System;

namespace MonsterSupergroup.GAS
{
    public enum CombatTraceKind : byte
    {
        AttackStarted = 0,
        HitResolved = 1,
        DamageResolved = 2,
        PredictedLethalHit = 3,
        ConfirmedKill = 4,
        StatusAdded = 5,
        StatusUpdated = 6,
        StatusRemoved = 7
    }

    public readonly struct CombatTraceEntry
    {
        public CombatTraceEntry(
            CombatTraceKind kind,
            CombatEventId eventId,
            CombatEventId rootEventId,
            CombatEventId parentEventId,
            uint sourcePlayerId,
            uint sourceEntityId,
            uint targetEntityId,
            uint abilityId,
            uint buildId,
            CombatTags tags,
            int damage,
            uint stateVersion,
            ushort chainDepth)
        {
            Kind = kind;
            EventId = eventId;
            RootEventId = rootEventId;
            ParentEventId = parentEventId;
            SourcePlayerId = sourcePlayerId;
            SourceEntityId = sourceEntityId;
            TargetEntityId = targetEntityId;
            AbilityId = abilityId;
            BuildId = buildId;
            Tags = tags;
            Damage = damage;
            StateVersion = stateVersion;
            ChainDepth = chainDepth;
        }

        public CombatTraceKind Kind { get; }
        public CombatEventId EventId { get; }
        public CombatEventId RootEventId { get; }
        public CombatEventId ParentEventId { get; }
        public uint SourcePlayerId { get; }
        public uint SourceEntityId { get; }
        public uint TargetEntityId { get; }
        public uint AbilityId { get; }
        public uint BuildId { get; }
        public CombatTags Tags { get; }
        public int Damage { get; }
        public uint StateVersion { get; }
        public ushort ChainDepth { get; }
    }

    /// <summary>
    /// Fixed-capacity development trace. Recording does not allocate after construction;
    /// Snapshot allocation is explicit and intended for diagnostics only.
    /// </summary>
    public sealed class CombatTraceRecorder : ICombatEventSink
    {
        private readonly CombatTraceEntry[] entries;
        private int nextIndex;
        private int count;

        public CombatTraceRecorder(int capacity = 4096)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            entries = new CombatTraceEntry[capacity];
        }

        public int Capacity => entries.Length;
        public int Count => count;

        public void Publish(CombatEvent combatEvent)
        {
            CombatContext context = combatEvent.Context;
            Append(new CombatTraceEntry(
                (CombatTraceKind)combatEvent.Kind,
                context.EventId,
                context.RootEventId,
                context.ParentEventId,
                context.SourcePlayerId,
                context.SourceEntityId,
                context.TargetEntityId,
                context.AbilityId,
                context.BuildId,
                context.Tags,
                combatEvent.ResolvedDamage.Value,
                context.TargetStateVersion,
                context.ChainDepth));
        }

        public void RecordStatus(StatusChange change, CombatEventId eventId)
        {
            StatusInstance instance = change.Instance;
            CombatContext source = instance.SourceContext;
            CombatTraceKind kind = change.Kind == StatusChangeKind.Added
                ? CombatTraceKind.StatusAdded
                : change.Kind == StatusChangeKind.Updated
                    ? CombatTraceKind.StatusUpdated
                    : CombatTraceKind.StatusRemoved;
            Append(new CombatTraceEntry(
                kind,
                eventId,
                source.IsValid ? source.RootEventId : eventId,
                source.IsValid ? source.EventId : CombatEventId.None,
                instance.SourcePlayerId,
                instance.SourceEntityId,
                instance.TargetEntityId,
                source.IsValid ? source.AbilityId : 0,
                source.IsValid ? source.BuildId : 0,
                source.IsValid ? source.Tags | CombatTags.Status : CombatTags.Status,
                instance.TickDamage,
                instance.Version,
                source.IsValid && source.ChainDepth < ushort.MaxValue
                    ? (ushort)(source.ChainDepth + 1)
                    : (ushort)0));
        }

        public void RecordResolvedDamage(CombatContext context, int damage)
        {
            if (!context.IsValid)
            {
                throw new ArgumentException("Trace context must be valid.", nameof(context));
            }

            Append(new CombatTraceEntry(
                CombatTraceKind.DamageResolved,
                context.EventId,
                context.RootEventId,
                context.ParentEventId,
                context.SourcePlayerId,
                context.SourceEntityId,
                context.TargetEntityId,
                context.AbilityId,
                context.BuildId,
                context.Tags,
                damage,
                context.TargetStateVersion,
                context.ChainDepth));
        }

        public void RecordConfirmedKill(
            CombatEventId causeEventId,
            uint killerPlayerId,
            uint targetEntityId,
            uint stateVersion)
        {
            Append(new CombatTraceEntry(
                CombatTraceKind.ConfirmedKill,
                causeEventId,
                causeEventId,
                CombatEventId.None,
                killerPlayerId,
                0,
                targetEntityId,
                0,
                0,
                CombatTags.ConfirmedKill,
                0,
                stateVersion,
                0));
        }

        public CombatTraceEntry[] Snapshot()
        {
            var snapshot = new CombatTraceEntry[count];
            int start = count == entries.Length ? nextIndex : 0;
            for (int i = 0; i < count; i++)
            {
                snapshot[i] = entries[(start + i) % entries.Length];
            }

            return snapshot;
        }

        public void Clear()
        {
            Array.Clear(entries, 0, entries.Length);
            nextIndex = 0;
            count = 0;
        }

        private void Append(CombatTraceEntry entry)
        {
            entries[nextIndex] = entry;
            nextIndex = (nextIndex + 1) % entries.Length;
            if (count < entries.Length)
            {
                count++;
            }
        }
    }

    public sealed class CompositeCombatEventSink : ICombatEventSink
    {
        private readonly ICombatEventSink first;
        private readonly ICombatEventSink second;

        public CompositeCombatEventSink(ICombatEventSink first, ICombatEventSink second)
        {
            this.first = first ?? throw new ArgumentNullException(nameof(first));
            this.second = second ?? throw new ArgumentNullException(nameof(second));
        }

        public void Publish(CombatEvent combatEvent)
        {
            first.Publish(combatEvent);
            second.Publish(combatEvent);
        }
    }
}
