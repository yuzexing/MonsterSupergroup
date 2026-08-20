using System;

namespace AstralShift.HellMaiden.Player.Attacks
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
			burnDamageMultiplier = 0f;
			poisonDamageMultiplier = 0f;
			bleedDamageMultiplier = 0f;
			statusGeneralMultiplier = 0f;
			meleeDamageMultiplier = 0f;
			rangedDamageMultiplier = 0f;
			playerFullHealthMultiplier = 0f;
			knockBackMultiplier = 0f;
		}

		private void AddOp(AttackStatsMultipliers other)
		{
			damage += other.damage;
			critRate += other.critRate;
			critDamage += other.critDamage;
			speed += other.speed;
			size += other.size;
			duration += other.duration;
			projectileCountIncrement += other.projectileCountIncrement;
			pristineDamageMultiplier += other.pristineDamageMultiplier;
			contactDamageReceivedMultiplier += other.contactDamageReceivedMultiplier;
			projectileDamageReceivedMultiplier += other.projectileDamageReceivedMultiplier;
			eliteDamageMultiplier += other.eliteDamageMultiplier;
			meleeDamageMultiplier += other.meleeDamageMultiplier;
			rangedDamageMultiplier += other.rangedDamageMultiplier;
			burnDamageMultiplier += other.burnDamageMultiplier;
			poisonDamageMultiplier += other.poisonDamageMultiplier;
			bleedDamageMultiplier += other.bleedDamageMultiplier;
			statusGeneralMultiplier += other.statusGeneralMultiplier;
			playerFullHealthMultiplier += other.playerFullHealthMultiplier;
			knockBackMultiplier += other.knockBackMultiplier;
		}

		private void AddOp(float value)
		{
			damage += value;
			critRate += value;
			critDamage += value;
			speed += value;
			size += value;
			duration += value;
			projectileCountIncrement += (int)value;
			pristineDamageMultiplier += value;
			contactDamageReceivedMultiplier += value;
			projectileDamageReceivedMultiplier += value;
			eliteDamageMultiplier += value;
			meleeDamageMultiplier += value;
			rangedDamageMultiplier += value;
			burnDamageMultiplier += value;
			poisonDamageMultiplier += value;
			bleedDamageMultiplier += value;
			statusGeneralMultiplier += value;
			playerFullHealthMultiplier += value;
			knockBackMultiplier += value;
		}

		private void SubtractOp(AttackStatsMultipliers other)
		{
			damage -= other.damage;
			critRate -= other.critRate;
			critDamage -= other.critDamage;
			speed -= other.speed;
			size -= other.size;
			duration -= other.duration;
			projectileCountIncrement -= other.projectileCountIncrement;
			pristineDamageMultiplier -= other.pristineDamageMultiplier;
			contactDamageReceivedMultiplier -= other.contactDamageReceivedMultiplier;
			projectileDamageReceivedMultiplier -= other.projectileDamageReceivedMultiplier;
			eliteDamageMultiplier -= other.eliteDamageMultiplier;
			meleeDamageMultiplier -= other.meleeDamageMultiplier;
			rangedDamageMultiplier -= other.rangedDamageMultiplier;
			burnDamageMultiplier -= other.burnDamageMultiplier;
			poisonDamageMultiplier -= other.poisonDamageMultiplier;
			bleedDamageMultiplier -= other.bleedDamageMultiplier;
			statusGeneralMultiplier -= other.statusGeneralMultiplier;
			playerFullHealthMultiplier -= other.playerFullHealthMultiplier;
			knockBackMultiplier -= other.knockBackMultiplier;
		}

		private void SubtractOp(float value)
		{
			damage -= value;
			critRate -= value;
			critDamage -= value;
			speed -= value;
			size -= value;
			duration -= value;
			projectileCountIncrement -= (int)value;
			pristineDamageMultiplier -= value;
			contactDamageReceivedMultiplier -= value;
			projectileDamageReceivedMultiplier -= value;
			eliteDamageMultiplier -= value;
			meleeDamageMultiplier -= value;
			rangedDamageMultiplier -= value;
			burnDamageMultiplier -= value;
			poisonDamageMultiplier -= value;
			bleedDamageMultiplier -= value;
			statusGeneralMultiplier -= value;
			playerFullHealthMultiplier -= value;
			knockBackMultiplier -= value;
		}

		private void MultiplyOp(AttackStatsMultipliers other)
		{
			damage *= other.damage;
			critRate *= other.critRate;
			critDamage *= other.critDamage;
			speed *= other.speed;
			size *= other.size;
			duration *= other.duration;
			projectileCountIncrement *= other.projectileCountIncrement;
			pristineDamageMultiplier *= other.pristineDamageMultiplier;
			contactDamageReceivedMultiplier *= other.contactDamageReceivedMultiplier;
			projectileDamageReceivedMultiplier *= other.projectileDamageReceivedMultiplier;
			eliteDamageMultiplier *= other.eliteDamageMultiplier;
			meleeDamageMultiplier *= other.meleeDamageMultiplier;
			rangedDamageMultiplier *= other.rangedDamageMultiplier;
			burnDamageMultiplier *= other.burnDamageMultiplier;
			poisonDamageMultiplier *= other.poisonDamageMultiplier;
			bleedDamageMultiplier *= other.bleedDamageMultiplier;
			statusGeneralMultiplier *= other.statusGeneralMultiplier;
			playerFullHealthMultiplier *= other.playerFullHealthMultiplier;
			knockBackMultiplier *= other.knockBackMultiplier;
		}

		private void MultiplyOp(float value)
		{
			damage *= value;
			critRate *= value;
			critDamage *= value;
			speed *= value;
			size *= value;
			duration *= value;
			int num = (int)Math.Round((float)projectileCountIncrement * value, MidpointRounding.AwayFromZero);
			projectileCountIncrement = num;
			pristineDamageMultiplier *= value;
			contactDamageReceivedMultiplier *= value;
			projectileDamageReceivedMultiplier *= value;
			eliteDamageMultiplier *= value;
			meleeDamageMultiplier *= value;
			rangedDamageMultiplier *= value;
			burnDamageMultiplier *= value;
			poisonDamageMultiplier *= value;
			bleedDamageMultiplier *= value;
			statusGeneralMultiplier *= value;
			playerFullHealthMultiplier *= value;
			knockBackMultiplier *= value;
		}

		private void DivideOp(AttackStatsMultipliers other)
		{
			damage /= other.damage;
			critRate /= other.critRate;
			critDamage /= other.critDamage;
			speed /= other.speed;
			size /= other.size;
			duration /= other.duration;
			projectileCountIncrement /= other.projectileCountIncrement;
			pristineDamageMultiplier /= other.pristineDamageMultiplier;
			contactDamageReceivedMultiplier /= other.contactDamageReceivedMultiplier;
			projectileDamageReceivedMultiplier /= other.projectileDamageReceivedMultiplier;
			eliteDamageMultiplier /= other.eliteDamageMultiplier;
			meleeDamageMultiplier /= other.meleeDamageMultiplier;
			rangedDamageMultiplier /= other.rangedDamageMultiplier;
			burnDamageMultiplier /= other.burnDamageMultiplier;
			poisonDamageMultiplier /= other.poisonDamageMultiplier;
			bleedDamageMultiplier /= other.bleedDamageMultiplier;
			statusGeneralMultiplier /= other.statusGeneralMultiplier;
			playerFullHealthMultiplier /= other.playerFullHealthMultiplier;
			knockBackMultiplier /= other.knockBackMultiplier;
		}

		private void DivideOp(float value)
		{
			damage /= value;
			critRate /= value;
			critDamage /= value;
			speed /= value;
			size /= value;
			duration /= value;
			int num = (int)Math.Round((float)projectileCountIncrement / value, MidpointRounding.AwayFromZero);
			projectileCountIncrement = num;
			pristineDamageMultiplier /= value;
			contactDamageReceivedMultiplier /= value;
			projectileDamageReceivedMultiplier /= value;
			eliteDamageMultiplier /= value;
			meleeDamageMultiplier /= value;
			rangedDamageMultiplier /= value;
			burnDamageMultiplier /= value;
			poisonDamageMultiplier /= value;
			bleedDamageMultiplier /= value;
			statusGeneralMultiplier /= value;
			playerFullHealthMultiplier /= value;
			knockBackMultiplier /= value;
		}

		public static AttackStatsMultipliers operator +(AttackStatsMultipliers a, AttackStatsMultipliers b)
		{
			a.AddOp(b);
			return a;
		}

		public static AttackStatsMultipliers operator +(AttackStatsMultipliers a, float b)
		{
			a.AddOp(b);
			return a;
		}

		public static AttackStatsMultipliers operator -(AttackStatsMultipliers a, AttackStatsMultipliers b)
		{
			a.SubtractOp(b);
			return a;
		}

		public static AttackStatsMultipliers operator -(AttackStatsMultipliers a, float b)
		{
			a.SubtractOp(b);
			return a;
		}

		public static AttackStatsMultipliers operator *(AttackStatsMultipliers a, AttackStatsMultipliers b)
		{
			a.MultiplyOp(b);
			return a;
		}

		public static AttackStatsMultipliers operator *(AttackStatsMultipliers a, float b)
		{
			a.MultiplyOp(b);
			return a;
		}

		public static AttackStatsMultipliers operator /(AttackStatsMultipliers a, AttackStatsMultipliers b)
		{
			a.DivideOp(b);
			return a;
		}

		public static AttackStatsMultipliers operator /(AttackStatsMultipliers a, float b)
		{
			a.DivideOp(b);
			return a;
		}
	}
}
