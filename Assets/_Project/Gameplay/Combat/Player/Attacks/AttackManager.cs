using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class AttackManager : MonoBehaviour
	{
		public List<GameObject> attacks;

		public void SubscribeToAttack(Component enemyHitbox)
		{
			foreach (GameObject attack in attacks)
			{
				attack.GetComponent<ParticleSystem>().trigger.AddCollider(enemyHitbox);
			}
		}

		public void UnSubscribeToAttack(Component enemyHitbox)
		{
			foreach (GameObject attack in attacks)
			{
				attack.GetComponent<ParticleSystem>().trigger.RemoveCollider(enemyHitbox);
			}
		}
	}
}
