using System;
using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkEnemySimulationWorld
    {
        [SyncVar] private bool referenceClockEnabled;
        [SyncVar] private double referenceCombatAnchor, referenceNetworkAnchor;
        [SyncVar(hook = nameof(ApplyReferenceClockRate))] private float referenceClockRate = 1;
        private double serverCombatTime, nextClockPublish;
        public bool UsesReferenceClock => referenceClockEnabled;
        public double CombatTime => !referenceClockEnabled ? EnemySimulationClock.Now : isServer ? serverCombatTime :
            referenceCombatAnchor + Math.Max(0, EnemySimulationClock.Now - referenceNetworkAnchor) * referenceClockRate;

        [Server]
        internal void BeginReferenceClock()
        {
            if (referenceClockEnabled) return;
            referenceClockEnabled = true;
            serverCombatTime = NetworkTime.time;
            PublishReferenceClock();
        }

        private void UpdateReferenceClock()
        {
            if (!referenceClockEnabled) return;
            serverCombatTime += Time.deltaTime;
            if (!Mathf.Approximately(referenceClockRate, Time.timeScale) || NetworkTime.time >= nextClockPublish)
                PublishReferenceClock();
        }

        private void PublishReferenceClock()
        {
            referenceCombatAnchor = serverCombatTime;
            referenceNetworkAnchor = NetworkTime.time;
            referenceClockRate = Time.timeScale;
            nextClockPublish = NetworkTime.time + .1;
        }

        private void ApplyReferenceClockRate(float previous, float current)
        {
            if (!isServer && referenceClockEnabled) Time.timeScale = current;
        }

        private void ClearReferenceClock()
        {
            if (referenceClockEnabled && !isServer) Time.timeScale = 1;
            referenceClockEnabled = false;
        }
    }
}
