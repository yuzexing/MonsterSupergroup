using System;

namespace MonsterSupergroup.NetworkCombat
{
    public static class PickupAudit
    {
        public static event Action<string, string, ulong, string> Recorded;
        public static void Emit(string kind, string run, ulong drop, string detail) => Recorded?.Invoke(kind, run, drop, detail);
    }
}
