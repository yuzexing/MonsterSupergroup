using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;
using MonsterSupergroup.Gameplay.Combat;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Short-lived permits for dash weapons, backed by this avatar's server charge runtime.</summary>
    public sealed class ServerDashUseRegistry
    {
        private readonly Dictionary<ulong, Use> uses = new Dictionary<ulong, Use>();
        private readonly List<ulong> expired = new List<ulong>();
        private uint lastSequence;
        public PlayerDashRuntime Runtime { get; } = new PlayerDashRuntime();
        public int PendingUseCount => uses.Count;

        // Identity, life state, Build and motion validation belong to the network boundary.
        public bool TryConsume(ulong useId, uint buildRevision, double now, float duration,
            float recharge, uint[] dashWeaponIds)
        {
            var id = new CombatEventId(useId);
            if (!id.IsValid || id.Sequence <= lastSequence || buildRevision == 0 ||
                dashWeaponIds == null || dashWeaponIds.Length != PlayerBuildRuntime.HandSlotCount ||
                !Finite(now) || now < 0 || !Finite(duration) || duration < 0 ||
                !Finite(recharge) || recharge < 0) return false;
            Prune(now);
            // Absorb one frame of receipt jitter without shortening any accepted use interval.
            if (now + 0.05d < Runtime.NextUseAt) return false;
            double acceptedAt = Math.Max(now, Runtime.NextUseAt);
            if (!Runtime.TryConsume(acceptedAt, duration, recharge)) return false;
            uses.Add(useId, new Use(buildRevision, now + 2d, (uint[])dashWeaponIds.Clone()));
            lastSequence = id.Sequence;
            return true;
        }

        public bool CanAdmitWeapon(ulong useId, uint revision, int slot, uint weaponId, double now)
        {
            if (!Finite(now) || now < 0d) return false;
            Prune(now);
            return (uint)slot < PlayerBuildRuntime.HandSlotCount && weaponId != 0 &&
                uses.TryGetValue(useId, out Use use) && use.Revision == revision &&
                use.Weapons[slot] == weaponId && (use.ConsumedSlots & (1 << slot)) == 0;
        }

        public void MarkWeaponAdmitted(ulong useId, int slot)
        {
            if ((uint)slot >= PlayerBuildRuntime.HandSlotCount || !uses.TryGetValue(useId, out Use use))
                throw new InvalidOperationException("Dash weapon admission requires a current dash permit.");
            use.ConsumedSlots |= 1 << slot;
        }

        public void Prune(double now)
        {
            if (!Finite(now) || now < 0d) return;
            expired.Clear();
            foreach (var item in uses) if (item.Value.ExpiresAt < now) expired.Add(item.Key);
            foreach (ulong key in expired) uses.Remove(key);
        }

        public void ClearPermits() => uses.Clear();
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private sealed class Use
        {
            public readonly uint Revision;
            public readonly double ExpiresAt;
            public readonly uint[] Weapons;
            public int ConsumedSlots;
            public Use(uint revision, double expiresAt, uint[] weapons)
            { Revision = revision; ExpiresAt = expiresAt; Weapons = weapons; }
        }
    }
}
