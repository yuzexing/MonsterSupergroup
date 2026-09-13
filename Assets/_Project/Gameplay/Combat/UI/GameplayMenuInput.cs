using System;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Combat
{
    /// <summary>Local presentation bridge; never pauses simulation or grants invulnerability.</summary>
    public static class GameplayMenuInput
    {
        public static bool IsOpen { get; private set; }
        public static event Action ToggleRequested;
        public static void RequestToggle() => ToggleRequested?.Invoke();
        public static void SetOpen(bool open) => IsOpen = open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { IsOpen = false; ToggleRequested = null; }
    }
}
