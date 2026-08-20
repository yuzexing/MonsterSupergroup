using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Spawners;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression
{
	public class EnemyBurstSpawnerClip : EnemyClip
	{
		private EnemySpawnerBehaviour template = new EnemySpawnerBehaviour();

		public uint enemyAmount = 10u;

		public EnemyController enemyPrefab;

		public MultipleEnemySpawner.SpawnShapeOptions spawnShapeOptions;

		[SerializeField]
		protected bool allowRubberBand = true;

		[SerializeField]
		protected bool rubberBandKillsOnClipEnd = true;

		[SerializeField]
		private bool isTrap = true;

		public bool AllowRubberBand => allowRubberBand;

		public bool RubberBandKillsOnClipEnd => rubberBandKillsOnClipEnd;

		public ClipCaps clipCaps => ClipCaps.None;

		public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
		{
			ScriptPlayable<EnemySpawnerBehaviour> scriptPlayable = ScriptPlayable<EnemySpawnerBehaviour>.Create(graph, template);
			if ((bool)enemyPrefab)
			{
				sprite = enemyPrefab.spriteRenderer.sprite;
			}
			switch (spawnShapeOptions)
			{
			case MultipleEnemySpawner.SpawnShapeOptions.Triangle:
				while (enemyAmount % 3 != 0)
				{
					enemyAmount++;
				}
				break;
			case MultipleEnemySpawner.SpawnShapeOptions.Rectangle:
				while (enemyAmount % 4 != 0)
				{
					enemyAmount++;
				}
				break;
			}
			return scriptPlayable;
		}

		public override void ProcessClip(ProgressionTimeline timeline, TimelineClip clip)
		{
			MultipleEnemySpawner multipleEnemySpawner = Object.Instantiate(timeline.multipleEnemySpawner, timeline.transform);
			multipleEnemySpawner.enemyCount = (int)enemyAmount;
			multipleEnemySpawner.enemyPrefab = enemyPrefab;
			multipleEnemySpawner.spawnShapeOptions = spawnShapeOptions;
			multipleEnemySpawner.variantIdx = variantIndex;
			multipleEnemySpawner.AllowRubberBand = AllowRubberBand;
			multipleEnemySpawner.isTrap = isTrap;
			if (multipleEnemySpawner.AllowRubberBand)
			{
				multipleEnemySpawner.RubberbandKillsEnemiesOnClipEnd = RubberBandKillsOnClipEnd;
			}
			timeline.CreateMilestone(multipleEnemySpawner, clip);
		}
	}
}
