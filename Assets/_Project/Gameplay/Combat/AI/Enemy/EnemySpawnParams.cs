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

		public Action OnKill;
	}
}
