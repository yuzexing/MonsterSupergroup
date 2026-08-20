using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Spawners;
using AstralShift.HellMaiden.Common;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression
{
	public class EnemyDirectionalSpawnerClip : EnemyClip
	{
		private EnemySpawnerBehaviour template = new EnemySpawnerBehaviour();

		public uint enemyAmount = 10u;

		public EnemyController enemyPrefab;

		public Direction direction;

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
			if (enemyPrefab != null)
			{
				sprite = enemyPrefab.spriteRenderer.sprite;
			}
			return scriptPlayable;
		}

		public override void ProcessClip(ProgressionTimeline timeline, TimelineClip clip)
		{
			DirectionalEnemySpawner directionalEnemySpawner = Object.Instantiate(timeline.directionalEnemySpawner, timeline.transform);
			directionalEnemySpawner.enemyCount = (int)enemyAmount;
			directionalEnemySpawner.enemyPrefab = enemyPrefab;
			directionalEnemySpawner.direction = direction;
			directionalEnemySpawner.variantIdx = variantIndex;
			directionalEnemySpawner.AllowRubberBand = AllowRubberBand;
			if (directionalEnemySpawner.AllowRubberBand)
			{
				directionalEnemySpawner.RubberbandKillsEnemiesOnClipEnd = RubberBandKillsOnClipEnd;
			}
			timeline.CreateMilestone(directionalEnemySpawner, clip);
		}
	}
}
