using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	[RequireComponent(typeof(Collider2D))]
	public class PlayerAttackHitBox : BaseAttackHitBox
	{
		[Space]
		[SerializeField]
		private bool triggerOnce = true;

		[SerializeField]
		protected float timeoutAfterExit = 0.3f;

		protected virtual void OnTriggerEnter2D(Collider2D other)
		{
			if (other.TryGetComponent<IDamageable>(out var component))
			{
				int iD = component.GetID();
				TryCancelPendingRemoval(iD);
				if (_hitEntries.Add(iD))
				{
					_onHit?.Invoke(component);
				}
			}
		}

		protected virtual void OnTriggerExit2D(Collider2D other)
		{
			if (other.TryGetComponent<IDamageable>(out var component) && !triggerOnce)
			{
				RemoveEntryAsync(component.GetID(), timeoutAfterExit).Forget();
			}
		}
	}
}
