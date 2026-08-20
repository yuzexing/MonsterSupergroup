using UnityEngine;

namespace AstralShift.HellMaiden.Combat
{
	[RequireComponent(typeof(Collider2D))]
	public class Safezone : MonoBehaviour
	{
		[SerializeField]
		private bool _disableTraps;

		private Collider2D _collider;

		private void Reset()
		{
			_collider = GetComponent<Collider2D>();
			_collider.isTrigger = true;
		}

		private void EnableTraps()
		{
			ProgressionManager.Instance.EnableTraps();
		}

		private void DisableTraps()
		{
			ProgressionManager.Instance.EndTraps();
			ProgressionManager.Instance.DisableTraps();
		}

		private void OnTriggerEnter2D(Collider2D other)
		{
			DisableTraps();
		}

		private void OnTriggerExit2D(Collider2D other)
		{
			EnableTraps();
		}
	}
}
