using System;

namespace MonsterSupergroup.GAS
{
    internal static class ModifierRollEvidence
    {
        public static void Record(RuntimeEquipmentModifier modifier, CombatContext context, ICombatTarget target, float chance, float roll)
        {
            try
            {
                CombatEvidence.Event("Owner", "owner.modifier_roll", roll < chance ? "Accepted" : "Rejected", roll < chance ? "RollBelowChance" : "RollAtOrAboveChance",
                    context.EventId.Value, context.SourcePlayerId, context.TargetEntityId,
                    new { modifier = ModifierEvidence.Capture(modifier), chance, roll },
                    new { alive = target.IsAlive, health = CombatCalculationEvidence.Health(target) },
                    root: context.RootEventId.Value, parent: context.ParentEventId.Value, bytes: 1024);
            }
            catch (Exception error) { CombatEvidence.Event("Owner", "owner.modifier_roll", "CaptureFailed", error.GetType().Name,
                context.EventId.Value, context.SourcePlayerId, context.TargetEntityId, root: context.RootEventId.Value); }
        }
    }
}
