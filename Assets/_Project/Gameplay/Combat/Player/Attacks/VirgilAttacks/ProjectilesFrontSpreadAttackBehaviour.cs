using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks.VirgilAttacks
{
	public class ProjectilesFrontSpreadAttackBehaviour : ProjectileAttackBehaviour
	{
		public override void Attack()
		{
			EvaluateDynamicStatModifiers();
			PlayAttackSound();
			int projectileCountValue = base.ProjectileCountValue;
			for (int i = 0; i < projectileCountValue; i++)
			{
				ProjectileAttack orCreateAttack = GetOrCreateAttack();
				orCreateAttack.gameObject.SetActive(value: true);
				float num = Vector2.SignedAngle(player.attackDirection, Vector2.right);
				float num2 = ((i % 2 != 0) ? 1 : (-1));
				int num3 = ((i % 2 == 0 && i > 1) ? (i - 1) : i);
				Vector3 vector = Quaternion.AngleAxis(num + num2 * 90f / (float)projectileCountValue * (float)num3, -Vector3.forward) * Vector3.right;
				orCreateAttack.transform.position = base.transform.position + positionOffset + vector.normalized * spawnRadius;
				orCreateAttack.Attack(vector.normalized, baseSpeed, hitCount, rotateToMovement);
			}
			LastAttackElapsedTime = 0f;
		}
	}
}
