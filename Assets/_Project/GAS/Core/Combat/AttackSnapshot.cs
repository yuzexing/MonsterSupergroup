using System;

namespace MonsterSupergroup.GAS
{
    public sealed class AttackSnapshot : IDisposable
    {
        private RuntimeModifierExecutionSnapshot execution;
        private int referenceCount = 1;
        private bool ownerReferenceReleased;

        internal AttackSnapshot(
            IWeaponRuntime weapon,
            AttackStatsSnapshot stats,
            CombatContext context,
            RuntimeModifierExecutionSnapshot execution)
        {
            Weapon = weapon ?? throw new ArgumentNullException(nameof(weapon));
            CombatId = weapon.CombatId;
            Stats = stats;
            Context = context.IsValid
                ? context
                : throw new ArgumentException("Attack context must be valid.", nameof(context));
            this.execution = execution ?? throw new ArgumentNullException(nameof(execution));
        }

        public IWeaponRuntime Weapon { get; }

        public uint CombatId { get; }

        public AttackStatsSnapshot Stats { get; }

        public CombatContext Context { get; }

        internal RuntimeModifierExecutionSnapshot Execution
        {
            get
            {
                EnsureAlive();
                return execution;
            }
        }

        public AttackSnapshotLease Retain()
        {
            EnsureAlive();
            referenceCount++;
            return new AttackSnapshotLease(this);
        }

        public void Dispose()
        {
            if (ownerReferenceReleased)
            {
                return;
            }

            ownerReferenceReleased = true;
            ReleaseReference();
        }

        internal void EnsureAlive()
        {
            if (referenceCount <= 0 || execution == null)
            {
                throw new ObjectDisposedException(nameof(AttackSnapshot));
            }
        }

        internal void ReleaseReference()
        {
            if (referenceCount <= 0)
            {
                return;
            }

            referenceCount--;
            if (referenceCount == 0)
            {
                execution.Dispose();
                execution = null;
            }
        }
    }

    public sealed class AttackSnapshotLease : IDisposable
    {
        private AttackSnapshot snapshot;

        internal AttackSnapshotLease(AttackSnapshot snapshot)
        {
            this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public AttackSnapshot Snapshot
        {
            get
            {
                if (snapshot == null)
                {
                    throw new ObjectDisposedException(nameof(AttackSnapshotLease));
                }

                snapshot.EnsureAlive();
                return snapshot;
            }
        }

        public void Dispose()
        {
            AttackSnapshot current = snapshot;
            snapshot = null;
            current?.ReleaseReference();
        }
    }
}
