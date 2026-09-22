using Mirror;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkCombatWorld
    {
        public bool PrototypesEnabled { get; private set; } = true;

        [Server]
        public void ServerConfigurePrototypesEnabled(bool enabled)
        {
            PrototypesEnabled = enabled;
            foreach (var identity in NetworkServer.spawned.Values)
                if (identity != null && identity.TryGetComponent<NetworkPlayerPrototypeAbilities>(out var abilities))
                    abilities.ServerSetEnabled(enabled);
        }
    }
}
