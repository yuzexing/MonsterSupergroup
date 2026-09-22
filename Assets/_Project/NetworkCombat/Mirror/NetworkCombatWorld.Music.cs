using System;
using Mirror;
namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkCombatWorld
    {
        private MusicParameters? musicSession;
        internal MusicParameters GetMusicParameters(MusicPrototypeConfig config)
        {
            if (!musicSession.HasValue)
            {
                var initial = config != null ? config.parameters : MusicParameters.Defaults;
                if (!initial.IsValid) throw new InvalidOperationException("Invalid Music parameters.");
                musicSession = initial;
            }
            return musicSession.Value;
        }
        [Server]
        public void ServerConfigureMusic(MusicParameters settings, bool resetCooldowns = false)
        {
            if (!settings.IsValid) throw new ArgumentException("Invalid Music parameters.");
            musicSession = settings;
            foreach (var identity in NetworkServer.spawned.Values)
                if (identity != null && identity.TryGetComponent<NetworkPlayerMusic>(out var skill))
                    skill.ApplyServerParameters(settings, resetCooldowns);
        }
    }
}
