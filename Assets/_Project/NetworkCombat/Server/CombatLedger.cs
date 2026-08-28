using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public enum CombatRejectionReason : byte
    {
        None = 0,
        InvalidSender = 1,
        SourceNotOwned = 2,
        TargetNotFound = 3,
        TargetCanonicalDead = 4,
        InvalidDamage = 5,
        DuplicateEvent = 6,
        InvalidSequence = 7,
        AbsoluteInvulnerable = 8,
        WrongAuthority = 9,
        StaleOwnerReport = 10,
        InvalidStatus = 11
    }

    public readonly struct CombatApplyResult
    {
        public CombatApplyResult(
            bool accepted,
            CombatRejectionReason rejection,
            CanonicalEntityState state,
            int appliedDamage,
            bool confirmedKill,
            ConfirmedKill kill)
        {
            Accepted = accepted;
            Rejection = rejection;
            State = state;
            AppliedDamage = appliedDamage;
            IsConfirmedKill = confirmedKill;
            Kill = kill;
        }

        public bool Accepted { get; }
        public CombatRejectionReason Rejection { get; }
        public CanonicalEntityState State { get; }
        public int AppliedDamage { get; }
        public bool IsConfirmedKill { get; }
        public ConfirmedKill Kill { get; }

        public static CombatApplyResult Reject(CombatRejectionReason reason) =>
            new CombatApplyResult(false, reason, default, 0, false, default);
    }

    /// <summary>
    /// Server-owned shared HP/death facts. It deliberately does not contain player
    /// attack stats, crit rolls, projectiles, builds or the full GAS.
    /// </summary>
    public sealed class CombatLedger
    {
        private readonly Dictionary<uint, EntityEntry> entities =
            new Dictionary<uint, EntityEntry>();
        private readonly Dictionary<uint, uint> sourceOwners =
            new Dictionary<uint, uint>();

        public CombatLedger(int maximumDamagePerResult = 100000000)
        {
            if (maximumDamagePerResult < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDamagePerResult));
            }

            MaximumDamagePerResult = maximumDamagePerResult;
        }

        public int MaximumDamagePerResult { get; }
        public int EntityCount => entities.Count;

        public void RegisterSource(uint sourceEntityId, uint ownerPlayerId)
        {
            if (sourceEntityId == 0 || ownerPlayerId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceEntityId));
            }

            sourceOwners[sourceEntityId] = ownerPlayerId;
        }

        public bool UnregisterSource(uint sourceEntityId)
        {
            return sourceOwners.Remove(sourceEntityId);
        }

        public bool IsSourceOwnedBy(uint sourceEntityId, uint playerId)
        {
            return sourceEntityId != 0 &&
                playerId != 0 &&
                sourceOwners.TryGetValue(sourceEntityId, out uint owner) &&
                owner == playerId;
        }

        public CanonicalEntityState RegisterEntity(
            uint entityId,
            int maximumHealth,
            CombatEntityKind kind,
            CombatEntityAuthority authority,
            uint ownerPlayerId = 0)
        {
            if (entityId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityId));
            }

            if (maximumHealth < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumHealth));
            }

            if (authority == CombatEntityAuthority.OwnerFinal && ownerPlayerId == 0)
            {
                throw new ArgumentException("Owner-final entities require an owner player ID.");
            }

            var entry = new EntityEntry
            {
                EntityId = entityId,
                OwnerPlayerId = ownerPlayerId,
                Kind = kind,
                Authority = authority,
                Health = maximumHealth,
                MaxHealth = maximumHealth,
                Alive = true,
                Version = 1
            };
            entities[entityId] = entry;
            return entry.ToState();
        }

        public bool UnregisterEntity(uint entityId)
        {
            return entities.Remove(entityId);
        }

        public bool TryGetState(uint entityId, out CanonicalEntityState state)
        {
            if (entities.TryGetValue(entityId, out EntityEntry entry))
            {
                state = entry.ToState();
                return true;
            }

            state = default;
            return false;
        }

        /// <summary>Creates a point-in-time copy for late-join synchronization.</summary>
        public IReadOnlyList<CanonicalEntityState> GetAllStates()
        {
            var result = new List<CanonicalEntityState>(entities.Count);
            foreach (EntityEntry entry in entities.Values)
            {
                result.Add(entry.ToState());
            }

            return result;
        }

        public bool IsAlive(uint entityId)
        {
            return entities.TryGetValue(entityId, out EntityEntry entry) && entry.Alive;
        }

        public bool SetAbsoluteInvulnerable(uint entityId, bool value)
        {
            if (!entities.TryGetValue(entityId, out EntityEntry entry))
            {
                return false;
            }

            if (entry.AbsoluteInvulnerable != value)
            {
                entry.AbsoluteInvulnerable = value;
                entry.Version++;
            }

            return true;
        }

        public CombatApplyResult Apply(uint senderPlayerId, CombatResult result)
        {
            CombatRejectionReason validation = Validate(senderPlayerId, result);
            if (validation != CombatRejectionReason.None)
            {
                return CombatApplyResult.Reject(validation);
            }

            return ApplyDamage(
                entities[result.TargetEntityId],
                result.Damage,
                result.EventId,
                result.SourcePlayerId);
        }

        public CombatApplyResult ApplyServerStatusDamage(
            uint targetEntityId,
            int damage,
            ulong causeEventId,
            uint sourcePlayerId)
        {
            if (damage < 0 || damage > MaximumDamagePerResult)
            {
                return CombatApplyResult.Reject(CombatRejectionReason.InvalidDamage);
            }

            if (!entities.TryGetValue(targetEntityId, out EntityEntry target))
            {
                return CombatApplyResult.Reject(CombatRejectionReason.TargetNotFound);
            }

            if (target.Authority != CombatEntityAuthority.ServerCanonical)
            {
                return CombatApplyResult.Reject(CombatRejectionReason.WrongAuthority);
            }

            if (!target.Alive)
            {
                return CombatApplyResult.Reject(CombatRejectionReason.TargetCanonicalDead);
            }

            if (target.AbsoluteInvulnerable)
            {
                return CombatApplyResult.Reject(CombatRejectionReason.AbsoluteInvulnerable);
            }

            return ApplyDamage(target, damage, causeEventId, sourcePlayerId);
        }

        public CombatApplyResult ApplyOwnerFinalReport(
            uint senderPlayerId,
            PlayerHealthReport report)
        {
            if (senderPlayerId == 0 || report.PlayerId != senderPlayerId ||
                report.EventId == 0 || report.Sequence == 0 ||
                new MonsterSupergroup.GAS.CombatEventId(report.EventId).Sequence != report.Sequence)
            {
                return CombatApplyResult.Reject(CombatRejectionReason.InvalidSender);
            }

            if (!entities.TryGetValue(report.EntityId, out EntityEntry entry))
            {
                return CombatApplyResult.Reject(CombatRejectionReason.TargetNotFound);
            }

            if (entry.Authority != CombatEntityAuthority.OwnerFinal ||
                entry.OwnerPlayerId != senderPlayerId)
            {
                return CombatApplyResult.Reject(CombatRejectionReason.WrongAuthority);
            }

            if (report.StateVersion <= entry.Version)
            {
                return CombatApplyResult.Reject(CombatRejectionReason.StaleOwnerReport);
            }

            if (report.MaxHealth < 1 || report.Health < 0 || report.Health > report.MaxHealth ||
                report.Alive != (report.Health > 0))
            {
                return CombatApplyResult.Reject(CombatRejectionReason.InvalidDamage);
            }

            entry.MaxHealth = report.MaxHealth;
            entry.Health = report.Health;
            entry.Alive = report.Alive;
            entry.Version = report.StateVersion;
            return new CombatApplyResult(
                true,
                CombatRejectionReason.None,
                entry.ToState(),
                0,
                false,
                default);
        }

        private CombatRejectionReason Validate(uint senderPlayerId, CombatResult result)
        {
            if (senderPlayerId == 0 || result.SourcePlayerId != senderPlayerId)
            {
                return CombatRejectionReason.InvalidSender;
            }

            if (result.EventId == 0 || result.Sequence == 0)
            {
                return CombatRejectionReason.InvalidSequence;
            }

            if (!IsSourceOwnedBy(result.SourceEntityId, senderPlayerId))
            {
                return CombatRejectionReason.SourceNotOwned;
            }

            if (result.Damage < 0 || result.Damage > MaximumDamagePerResult)
            {
                return CombatRejectionReason.InvalidDamage;
            }

            if (!entities.TryGetValue(result.TargetEntityId, out EntityEntry target))
            {
                return CombatRejectionReason.TargetNotFound;
            }

            if (target.Authority != CombatEntityAuthority.ServerCanonical)
            {
                return CombatRejectionReason.WrongAuthority;
            }

            if (!target.Alive)
            {
                return CombatRejectionReason.TargetCanonicalDead;
            }

            return target.AbsoluteInvulnerable
                ? CombatRejectionReason.AbsoluteInvulnerable
                : CombatRejectionReason.None;
        }

        private static CombatApplyResult ApplyDamage(
            EntityEntry target,
            int damage,
            ulong causeEventId,
            uint sourcePlayerId)
        {
            int applied = Math.Min(target.Health, damage);
            target.Health -= applied;
            target.Version++;
            bool confirmedKill = target.Alive && target.Health == 0;
            if (confirmedKill)
            {
                target.Alive = false;
                target.KillerPlayerId = sourcePlayerId;
            }

            CanonicalEntityState state = target.ToState();
            var kill = confirmedKill
                ? new ConfirmedKill
                {
                    CauseEventId = causeEventId,
                    KillerPlayerId = sourcePlayerId,
                    TargetEntityId = target.EntityId,
                    TargetStateVersion = target.Version
                }
                : default;
            return new CombatApplyResult(
                true,
                CombatRejectionReason.None,
                state,
                applied,
                confirmedKill,
                kill);
        }

        private sealed class EntityEntry
        {
            public uint EntityId;
            public uint OwnerPlayerId;
            public CombatEntityKind Kind;
            public CombatEntityAuthority Authority;
            public int Health;
            public int MaxHealth;
            public bool Alive;
            public bool AbsoluteInvulnerable;
            public uint Version;
            public uint KillerPlayerId;

            public CanonicalEntityState ToState()
            {
                return new CanonicalEntityState
                {
                    EntityId = EntityId,
                    OwnerPlayerId = OwnerPlayerId,
                    Kind = (byte)Kind,
                    Authority = (byte)Authority,
                    Health = Health,
                    MaxHealth = MaxHealth,
                    Alive = Alive,
                    AbsoluteInvulnerable = AbsoluteInvulnerable,
                    StateVersion = Version,
                    KillerPlayerId = KillerPlayerId
                };
            }
        }
    }
}
