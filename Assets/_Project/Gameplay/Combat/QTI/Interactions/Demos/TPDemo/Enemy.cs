using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions.Demos.TPDemo
{
	public class Enemy : MonoBehaviour, IInteractor, IDamageable
	{
		[SerializeField]
		private int hp;

		public Interaction deathInteraction;

		public int HP
		{
			get
			{
				return hp;
			}
			protected set
			{
				hp = value;
			}
		}

		public Transform GetTransform()
		{
			return base.transform;
		}

		public void TakeDamage(int dmg)
		{
			HP -= dmg;
			if (HP <= 0)
			{
				deathInteraction.Interact(this);
			}
		}
	}
}
