using System;
using AstralShift.HellMaiden.AI.Boss;
using AstralShift.HellMaiden.AI.Boss.Minos;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy.Boss.Libicocco.Attacks
{
	public class LibicoccoClone : MonoBehaviour
	{
		private static readonly int Out = Animator.StringToHash("Out");

		[SerializeField]
		private MinosMovementController movementController;

		[SerializeField]
		private Shooter shooter;

		[SerializeField]
		private BossAnimator animator;

		[SerializeField]
		private Animator ballAnim;

		private void OnEnable()
		{
			ballAnim.Rebind();
		}

		public void SetDestination(Vector3 destination, Action onEnd = null, float speed = 30f)
		{
			movementController.SetDestination(destination, onEnd, speed);
		}

		public void Shoot()
		{
			shooter.ShootBullets();
		}

		public void AssignController(BossController controller)
		{
			movementController.enemyController = controller;
		}

		public void Despawn(Action onEnd = null)
		{
			animator.Attack(0f, 0f, onEnd);
			ballAnim.SetTrigger(Out);
		}
	}
}
