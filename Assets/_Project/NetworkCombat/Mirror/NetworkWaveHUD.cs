using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.UI;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WaveProgressHUD))]
    public sealed class NetworkWaveHUD : MonoBehaviour
    {
        private WaveProgressHUD view;
        public WaveProgressSnapshot DisplayedSnapshot { get; private set; }
        private void Awake() => view = GetComponent<WaveProgressHUD>();
        private void LateUpdate()
        {
            if (GameplayRuntimeEnvironment.IsDedicatedServer || !NetworkClient.isConnected)
            { Clear(); return; }
            var world = NetworkCombatWorld.Instance;
            var progress = world != null ? world.GetComponent<NetworkWaveProgress>() : null;
            DisplayedSnapshot = progress != null && progress.isClient ? progress.Snapshot : default;
            view.Present(Format(DisplayedSnapshot));
        }
        public static string Format(WaveProgressSnapshot state)
        {
            if (state.Phase == WavePhase.Disabled) return "Waiting for wave state";
            if (state.Phase == WavePhase.Waiting) return "Waiting for host / server to start the run";
            if (state.Phase == WavePhase.Stopped) return "Run stopped";
            string phase = state.Phase == WavePhase.Paused ? "Paused - no active players" : "Next wave";
            return $"Wave {state.Wave}  |  {phase}: {state.Remaining:F1}s\n" +
                $"Alive {state.Alive}/{state.Limit}  |  Spawned {state.Spawned}/{state.Planned}  |  Skipped {state.Skipped}";
        }
        private void Clear() { DisplayedSnapshot = default; if (view != null) view.Present(string.Empty); }
        private void OnDisable() => Clear();
    }
}
