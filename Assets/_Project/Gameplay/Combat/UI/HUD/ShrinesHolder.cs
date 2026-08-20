using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data.Shrines;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.HUD
{
	public class ShrinesHolder : MonoBehaviour
	{
		[SerializeField]
		private ShrinesHolderIcon permanentIconTemplatePrefab;

		[SerializeField]
		private ShrinesHolderTemporaryIcon temporaryIconTemplatePrefab;

		[SerializeField]
		private RectTransform layoutGroupTransform;

		private Stack<ShrinesHolderTemporaryIcon> _temporaryShrinesPool = new Stack<ShrinesHolderTemporaryIcon>();

		private Dictionary<RuntimeShrine, ShrinesHolderIcon> _permanentShrines = new Dictionary<RuntimeShrine, ShrinesHolderIcon>();

		private Dictionary<RuntimeShrine, ShrinesHolderTemporaryIcon> _temporaryShrines = new Dictionary<RuntimeShrine, ShrinesHolderTemporaryIcon>();

		private void Awake()
		{
			PlayerHand.Instance.OnPermanentShrineAdded += AddPermanentShrine;
			PlayerHand.Instance.OnTemporaryShrineAdded += AddTemporaryShrine;
			PlayerHand.Instance.OnTemporaryShrineRemoved += RemoveTemporaryShrine;
			Init();
		}

		private void OnDestroy()
		{
			PlayerHand.Instance.OnPermanentShrineAdded -= AddPermanentShrine;
			PlayerHand.Instance.OnTemporaryShrineAdded -= AddTemporaryShrine;
			PlayerHand.Instance.OnTemporaryShrineRemoved -= RemoveTemporaryShrine;
		}

		private void Init()
		{
			PlayerHand.Instance.PermanentShrines.ForEach(AddPermanentShrine);
			PlayerHand.Instance.TemporaryShrines.ForEach(AddTemporaryShrine);
		}

		private void AddPermanentShrine(RuntimeShrine shrine)
		{
			Sprite iconSprite = shrine.ShrineData.GetIconSprite();
			if (!(iconSprite == null))
			{
				if (!_permanentShrines.TryGetValue(shrine, out var value))
				{
					value = GetNewPermanentIcon();
					value.Init(iconSprite);
					_permanentShrines.Add(shrine, value);
				}
				value.UpdateCount(shrine.ModifiersCount);
			}
		}

		private void AddTemporaryShrine(RuntimeShrine shrine)
		{
			Sprite iconSprite = shrine.ShrineData.GetIconSprite();
			if (!(iconSprite == null) && !_temporaryShrines.ContainsKey(shrine))
			{
				ShrinesHolderTemporaryIcon newTemporaryIcon = GetNewTemporaryIcon();
				newTemporaryIcon.Init(iconSprite, shrine.GetRemainingTime, shrine.TotalDuration);
				_temporaryShrines.Add(shrine, newTemporaryIcon);
			}
		}

		private void RemoveTemporaryShrine(RuntimeShrine shrine)
		{
			if (_temporaryShrines.TryGetValue(shrine, out var value))
			{
				value.gameObject.SetActive(value: false);
				ReturnToPool(value);
				_temporaryShrines.Remove(shrine);
			}
		}

		private ShrinesHolderIcon GetNewPermanentIcon()
		{
			return Object.Instantiate(permanentIconTemplatePrefab, layoutGroupTransform);
		}

		private ShrinesHolderTemporaryIcon GetNewTemporaryIcon()
		{
			if (_temporaryShrinesPool.TryPop(out var result))
			{
				result.gameObject.SetActive(value: true);
				return result;
			}
			return Object.Instantiate(temporaryIconTemplatePrefab, layoutGroupTransform);
		}

		private void ReturnToPool(ShrinesHolderTemporaryIcon icon)
		{
			_temporaryShrinesPool.Push(icon);
		}
	}
}
