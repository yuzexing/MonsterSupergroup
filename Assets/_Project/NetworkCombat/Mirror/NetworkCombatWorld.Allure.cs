using System;
using Mirror;
namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class NetworkCombatWorld
    {
        private AllureParameters? allureSession;
        internal AllureParameters GetAllureParameters(AllurePrototypeConfig config)
        {
            if (!allureSession.HasValue)
            {
                var initial = config != null ? config.parameters : AllureParameters.Defaults;
                if (!initial.IsValid) throw new InvalidOperationException("Invalid Allure parameters.");
                allureSession = initial;
            }
            return allureSession.Value;
        }
        [Server]
        public void ServerConfigureAllure(AllureParameters settings, bool resetCooldowns = false)
        {
            if (!MonsterSupergroup.Builds.BuildFeatures.DevelopmentToolsAllowed) return;
            if (!settings.IsValid) throw new ArgumentException("Invalid Allure parameters.");
            allureSession = settings;
            foreach (var identity in NetworkServer.spawned.Values)
                if (identity != null && identity.TryGetComponent<NetworkPlayerAllure>(out var skill))
                    skill.ApplyServerParameters(settings, resetCooldowns);
        }
    }
}
