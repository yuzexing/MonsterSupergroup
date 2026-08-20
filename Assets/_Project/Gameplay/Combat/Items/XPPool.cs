using System.Collections.Generic;
using AstralShift.Pooling;
using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.Items
{
	public class XPPool : MonoBehaviour
	{
		public Transform poolParent;

		public int poolSize = 100;

		private Dictionary<XPGem, BasePooler<XPGem>> _pool;

		private List<XPGem> inactiveGems;

		[SerializeField]
		private XPColorTresholds colorTresholds;

		private Queue<XPGem> Queue;

		[Header("Inactive Gems Settings")]
		[SerializeField]
		private bool deleteInactiveGems;

		[ConditionalHide("deleteInactiveGems", true)]
		[SerializeField]
		private float chanceToDeleteInactiveGems = 0.5f;

		[SerializeField]
		private float chanceToUseInactiveGems = 0.5f;

		[SerializeField]
		private int amountBeforeUsingInactiveGems = 100;

		public XPGem Get(float xp)
		{
			if (_pool == null)
			{
				_pool = new Dictionary<XPGem, BasePooler<XPGem>>();
				inactiveGems = new List<XPGem>();
				foreach (XPColorTreshold xPColorTresholds in colorTresholds.XPColorTresholdsList)
				{
					_pool.Add(xPColorTresholds.xpPrefab, new BasePooler<XPGem>("XPPool", poolParent, poolSize));
				}
				colorTresholds?.Initialize();
			}
			float num = Random.Range(0f, 1f);
			if (inactiveGems.Count >= amountBeforeUsingInactiveGems && num <= chanceToUseInactiveGems)
			{
				xp += inactiveGems[0].xp;
				LootManager.Instance.EnqueueConsume(inactiveGems[0]);
				Return(inactiveGems[0]);
			}
			if (!_pool[colorTresholds.GetGemByValue(xp)].Get(out var element))
			{
				element = Object.Instantiate(colorTresholds.GetGemByValue(xp), poolParent);
			}
			element.xp = xp;
			return element;
		}

		public void SetGemAsInactive(XPGem gem)
		{
			if (!inactiveGems.Contains(gem))
			{
				float num = Random.Range(0f, 1f);
				if (deleteInactiveGems && num <= chanceToDeleteInactiveGems)
				{
					LootManager.Instance.EnqueueConsume(gem);
					Return(gem);
				}
				else
				{
					inactiveGems.Add(gem);
				}
			}
		}

		public void SetGemAsActive(XPGem gem)
		{
			if (inactiveGems.Contains(gem))
			{
				inactiveGems.Remove(gem);
			}
		}

		public void Return(XPGem gem)
		{
			SetGemAsActive(gem);
			_pool[colorTresholds.GetGemByValue(gem.xp)].Return(gem);
			gem.gameObject.SetActive(value: false);
		}
	}
}
