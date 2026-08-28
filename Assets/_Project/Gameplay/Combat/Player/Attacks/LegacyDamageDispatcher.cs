using AstralShift.HellMaiden.AI.Enemy;
using CombatTags = MonsterSupergroup.GAS.CombatTags;

namespace AstralShift.HellMaiden.Player.Attacks
{
    /// <summary>Preserves legacy IDamageable compatibility while carrying GAS provenance.</summary>
    public static class LegacyDamageDispatcher
    {
        public static void Damage(
            IDamageable damageable,
            int value,
            DamageType damageType,
            LegacyDamageSource source,
            CombatTags tags)
        {
            if (damageable == null)
            {
                return;
            }

            if (damageable is EnemyHurtbox enemyHurtbox)
            {
                enemyHurtbox.Damage(value, damageType, source.WithTags(tags));
                return;
            }

            damageable.Damage(value, damageType);
        }
    }
}
