using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed class ProcessedEventCache
    {
        private readonly int capacity;
        private readonly double retentionSeconds;
        private readonly HashSet<ulong> ids = new HashSet<ulong>();
        private readonly Queue<Entry> expiryOrder = new Queue<Entry>();

        public ProcessedEventCache(int capacity = 262144, double retentionSeconds = 120)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (double.IsNaN(retentionSeconds) ||
                double.IsInfinity(retentionSeconds) ||
                retentionSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(retentionSeconds));
            }

            this.capacity = capacity;
            this.retentionSeconds = retentionSeconds;
        }

        public int Count => ids.Count;
        public int Capacity => capacity;

        public bool IsProcessed(ulong eventId, double now)
        {
            ValidateTime(now);
            EvictExpired(now);
            return eventId != 0UL && ids.Contains(eventId);
        }

        public bool MarkProcessed(ulong eventId, double now)
        {
            ValidateTime(now);
            if (eventId == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(eventId));
            }

            EvictExpired(now);
            if (!ids.Add(eventId))
            {
                return false;
            }

            expiryOrder.Enqueue(new Entry(eventId, now + retentionSeconds));
            while (ids.Count > capacity && expiryOrder.Count > 0)
            {
                ids.Remove(expiryOrder.Dequeue().EventId);
            }

            return true;
        }

        public void Clear()
        {
            ids.Clear();
            expiryOrder.Clear();
        }

        private void EvictExpired(double now)
        {
            while (expiryOrder.Count > 0 && expiryOrder.Peek().ExpiresAt <= now)
            {
                ids.Remove(expiryOrder.Dequeue().EventId);
            }
        }

        private static void ValidateTime(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now))
            {
                throw new ArgumentOutOfRangeException(nameof(now));
            }
        }

        private readonly struct Entry
        {
            public Entry(ulong eventId, double expiresAt)
            {
                EventId = eventId;
                ExpiresAt = expiresAt;
            }

            public ulong EventId { get; }
            public double ExpiresAt { get; }
        }
    }

    public sealed class ClientEventIdentityRegistry
    {
        private readonly Dictionary<uint, Identity> identities =
            new Dictionary<uint, Identity>();

        public void Register(uint playerId, ushort sourceSlot, ushort connectionEpoch)
        {
            if (playerId == 0 || sourceSlot == 0 || connectionEpoch == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(playerId));
            }

            identities[playerId] = new Identity(sourceSlot, connectionEpoch);
        }

        public bool Unregister(uint playerId)
        {
            return identities.Remove(playerId);
        }

        public bool Validate(uint playerId, ulong eventId, uint declaredSequence)
        {
            var id = new CombatEventId(eventId);
            if (!id.IsValid || id.Sequence == 0 || id.Sequence != declaredSequence)
            {
                return false;
            }

            return !identities.TryGetValue(playerId, out Identity identity) ||
                (id.SourceSlot == identity.SourceSlot &&
                 id.ConnectionEpoch == identity.ConnectionEpoch);
        }

        private readonly struct Identity
        {
            public Identity(ushort sourceSlot, ushort connectionEpoch)
            {
                SourceSlot = sourceSlot;
                ConnectionEpoch = connectionEpoch;
            }

            public ushort SourceSlot { get; }
            public ushort ConnectionEpoch { get; }
        }
    }

    public sealed class ClientBatchSequenceTracker
    {
        private readonly uint maximumForwardJump;
        private readonly Dictionary<uint, uint> highestSequences =
            new Dictionary<uint, uint>();

        public ClientBatchSequenceTracker(uint maximumForwardJump = 1000000)
        {
            if (maximumForwardJump == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumForwardJump));
            }

            this.maximumForwardJump = maximumForwardJump;
        }

        public bool Accept(uint playerId, uint sequence)
        {
            if (playerId == 0 || sequence == 0)
            {
                return false;
            }

            if (!highestSequences.TryGetValue(playerId, out uint highest))
            {
                highestSequences[playerId] = sequence;
                return true;
            }

            if (sequence > highest)
            {
                if ((ulong)sequence - highest > maximumForwardJump)
                {
                    return false;
                }

                highestSequences[playerId] = sequence;
            }

            // Older/repeated batches are allowed through because per-event idempotency
            // safely handles packet reordering and retransmission.
            return true;
        }

        public void Remove(uint playerId)
        {
            highestSequences.Remove(playerId);
        }
    }
}
