using System;

namespace MonsterSupergroup.Gameplay.Combat
{
    /// <summary>Headless server role, independent of batch mode used by client tests.</summary>
    public static class GameplayRuntimeEnvironment
    {
        public static bool IsDedicatedServer
        {
            get
            {
#if UNITY_SERVER
                return true;
#else
                return HasDedicatedServerArgument(Environment.GetCommandLineArgs());
#endif
            }
        }

        public static bool HasDedicatedServerArgument(string[] arguments)
        {
            if (arguments == null) return false;
            foreach (string argument in arguments)
                if (string.Equals(argument, "--dedicated-server", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
