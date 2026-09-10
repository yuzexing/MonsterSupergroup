using Mirror;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    /// <summary>One read-only replicated snapshot on the existing Boot network World.</summary>
    [DisallowMultipleComponent]
    public sealed class NetworkWaveProgress : NetworkBehaviour
    {
        [SyncVar] private WaveProgressSnapshot snapshot;
        private NetworkGameplayEnemySpawner producer;
        public WaveProgressSnapshot Snapshot => snapshot;

        [Server]
        internal bool TryClaim(NetworkGameplayEnemySpawner source)
        {
            if (producer != null && producer != source) return false;
            producer = source; return true;
        }

        [Server]
        internal void Publish(NetworkGameplayEnemySpawner source, WaveProgressSnapshot value)
        {
            if (producer == source) snapshot = value;
        }

        internal void Release(NetworkGameplayEnemySpawner source)
        {
            if (producer != source) return;
            if (NetworkServer.active)
            {
                var stopped = snapshot; stopped.Phase = WavePhase.Stopped; snapshot = stopped;
            }
            producer = null;
        }

        public override void OnStartServer() { snapshot = default; producer = null; }
        public override void OnStopServer()
        {
            if (producer != null) producer.StopWaveRun();
            producer = null; snapshot = default;
        }
        public override void OnStopClient() { if (!NetworkServer.active) snapshot = default; }
    }
}
