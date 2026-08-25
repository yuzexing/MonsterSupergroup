using System;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    [Serializable]
    public struct CombatResult
    {
        public ulong EventId;
        public ulong RootEventId;
        public ulong ParentEventId;
        public uint Sequence;
        public ushort ChainDepth;
        public uint SourcePlayerId;
        public uint SourceEntityId;
        public uint TargetEntityId;
        public uint AbilityId;
        public uint BuildId;
        public int Damage;
        public ulong DamageTags;
        public uint TargetStateVersion;

        public static CombatResult From(CombatEvent combatEvent)
        {
            if (combatEvent.Kind != CombatEventKind.DamageResolved)
            {
                throw new ArgumentException(
                    "Only DamageResolved events can become a CombatResult.",
                    nameof(combatEvent));
            }

            CombatContext context = combatEvent.Context;
            return new CombatResult
            {
                EventId = context.EventId.Value,
                RootEventId = context.RootEventId.Value,
                ParentEventId = context.ParentEventId.Value,
                Sequence = context.Sequence,
                ChainDepth = context.ChainDepth,
                SourcePlayerId = context.SourcePlayerId,
                SourceEntityId = context.SourceEntityId,
                TargetEntityId = context.TargetEntityId,
                AbilityId = context.AbilityId,
                BuildId = context.BuildId,
                Damage = combatEvent.ResolvedDamage.Value,
                DamageTags = (ulong)context.Tags,
                TargetStateVersion = context.TargetStateVersion
            };
        }

        public CombatEventId GetEventId() => new CombatEventId(EventId);
    }

    [Serializable]
    public struct CombatSubmissionBatch
    {
        public uint BatchSequence;
        public CombatResult[] Results;
        public StatusMutation[] StatusMutations;
        public PlayerHealthReport[] PlayerHealthReports;

        public int ResultCount => Results?.Length ?? 0;
        public int StatusMutationCount => StatusMutations?.Length ?? 0;
        public int PlayerHealthReportCount => PlayerHealthReports?.Length ?? 0;
    }

    [Serializable]
    public struct PlayerHealthReport
    {
        public ulong EventId;
        public uint Sequence;
        public uint PlayerId;
        public uint EntityId;
        public int Health;
        public int MaxHealth;
        public bool Alive;
        public uint StateVersion;
    }
}
