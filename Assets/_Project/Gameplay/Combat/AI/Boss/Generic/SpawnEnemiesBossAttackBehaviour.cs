using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Boss.Minos;
using AstralShift.HellMaiden.Combat.Spawners;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss.Generic
{
	public class SpawnEnemiesBossAttackBehaviour : BossAttackBehaviour
	{
		[SerializeField]
		protected MinosMovementController movementController;

		[SerializeField]
		protected Transform centerPosition;

		[SerializeField]
		private List<EnemySpawner> spawners;

		[SerializeField]
		protected List<Animator> visualEffects;

		[Header("Sounds")]
		[SerializeField]
		private EventReference spawnSound;

		private void OnEnable()
		{
			foreach (Animator visualEffect in visualEffects)
			{
				visualEffect.gameObject.SetActive(value: false);
			}
		}

		public override void Positioning()
		{
			movementController.StopMovement();
			movementController.SetDestination(centerPosition.position, onPositioningEnd);
			movementController.ResumeMovement();
		}

		public override void Warning()
		{
			movementController.StopMovement();
			BarkWarning();
			WarningBossAnimation(onWarningEnd);
		}

		public override void Attack()
		{
			AttackBossAnimation(onAttackEnd);
			if (!spawnSound.IsNull)
			{
				RuntimeManager.PlayOneShot(spawnSound, base.transform.position);
			}
			for (int i = 0; i < spawners.Count; i++)
			{
				spawners[i].Init();
				if (i < visualEffects.Count)
				{
					Animator animator = visualEffects[i];
					animator.gameObject.SetActive(value: true);
					animator.Rebind();
					animator.Play(0);
				}
			}
		}
	}
}
