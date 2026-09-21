using System;
using Mirror;
namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkCombatWorld
    {
        private GluttonyParameters? gluttonySession;
        internal GluttonyParameters GetGluttonyParameters(GluttonyPrototypeConfig config)
        {
            if (!gluttonySession.HasValue)
            {
                var initial = config != null ? config.parameters : GluttonyParameters.Defaults;
                if (!initial.IsValid) throw new InvalidOperationException("Invalid Gluttony parameters.");
                gluttonySession = initial;
            }
            return gluttonySession.Value;
        }
        [Server]
        public void ServerConfigureGluttony(GluttonyParameters settings, bool resetCooldowns = false)
        {
            if (!settings.IsValid) throw new ArgumentException("Invalid Gluttony parameters.");
            gluttonySession = settings;
            foreach (var identity in NetworkServer.spawned.Values)
                if (identity != null && identity.TryGetComponent<NetworkPlayerGluttony>(out var skill))
                    skill.ApplyServerParameters(settings, resetCooldowns);
        }
        [Server]
        internal CombatApplyResult ServerDevour(uint player, uint source, uint target, ulong eventId)
        {
            var result = Gateway.ProcessGluttonyDevour(player, source, target, eventId, NetworkTime.time, out var batch);
            if (result.Accepted) Broadcast(batch);
            return result;
        }
    }
}
