using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks.ProjectileMovement
{
	public abstract class PM_Base : MonoBehaviour
	{
		public abstract void Init(Vector2 direction, Transform rotationTransform, float speed, float despawnTimeout, Transform originTransform = null);

		public abstract void MovementUpdate(Vector2 direction, Transform rotationTransform, float speed);
	}
}
