using System;
using System.Collections.Generic;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed class EnemySnapshotBuffer
    {
        private readonly List<EnemySimulationSnapshot> snapshots;
        private readonly int capacity;

        public EnemySnapshotBuffer(int capacity = 8)
        {
            if (capacity < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            this.capacity = capacity;
            snapshots = new List<EnemySimulationSnapshot>(capacity);
        }

        public int Count => snapshots.Count;

        public void Clear()
        {
            snapshots.Clear();
        }

        public bool Push(EnemySimulationSnapshot snapshot)
        {
            if (!snapshot.IsFinite)
            {
                return false;
            }

            bool discontinuity =
                (snapshot.Flags & EnemySimulationSnapshotFlags.Discontinuity) != 0;
            if (snapshots.Count > 0)
            {
                EnemySimulationSnapshot newest = snapshots[snapshots.Count - 1];
                if (snapshot.AssignmentEpoch == newest.AssignmentEpoch)
                {
                    if (!EnemySimulationSequence.IsNewer(
                            snapshot.Sequence,
                            newest.Sequence) ||
                        snapshot.SampleNetworkTime <= newest.SampleNetworkTime)
                    {
                        return false;
                    }
                }
                else
                {
                    if (!EnemySimulationSequence.IsNewer(
                            snapshot.AssignmentEpoch,
                            newest.AssignmentEpoch))
                    {
                        return false;
                    }
                    discontinuity = true;
                }
            }

            if (discontinuity)
            {
                snapshots.Clear();
            }

            snapshots.Add(snapshot);
            if (snapshots.Count > capacity)
            {
                snapshots.RemoveAt(0);
            }

            return true;
        }

        public bool TrySample(
            double renderNetworkTime,
            double maximumExtrapolation,
            out Vector2 position,
            out Vector2 facing)
        {
            if (snapshots.Count == 0)
            {
                position = default;
                facing = default;
                return false;
            }

            while (snapshots.Count > 2 &&
                   snapshots[1].SampleNetworkTime <= renderNetworkTime)
            {
                snapshots.RemoveAt(0);
            }

            EnemySimulationSnapshot first = snapshots[0];
            if (snapshots.Count >= 2)
            {
                EnemySimulationSnapshot second = snapshots[1];
                double duration = second.SampleNetworkTime - first.SampleNetworkTime;
                if (duration > 0d && renderNetworkTime <= second.SampleNetworkTime)
                {
                    float t = Mathf.Clamp01((float)(
                        (renderNetworkTime - first.SampleNetworkTime) / duration));
                    position = Vector2.LerpUnclamped(first.Position, second.Position, t);
                    facing = Vector2.Lerp(first.Facing, second.Facing, t).normalized;
                    return true;
                }
            }

            EnemySimulationSnapshot newest = snapshots[snapshots.Count - 1];
            double extrapolation = Math.Max(
                0d,
                Math.Min(maximumExtrapolation, renderNetworkTime - newest.SampleNetworkTime));
            position = newest.Position + newest.Velocity * (float)extrapolation;
            facing = newest.Facing;
            return true;
        }
    }
}
