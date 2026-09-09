using System;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>Server-observed timing only; restored into the existing owned weapon behaviour.</summary>
    [Serializable]
    public struct PlayerWeaponCooldownSnapshot
    {
        public int SlotIndex;
        public uint WeaponId;
        public ulong LastAttackEventId;
        public double ServerReceivedAt;
        public double ReadyAt;
        public float CooldownSeconds;
        /// <summary>Launch delay frozen when this root began; zero for old checkpoints and instant attacks.</summary>
        public float SequenceSeconds;

        public bool IsValid => SlotIndex >= 0 && SlotIndex < Gameplay.Combat.PlayerBuildRuntime.HandSlotCount &&
            WeaponId != 0 && LastAttackEventId != 0 && IsFinite(ServerReceivedAt) && IsFinite(ReadyAt) &&
            IsFinite(CooldownSeconds) && CooldownSeconds >= 0f &&
            IsFinite(SequenceSeconds) && SequenceSeconds >= 0f;

        public double RemainingAt(double networkTime) => Math.Max(0d, ReadyAt - networkTime);

        /// <summary>An interrupted/shortened sequence still pays the full ordinary cooldown after its actual end.</summary>
        public bool TryCompleteSequence(ulong rootEventId, double completedAt, float minimumSequenceSeconds,
            out PlayerWeaponCooldownSnapshot completed)
        {
            completed = this;
            if (!IsValid || rootEventId != LastAttackEventId || !IsFinite(completedAt) ||
                !IsFinite(minimumSequenceSeconds) || minimumSequenceSeconds < 0f || CooldownSeconds <= 0f) return false;
            double startedAt = ReadyAt - CooldownSeconds - SequenceSeconds;
            double end = Math.Max(startedAt + minimumSequenceSeconds, completedAt);
            if (end - startedAt > float.MaxValue) return false;
            completed.SequenceSeconds = (float)(end - startedAt);
            completed.ReadyAt = end + CooldownSeconds;
            return true;
        }

        /// <summary>Keep elapsed attack time when stats change the existing weapon's interval.</summary>
        public PlayerWeaponCooldownSnapshot WithCooldown(float currentCooldown)
        {
            if (!IsFinite(currentCooldown) || currentCooldown <= 0f)
                throw new ArgumentOutOfRangeException(nameof(currentCooldown));
            // Old checkpoints did not record an interval, so their saved deadline is authoritative.
            if (!IsValid || CooldownSeconds == 0f) return this;
            var adjusted = this;
            adjusted.ReadyAt += (double)currentCooldown - CooldownSeconds;
            adjusted.CooldownSeconds = currentCooldown;
            return adjusted;
        }

        /// <summary>
        /// Admission uses a bounded owner timestamp and charges every accepted attack a full
        /// cooldown. Tolerance absorbs clock/frame jitter without granting a faster sustained rate.
        /// </summary>
        public static bool TryAdmit(int slotIndex, uint weaponId, ulong attackEventId,
            double ownerAttackTime, double serverTime, float currentCooldown,
            PlayerWeaponCooldownSnapshot previous, out PlayerWeaponCooldownSnapshot snapshot,
            float sequenceSeconds = 0f)
        {
            snapshot = default;
            if (!IsFinite(sequenceSeconds) || sequenceSeconds < 0f ||
                !IsFinite(ownerAttackTime) || !IsFinite(serverTime) ||
                ownerAttackTime < serverTime - 2d || ownerAttackTime > serverTime + 0.1d ||
                !TryObserve(slotIndex, weaponId, attackEventId, ownerAttackTime, serverTime,
                    currentCooldown, previous, out var observed)) return false;
            double start = Math.Min(serverTime, ownerAttackTime);
            double earliest = start;
            if (previous.IsValid && previous.WeaponId == weaponId && previous.SlotIndex == slotIndex)
            {
                earliest = previous.WithCooldown(currentCooldown).ReadyAt;
                double tolerance = Math.Min(0.1d, currentCooldown * 0.1d);
                if (start + tolerance < earliest) return false;
            }
            observed.ReadyAt = Math.Max(start, earliest) + currentCooldown + sequenceSeconds;
            observed.CooldownSeconds = currentCooldown;
            observed.SequenceSeconds = sequenceSeconds;
            snapshot = observed;
            return true;
        }

        /// <summary>
        /// The owner still executes attacks. The server bounds the observed timing by its current
        /// weapon cooldown and clock; this is not the future attack-frequency adjudicator.
        /// Identity/epoch ownership is checked by the caller using ClientEventIdentityRegistry.
        /// </summary>
        public static bool TryObserve(int slotIndex, uint weaponId, ulong attackEventId,
            double ownerAttackTime, double serverTime, float currentCooldown,
            PlayerWeaponCooldownSnapshot previous, out PlayerWeaponCooldownSnapshot snapshot)
        {
            snapshot = default;
            if (slotIndex < 0 || slotIndex >= Gameplay.Combat.PlayerBuildRuntime.HandSlotCount ||
                weaponId == 0 || attackEventId == 0 || !IsFinite(ownerAttackTime) || !IsFinite(serverTime) ||
                !IsFinite(currentCooldown) || currentCooldown <= 0f) return false;
            var incoming = new CombatEventId(attackEventId);
            var prior = new CombatEventId(previous.LastAttackEventId);
            if (incoming.Sequence == 0 || (previous.WeaponId == weaponId && previous.SlotIndex == slotIndex &&
                incoming.SourceSlot == prior.SourceSlot && incoming.ConnectionEpoch == prior.ConnectionEpoch &&
                incoming.Sequence <= prior.Sequence)) return false;
            double start = Math.Max(serverTime - currentCooldown, Math.Min(serverTime, ownerAttackTime));
            double ready = start + currentCooldown;
            if (previous.WeaponId == weaponId && previous.SlotIndex == slotIndex && previous.IsValid)
                ready = Math.Min(serverTime + currentCooldown, Math.Max(previous.ReadyAt, ready));
            snapshot = new PlayerWeaponCooldownSnapshot
            {
                SlotIndex = slotIndex, WeaponId = weaponId, LastAttackEventId = attackEventId,
                ServerReceivedAt = serverTime, ReadyAt = ready, CooldownSeconds = currentCooldown
            };
            return true;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
