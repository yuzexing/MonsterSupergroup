using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;

namespace AstralShift.HellMaiden.GameStats
{
	public class WeaponStatsEntry : StatEntry, IOutputStatisticsEvidence
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
			string engine = CombatOutputEvidence.Register(this);
			var before = CaptureOutputStatistics();
			var input = new OutputStatisticInput { weaponId = ID, value = value, critical = critical, metric = "ComputedDamage" };
			var after = OutputStatistics.ApplyDamage(before, input);
			TotalDamage = after.totalDamage;
			CriticalDamage = after.criticalDamage;
			CombatOutputEvidence.Record(engine, input, before, after);
		}

		public OutputStatisticsState CaptureOutputStatistics() => new OutputStatisticsState { totalDamage = TotalDamage, criticalDamage = CriticalDamage };

		public void RegisterHit()
		{
			int before = TotalHits;
			TotalHits++;
			if (CombatEvidence.Enabled) CombatEvidence.Event("Owner", "stats.hit", "Applied", "ContactCounter",
				CombatOutputEvidence.Current.EventId.Value, CombatOutputEvidence.Current.SourcePlayerId, CombatOutputEvidence.Current.TargetEntityId,
				new { weaponId = ID, increment = 1 }, before, TotalHits, CombatOutputEvidence.Current.RootEventId.Value, bytes: 512);
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
