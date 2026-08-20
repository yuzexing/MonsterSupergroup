using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.HellMaiden.Interactions
{
	public class WindInteraction : Interaction
	{
		[SerializeField]
		public float magnitude = 1.5f;

		public Vector2 windDirection = Vector2.right;

		private bool _isPaused;

		public override void Interact(IInteractor interactor)
		{
			if (!_isPaused)
			{
				base.Interact(interactor);
				GameDirector.Instance.Player.WindForce = windDirection * magnitude;
				OnEnd();
			}
		}

		private void OnTriggerEnter2D(Collider2D other)
		{
			if (!_isPaused)
			{
				EnemyController componentInParent = other.GetComponentInParent<EnemyController>();
				if ((bool)componentInParent && componentInParent.enemyFlyingType)
				{
					componentInParent.windDirection = windDirection * magnitude;
				}
			}
		}

		private void OnTriggerExit2D(Collider2D other)
		{
			if (!_isPaused)
			{
				EnemyController componentInParent = other.GetComponentInParent<EnemyController>();
				if ((bool)componentInParent && componentInParent.enemyFlyingType)
				{
					componentInParent.windDirection = windDirection * magnitude;
				}
			}
		}

		public void SetPaused(bool paused)
		{
			_isPaused = paused;
			if ((bool)GameDirector.Instance && (bool)GameDirector.Instance.Player)
			{
				if (paused)
				{
					GameDirector.Instance.Player.WindForce = Vector2.zero;
				}
				else
				{
					GameDirector.Instance.Player.WindForce = windDirection * magnitude;
				}
			}
		}
	}
}
