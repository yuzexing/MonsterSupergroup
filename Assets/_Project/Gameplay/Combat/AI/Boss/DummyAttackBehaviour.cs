using AstralShift.Helpers;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss
{
	public class DummyAttackBehaviour : BossAttackBehaviour
	{
		public override void Init(BossController controller)
		{
		}

		public override void Positioning()
		{
			Debug.Log(name + "Positioning");
			StartCoroutine(Wait.SetTimeout(2f, onPositioningEnd));
		}

		public override void Warning()
		{
			Debug.Log(name + " Warning");
			StartCoroutine(Wait.SetTimeout(2f, onWarningEnd));
		}

		public override void Attack()
		{
			Debug.Log(name + " Attack");
			StartCoroutine(Wait.SetTimeout(2f, onAttackEnd));
		}
	}
}
