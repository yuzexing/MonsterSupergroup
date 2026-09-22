using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class ServerCombatGateway
    {
        public const uint MusicCombatId = 0x80000002u;

        // All authoritative effects share the status tick allocator. A separate allocator
        // with the same server source slot would collide with damage and kill receipts.
        private ulong EvidenceCore_NextServerEventId() => serverEventIds.Next().Value;

        private bool EvidenceCore_TryAdmitMusicEffect(uint player, uint source, ulong eventId, double now)
        {
            ValidateServerTime(now);
            var id = new CombatEventId(eventId);
            if (CombatStopped || player == 0 || !Ledger.IsAlive(player) ||
                !Ledger.IsSourceOwnedBy(source, player) || Ledger.IsPlayerSelectingUpgrade(player) ||
                !id.IsValid || id.SourceSlot != ushort.MaxValue || id.Sequence == 0) return false;
            return ProcessedEvents.MarkProcessed(eventId, now);
        }

        // Only called by the server after a beat has been admitted. This never consumes a
        // client damage number or re-runs the player's automatic weapon/GAS modifiers.
        private CombatApplyResult[] EvidenceCore_ProcessMusicDamage(uint player, uint source, uint[] targets,
            int damage, ulong rootId, double now, out CanonicalWorldBatch batch)
        {
            batch = default;
            if (damage <= 0 || damage > Ledger.MaximumDamagePerResult || targets == null ||
                !TryAdmitMusicEffect(player, source, rootId, now)) return Array.Empty<CombatApplyResult>();

            var appliedResults = new List<CombatApplyResult>(targets.Length);
            var states = new List<CanonicalEntityState>(targets.Length);
            var statuses = new List<CanonicalStatusState>();
            var kills = new List<ConfirmedKill>();
            var hits = new List<EnemyHitPresentation>(targets.Length);
            var unique = new HashSet<uint>();
            foreach (uint target in targets)
            {
                if (!unique.Add(target) || !Ledger.TryGetState(target, out var before) ||
                    before.Kind != (byte)CombatEntityKind.Enemy) continue;
                // This effect is resolved by the server, so its local immunity rules still apply.
                if (before.AbsoluteInvulnerable) { Metrics.Reject(CombatRejectionReason.AbsoluteInvulnerable); continue; }
                var id = serverEventIds.Next();
                var result = new CombatResult
                {
                    EventId = id.Value, RootEventId = rootId, ParentEventId = rootId,
                    Sequence = id.Sequence, SourcePlayerId = player, SourceEntityId = source,
                    TargetEntityId = target, TargetStateVersion = before.StateVersion,
                    AbilityId = MusicCombatId, DamageSourceId = MusicCombatId,
                    Damage = damage, DamageTags = (ulong)(CombatTags.Hit | CombatTags.Damage),
                    PresentationDamageType = (byte)DamageType.Lightning
                };
                var applied = Ledger.Apply(player, result);
                if (!applied.Accepted) { Metrics.Reject(applied.Rejection); continue; }
                ProcessedEvents.MarkProcessed(id.Value, now);
                Metrics.AcceptedCombatResults++;
                appliedResults.Add(applied);
                states.Add(applied.State);
                RecordDamage(result);
                AddEnemyHit(result, applied, hits);
                CombatResultAccepted?.Invoke(result, applied, now);
                if (applied.IsConfirmedKill)
                {
                    AddConfirmedKill(applied.Kill, kills);
                    statuses.AddRange(Statuses.RemoveTarget(target));
                }
            }
            batch = CreateBatch(states, statuses, kills, hits);
            return appliedResults.ToArray();
        }
    }
}
