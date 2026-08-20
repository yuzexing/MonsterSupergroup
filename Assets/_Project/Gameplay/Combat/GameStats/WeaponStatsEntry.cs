using AstralShift.HellMaiden.Player.Attacks;

namespace AstralShift.HellMaiden.GameStats
{
	public class WeaponStatsEntry : StatEntry
	{
		public uint ID { get; private set; }

		public WeaponBehaviour weaponBehaviour { get; private set; }

		public float TotalDamage { get; private set; }

		public float CriticalDamage { get; private set; }

		public int TotalHits { get; private set; }

		public int EnemyDeaths { get; private set; }

		public void SetWeaponId(uint id)
		{
			ID = id;
		}

		public void LinkWeaponEvents(WeaponBehaviour weaponBehaviour)
		{
			this.weaponBehaviour = weaponBehaviour;
			weaponBehaviour.OnWeaponDamage += UpdateDamage;
			weaponBehaviour.OnWeaponHit += RegisterHit;
		}

		public void UpdateDamage(float value, bool critical)
		{
			RunStatsTracker.Instance.PlayerStatsEntry.RegisterDamageDealt(value);
			TotalDamage += value;
			if (critical)
			{
				CriticalDamage += value;
			}
		}

		public void RegisterHit()
		{
			TotalHits++;
		}

		public void RegisterEnemyDeath()
		{
			EnemyDeaths++;
		}

		public override void CompareHighScores(StatEntry statEntry)
		{
			if (statEntry != null && statEntry is WeaponStatsEntry weaponStatsEntry)
			{
				if (TotalHits < weaponStatsEntry.TotalHits)
				{
					TotalHits = weaponStatsEntry.TotalHits;
				}
				if (TotalDamage < weaponStatsEntry.TotalDamage)
				{
					TotalDamage = weaponStatsEntry.TotalDamage;
				}
				if (CriticalDamage < weaponStatsEntry.CriticalDamage)
				{
					CriticalDamage = weaponStatsEntry.CriticalDamage;
				}
			}
		}

		public override void JoinStatsEntries(StatEntry statEntry)
		{
			if (statEntry != null && statEntry is WeaponStatsEntry weaponStatsEntry)
			{
				TotalDamage += weaponStatsEntry.TotalDamage;
				CriticalDamage += weaponStatsEntry.CriticalDamage;
				TotalHits += weaponStatsEntry.TotalHits;
			}
		}

		public override void CleanLinkedEvents()
		{
			if ((bool)weaponBehaviour)
			{
				weaponBehaviour.OnWeaponDamage -= UpdateDamage;
				weaponBehaviour.OnWeaponHit -= RegisterHit;
			}
			weaponBehaviour = null;
		}

		public override void CleanEntry()
		{
			ID = 0u;
			TotalDamage = 0f;
			CriticalDamage = 0f;
			TotalHits = 0;
		}
	}
}
