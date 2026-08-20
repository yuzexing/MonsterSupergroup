using System;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Spawners;
using AstralShift.HellMaiden.Common;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression
{
	[Serializable]
	public class EnemySpawnerClip : EnemyClip, ITimelineClipAsset
	{
		private EnemySpawnerBehaviour template = new EnemySpawnerBehaviour();

		public uint maxEnemies = 10u;

		public EnemyController enemyPrefab;

		public AnimationCurve spawnCurve;

		public Direction direction;

		public float angle;

		[SerializeField]
		protected bool allowRubberBand = true;

		[SerializeField]
		protected bool rubberBandKillsOnClipEnd = true;

		public bool AllowRubberBand => allowRubberBand;

		public bool RubberBandKillsOnClipEnd => rubberBandKillsOnClipEnd;

		public ClipCaps clipCaps => ClipCaps.None;

		public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
		{
			ScriptPlayable<EnemySpawnerBehaviour> scriptPlayable = ScriptPlayable<EnemySpawnerBehaviour>.Create(graph, template);
			scriptPlayable.GetBehaviour();
			if (enemyPrefab != null)
			{
				sprite = enemyPrefab.spriteRenderer.sprite;
			}
			return scriptPlayable;
		}

		public override void ProcessClip(ProgressionTimeline timeline, TimelineClip clip)
		{
			EnemyContinuousSpawner enemyContinuousSpawner = UnityEngine.Object.Instantiate(timeline.enemyContinuousSpawner, timeline.transform);
			enemyContinuousSpawner.direction = direction;
			enemyContinuousSpawner.angle = angle;
			enemyContinuousSpawner.enemyCount = (int)maxEnemies;
			enemyContinuousSpawner.enemyPrefab = enemyPrefab;
			enemyContinuousSpawner.spawnCurve = spawnCurve;
			enemyContinuousSpawner.variantIdx = variantIndex;
			enemyContinuousSpawner.AllowRubberBand = AllowRubberBand;
			if (enemyContinuousSpawner.AllowRubberBand)
			{
				enemyContinuousSpawner.RubberbandKillsEnemiesOnClipEnd = RubberBandKillsOnClipEnd;
			}
			timeline.CreateMilestone(enemyContinuousSpawner, clip);
		}
	}
}
