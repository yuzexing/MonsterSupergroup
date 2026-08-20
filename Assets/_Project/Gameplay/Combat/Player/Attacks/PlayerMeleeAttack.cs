using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	[Obsolete]
	public abstract class PlayerMeleeAttack : MonoBehaviour
	{
		public Action OnHit;

		public Action OnEnd;

		public abstract void Init(Vector3 direction, int damage, AttackStats attackStats, PlayerStats playerMetaStats, Action onHit, Action onEnd);

		public abstract void Attack();
	}
}
