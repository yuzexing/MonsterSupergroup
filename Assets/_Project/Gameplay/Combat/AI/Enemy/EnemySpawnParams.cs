using System;
using AstralShift.Pooling;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public struct EnemySpawnParams
	{
		public EnemyController Prefab;

		public int ID;

		public GenericPooler<EnemyController> Pool;

		public Transform AttackTarget;

		public Vector2 SpawnPosition;

		public int VariantIdx;

		public Vector2 SpeedMultiplierRange;

		public bool AllowRubberBand;

		public bool RubberbandKillsEnemiesOnClipEnd;

		public float EndTime;

		/// <summary>
		/// Optional per-spawn stat customization applied after EnemyStats.Reset and
		/// before CombatantBehaviour receives EffectiveMaxHealth.
		/// </summary>
		public Action<EnemyStats> ConfigureStatsBeforeCombatant;

		public Action OnConfirmedKill;
	}
}
