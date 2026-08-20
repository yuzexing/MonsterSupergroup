using AstralShift.HellMaiden.AI.Enemy;
using UnityEngine;
using UnityEngine.Serialization;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class AreaHitbox : MonoBehaviour
	{
		[FormerlySerializedAs("areaAttack")]
		public PersistentAreaAttackBehaviour areaAttackBehaviour;

		private void OnTriggerEnter2D(Collider2D collision)
		{
			collision.transform.parent.TryGetComponent<EnemyController>(out var _);
		}

		private void OnTriggerExit2D(Collider2D collision)
		{
			collision.transform.parent.TryGetComponent<EnemyController>(out var _);
		}
	}
}
