using System;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.Gameplay.Combat
{
    /// <summary>
    /// Transport-neutral owner identity and event services inherited by every weapon
    /// created dynamically by PlayerHand. Offline play may omit this value.
    /// </summary>
    public sealed class CombatRuntimeServices
    {
        public CombatRuntimeServices(
            uint sourcePlayerId,
            uint sourceEntityId,
            ICombatEventIdSource eventIds,
            ICombatEventSink eventSink,
            CombatTriggerGuard triggerGuard = null,
            ICombatTimeSource timeSource = null)
        {
            if (sourcePlayerId == 0 || sourceEntityId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourcePlayerId));
            }

            SourcePlayerId = sourcePlayerId;
            SourceEntityId = sourceEntityId;
            EventIds = eventIds ?? throw new ArgumentNullException(nameof(eventIds));
            EventSink = eventSink ?? throw new ArgumentNullException(nameof(eventSink));
            TriggerGuard = triggerGuard ?? new CombatTriggerGuard();
            TimeSource = timeSource ?? MonotonicCombatTimeSource.Instance;
        }

        public uint SourcePlayerId { get; }
        public uint SourceEntityId { get; }
        public ICombatEventIdSource EventIds { get; }
        public ICombatEventSink EventSink { get; }
        public CombatTriggerGuard TriggerGuard { get; }
        public ICombatTimeSource TimeSource { get; }

        public void Configure(WeaponRuntimeBehaviour weapon)
        {
            if (weapon == null)
            {
                throw new ArgumentNullException(nameof(weapon));
            }

            weapon.ConfigureCombatIdentity(SourcePlayerId, SourceEntityId);
        }
    }
}
