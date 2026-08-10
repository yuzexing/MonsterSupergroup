using System;

namespace MonsterSupergroup.GAS
{
    [Serializable]
    public class AttackStatsMultipliers
    {
        public float damage;
        public float critRate;
        public float critDamage;
        public float speed;
        public float size;
        public float duration;
        public int projectileCountIncrement;
        public float pristineDamageMultiplier;
        public float contactDamageReceivedMultiplier;
        public float projectileDamageReceivedMultiplier;
        public float eliteDamageMultiplier;
        public float meleeDamageMultiplier;
        public float rangedDamageMultiplier;
        public float burnDamageMultiplier;
        public float poisonDamageMultiplier;
        public float bleedDamageMultiplier;
        public float statusGeneralMultiplier;
        public float playerFullHealthMultiplier;
        public float knockBackMultiplier;

        public void Reset()
        {
            damage = 0f;
            critRate = 0f;
            critDamage = 0f;
            speed = 0f;
            size = 0f;
            duration = 0f;
            projectileCountIncrement = 0;
            pristineDamageMultiplier = 0f;
            contactDamageReceivedMultiplier = 0f;
            projectileDamageReceivedMultiplier = 0f;
            eliteDamageMultiplier = 0f;
            meleeDamageMultiplier = 0f;
            rangedDamageMultiplier = 0f;
            burnDamageMultiplier = 0f;
            poisonDamageMultiplier = 0f;
            bleedDamageMultiplier = 0f;
            statusGeneralMultiplier = 0f;
            playerFullHealthMultiplier = 0f;
            knockBackMultiplier = 0f;
        }

        public void CopyFrom(AttackStatsMultipliers source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            damage = source.damage;
            critRate = source.critRate;
            critDamage = source.critDamage;
            speed = source.speed;
            size = source.size;
            duration = source.duration;
            projectileCountIncrement = source.projectileCountIncrement;
            pristineDamageMultiplier = source.pristineDamageMultiplier;
            contactDamageReceivedMultiplier = source.contactDamageReceivedMultiplier;
            projectileDamageReceivedMultiplier = source.projectileDamageReceivedMultiplier;
            eliteDamageMultiplier = source.eliteDamageMultiplier;
            meleeDamageMultiplier = source.meleeDamageMultiplier;
            rangedDamageMultiplier = source.rangedDamageMultiplier;
            burnDamageMultiplier = source.burnDamageMultiplier;
            poisonDamageMultiplier = source.poisonDamageMultiplier;
            bleedDamageMultiplier = source.bleedDamageMultiplier;
            statusGeneralMultiplier = source.statusGeneralMultiplier;
            playerFullHealthMultiplier = source.playerFullHealthMultiplier;
            knockBackMultiplier = source.knockBackMultiplier;
        }

        public void AddFrom(AttackStatsMultipliers source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            damage += source.damage;
            critRate += source.critRate;
            critDamage += source.critDamage;
            speed += source.speed;
            size += source.size;
            duration += source.duration;
            projectileCountIncrement += source.projectileCountIncrement;
            pristineDamageMultiplier += source.pristineDamageMultiplier;
            contactDamageReceivedMultiplier += source.contactDamageReceivedMultiplier;
            projectileDamageReceivedMultiplier += source.projectileDamageReceivedMultiplier;
            eliteDamageMultiplier += source.eliteDamageMultiplier;
            meleeDamageMultiplier += source.meleeDamageMultiplier;
            rangedDamageMultiplier += source.rangedDamageMultiplier;
            burnDamageMultiplier += source.burnDamageMultiplier;
            poisonDamageMultiplier += source.poisonDamageMultiplier;
            bleedDamageMultiplier += source.bleedDamageMultiplier;
            statusGeneralMultiplier += source.statusGeneralMultiplier;
            playerFullHealthMultiplier += source.playerFullHealthMultiplier;
            knockBackMultiplier += source.knockBackMultiplier;
        }

        public AttackStatsMultipliers Clone()
        {
            var clone = new AttackStatsMultipliers();
            clone.CopyFrom(this);
            return clone;
        }
    }
}
