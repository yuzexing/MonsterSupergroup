using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class HomingBulletProjectile : BulletProjectile
	{
		private Transform player;

		public float turnSpeed = 5f;

		public Transform explosionPosition;

		private void Awake()
		{
			player = GameDirector.Instance.Player.transform;
		}

		protected override void FixedUpdate()
		{
			if (!fired || player == null)
			{
				return;
			}
			Vector3 normalized = (player.position - base.transform.position).normalized;
			_direction = Vector3.Lerp(_direction, normalized, turnSpeed * Time.fixedDeltaTime).normalized;
			if (elapsedTime > duration)
			{
				fired = false;
				if (hitEffectResolver != null)
				{
					hitEffectResolver.Initialize();
				}
				ExpireEnter();
			}
			base.FixedUpdate();
		}

		public override void HitEnter()
		{
			base.HitEnter();
			if ((bool)explosionPosition)
			{
				explosionPosition.position = player.position;
			}
		}
	}
}
