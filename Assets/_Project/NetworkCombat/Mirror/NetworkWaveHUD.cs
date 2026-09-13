using MonsterSupergroup.Gameplay.Options;
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
            if (state.Phase == WavePhase.Disabled) return MenuLocalization.Get("ui.wave.syncing");
            if (state.Phase == WavePhase.Waiting) return MenuLocalization.Get("ui.wave.waiting");
            if (state.Phase == WavePhase.Stopped) return MenuLocalization.Get("ui.wave.stopped");
            string phase = MenuLocalization.Get(state.Phase == WavePhase.Paused ? "ui.wave.paused" : "ui.wave.next");
            return MenuLocalization.Get("ui.wave.progress", state.Wave, phase, state.Remaining, state.Alive, state.Limit, state.Spawned, state.Planned, state.Skipped);
        }
        private void Clear() { DisplayedSnapshot = default; if (view != null) view.Present(string.Empty); }
        private void OnDisable() => Clear();
    }
}
