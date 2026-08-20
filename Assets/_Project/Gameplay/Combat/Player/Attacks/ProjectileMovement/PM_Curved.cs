using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks.ProjectileMovement
{
	public class PM_Curved : PM_Base
	{
		[SerializeField]
		private float turnSpeed = 1f;

		private Vector2 origin;

		private float angle;

		private Vector2 baseDirection;

		private Vector2 internalDirection;

		public override void Init(Vector2 direction, Transform rotationTransform, float speed, float despawnTimeout, Transform originTransform = null)
		{
			origin = base.transform.position;
			baseDirection = direction.normalized;
			angle = Mathf.Atan2(baseDirection.y, baseDirection.x);
		}

		public override void MovementUpdate(Vector2 direction, Transform rotationTransform, float speed)
		{
			angle += turnSpeed * Time.deltaTime;
			float num = speed * (angle - Mathf.Atan2(baseDirection.y, baseDirection.x));
			Vector2 vector = new Vector2(Mathf.Cos(angle) * num, Mathf.Sin(angle) * num);
			base.transform.position = origin + vector;
			Vector2 vector2 = new Vector2(0f - Mathf.Sin(angle), Mathf.Cos(angle));
			if ((bool)rotationTransform)
			{
				float z = Mathf.Atan2(vector2.y, vector2.x) * 57.29578f;
				rotationTransform.rotation = Quaternion.Euler(0f, 0f, z);
			}
		}
	}
}
