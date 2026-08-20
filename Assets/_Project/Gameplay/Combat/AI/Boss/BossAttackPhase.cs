using System;
using System.Collections.Generic;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss
{
	[Serializable]
	public class BossAttackPhase
	{
		[SerializeField]
		protected List<BossAttackSettings> attacks;

		[SerializeField]
		protected bool pityChance;

		[ConditionalHide("pityChance", true)]
		public float recoveryInterval = 0.5f;

		private bool _attacking;

		public List<BossAttackSettings> Attacks => attacks;

		public bool PityChance => pityChance;

		public void Init()
		{
			Reset();
		}

		public void Reset()
		{
			for (int i = 0; i < Attacks.Count; i++)
			{
				Attacks[i].ResetWeight();
			}
		}

		public BossAttackBehaviour GetAttack(int index)
		{
			return Attacks[index].attack;
		}

		public BossAttackBehaviour GetRandomAttack()
		{
			int index = UnityEngine.Random.Range(0, Attacks.Count);
			return GetAttack(index);
		}

		public BossAttackBehaviour GetWeightedRandomAttack()
		{
			if (Attacks == null || Attacks.Count == 0)
			{
				return null;
			}
			float num = 0f;
			for (int i = 0; i < Attacks.Count; i++)
			{
				num += Attacks[i].CurrentWeight;
			}
			float num2 = UnityEngine.Random.Range(0f, num);
			float num3 = 0f;
			for (int j = 0; j < Attacks.Count; j++)
			{
				num3 += Attacks[j].CurrentWeight;
				if (num2 < num3)
				{
					ApplyPityChance(Attacks[j]);
					return Attacks[j].attack;
				}
			}
			List<BossAttackSettings> list = Attacks;
			ApplyPityChance(list[list.Count - 1]);
			List<BossAttackSettings> list2 = Attacks;
			return list2[list2.Count - 1].attack;
		}

		private void ApplyPityChance(BossAttackSettings attack)
		{
			if (PityChance)
			{
				attack.ApplyReductionFactor();
				if (attack.CurrentWeight < attack.MinWeightThreshold)
				{
					attack.SetWeight(attack.MinWeightThreshold);
				}
			}
		}
	}
}
