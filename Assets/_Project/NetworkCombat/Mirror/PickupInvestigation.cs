using System;
using System.Globalization;
using MonsterSupergroup.NetworkCombat.Diagnostics;

namespace MonsterSupergroup.NetworkCombat
{
    // Local diagnostic evidence only. No field in this envelope travels on the gameplay protocol.
    internal static class PickupInvestigation
    {
        public static bool Enabled => CombatInvestigationEvidence.Enabled;
        public static void Capture(string stage, string outcome, string run, ulong drop, uint claim,
            uint entity, uint avatar, Func<object> data, string reason = null, ulong participant = 0)
        {
            if (!Enabled) return;
            CombatInvestigationEvidence.Capture("pickup." + stage, outcome, () => new {
                schemaVersion = 1, dropId = drop.ToString(CultureInfo.InvariantCulture), claimVersion = claim,
                requestedRun = run, participantId = participant.ToString(CultureInfo.InvariantCulture),
                entity, avatar, data = data?.Invoke()
            }, reason, avatar, entity);
        }
        public static object Progress(NetworkModifierSelection value) => value == null ? null : new {
            level = value.Level, experience = value.Experience, pendingUpgrades = value.PendingUpgradeCount,
            selecting = value.IsSelecting, pendingEventId = value.PendingEventId.ToString(CultureInfo.InvariantCulture)
        };
    }
}
