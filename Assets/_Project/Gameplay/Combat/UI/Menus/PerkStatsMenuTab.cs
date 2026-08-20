using System.Collections.Generic;
using System.Linq;
using System.Text;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Perks;
using AstralShift.Managers;
using AstralShift.UI;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Menus
{
	public class PerkStatsMenuTab : TabContentController
	{
		[Header("Buttons")]
		[SerializeField]
		private AutomaticScroll autoScroll;

		[SerializeField]
		private GridLayoutGroup perkButtonsContainers;

		[SerializeField]
		private PerkStatMenuButton perkButtonPrefab;

		[Header("Selected Perk Info")]
		[SerializeField]
		private CanvasGroup selectedPerkGroup;

		[SerializeField]
		private UICardOrPerkStaticElement perkVisuals;

		[SerializeField]
		private TextMeshProUGUI perkTitleText;

		[SerializeField]
		private TextMeshProUGUI perkUpgradeCountText;

		[SerializeField]
		private TextMeshProUGUI perkDescriptionText;

		[SerializeField]
		private TextMeshProUGUI perkEffectsEntry;

		[Header("Perk Info Tween Settings")]
		[SerializeField]
		private float hideTime;

		[SerializeField]
		private CustomAnimationCurve hideCurve;

		[SerializeField]
		private float showTime;

		[SerializeField]
		private CustomAnimationCurve showCurve;

		private StatsMenuController _controller;

		private RuntimePerk _currentPerk;

		private readonly Dictionary<RuntimePerk, PerkStatMenuButton> _instantiatedPerks = new Dictionary<RuntimePerk, PerkStatMenuButton>();

		private Tween _showPerkInfoTween;

		private Tween _hidePerkInfoTween;

		private const string StatChangeFormat = "{0} {1} : {2}%";

		private const string ModifierNamePrefix = "STP_";

		public override void Open(bool instant = false)
		{
			_controller = ControllerManager.Instance.CurrentController as StatsMenuController;
			_controller.EnableMenuInteraction(state: false);
			EventSystem.current.SetSelectedGameObject(null);
			InstantiatePerkViews();
			base.Open(instant);
			autoScroll.RecalculateScrollContentSize();
			perkVisuals.SetEmptyVisuals();
			perkTitleText.SetText("- - - - - - -");
			perkDescriptionText.SetText("");
			perkEffectsEntry.SetText("");
			if (_instantiatedPerks.Count > 0)
			{
				_currentPerk = _instantiatedPerks.ElementAt(0).Key;
				currentSelected = _instantiatedPerks.ElementAt(0).Value;
			}
		}

		protected override void OnOpeningFinished()
		{
			base.OnOpeningFinished();
			_controller.EnableMenuInteraction(state: true);
			if (_instantiatedPerks.Count > 0)
			{
				EventSystem.current.SetSelectedGameObject(currentSelected.gameObject);
				SetInfo();
			}
		}

		public override void Close(bool instant = false)
		{
			base.Close(instant);
			currentSelected = null;
		}

		private void InstantiatePerkViews()
		{
			bool flag = false;
			foreach (RuntimePerk perk in PlayerHand.Instance.PerksList)
			{
				if (_instantiatedPerks.TryGetValue(perk, out var value))
				{
					value.SetPerk(perk);
					continue;
				}
				flag = true;
				PerkStatMenuButton button = Object.Instantiate(perkButtonPrefab, perkButtonsContainers.transform);
				button.SetPerk(perk);
				button.onPointerEnter.AddListener(delegate
				{
					if (!(currentSelected != null) || currentSelected.GetInstanceID() != button.GetInstanceID())
					{
						currentSelected = button;
						SetPerk(perk);
					}
				});
				button.onSelect.AddListener(delegate
				{
					if (!currentSelected || currentSelected.GetInstanceID() != button.GetInstanceID())
					{
						currentSelected = button;
						autoScroll.ScrollToSelectedObject(button.transform as RectTransform);
						SetPerk(perk);
					}
				});
				_instantiatedPerks.Add(perk, button);
			}
			if (flag)
			{
				ConstructNavigation();
			}
			void SetPerk(RuntimePerk currentPerk)
			{
				_currentPerk = currentPerk;
				HideSelectedPerkInfoTween();
			}
		}

		private void ConstructNavigation()
		{
			int count = _instantiatedPerks.Count;
			int constraintCount = perkButtonsContainers.constraintCount;
			int num = Mathf.CeilToInt(count / constraintCount);
			for (int i = 0; i < count; i++)
			{
				PerkStatMenuButton value = _instantiatedPerks.ElementAt(i).Value;
				int num2 = i / constraintCount;
				int num3 = i % constraintCount;
				Navigation navigation = new Navigation
				{
					mode = Navigation.Mode.Explicit
				};
				int value2 = ((num3 > 0) ? (i - 1) : (num2 * constraintCount + Mathf.Min(constraintCount - 1, (num2 + 1) * constraintCount - 1 - num2 * constraintCount)));
				int value3 = ((num3 < constraintCount - 1 && i + 1 < count) ? (i + 1) : (num2 * constraintCount));
				int value4 = ((num2 > 0) ? (i - constraintCount) : ((i + num * constraintCount < count) ? (i + num * constraintCount) : (_instantiatedPerks.Count - 1)));
				int value5 = ((i + constraintCount < count) ? (i + constraintCount) : ((num2 == num) ? (i % constraintCount) : (_instantiatedPerks.Count - 1)));
				navigation.selectOnLeft = _instantiatedPerks.ElementAt(Mathf.Clamp(value2, 0, count - 1)).Value;
				navigation.selectOnRight = _instantiatedPerks.ElementAt(Mathf.Clamp(value3, 0, count - 1)).Value;
				navigation.selectOnUp = _instantiatedPerks.ElementAt(Mathf.Clamp(value4, 0, count - 1)).Value;
				navigation.selectOnDown = _instantiatedPerks.ElementAt(Mathf.Clamp(value5, 0, count - 1)).Value;
				value.navigation = navigation;
			}
		}

		private void SetInfo()
		{
			PerkRarity currentRarity = _currentPerk.CurrentRarity;
			perkVisuals.SetPerkVisuals(_currentPerk.RuntimeData);
			perkTitleText.SetText(_currentPerk.RuntimeData.Data.GetTitle());
			perkUpgradeCountText.SetText(_currentPerk.Level.ToString());
			PerkDataModifier[] modifiers = _currentPerk.RuntimeData.Data.GetRarity(currentRarity).Modifiers;
			perkDescriptionText.SetText(_currentPerk.RuntimeData.Data.GetDescription(currentRarity));
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = 0; i < modifiers.Length; i++)
			{
				PerkDataModifier perkDataModifier = modifiers[i];
				float atIndexModifierParameterValue = _currentPerk.GetAtIndexModifierParameterValue(i);
				string term = "STP_" + ModifiersStringHelpers.GetPerkModifierNameLocKey(perkDataModifier.ModifierID);
				LocalizationMediator.GetTranslation(ref term);
				stringBuilder.AppendFormat("{0} {1} : {2}%", ModifiersStringHelpers.GetPerkModifierStringIcon(perkDataModifier.ModifierID), term, $"{atIndexModifierParameterValue * 100f:0.##}");
				if (i + 1 < modifiers.Length)
				{
					stringBuilder.Append("\n");
				}
			}
			perkEffectsEntry.SetText(stringBuilder.ToString());
			ShowSelectedPerkInfoTween();
		}

		private void ShowSelectedPerkInfoTween()
		{
			_showPerkInfoTween?.Kill();
			_showPerkInfoTween = selectedPerkGroup.DOFade(1f, showTime);
			_showPerkInfoTween.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			_showPerkInfoTween.SetEase(showCurve.GetEaseFunction());
			_showPerkInfoTween.Play();
		}

		private void HideSelectedPerkInfoTween()
		{
			_hidePerkInfoTween?.Kill();
			_hidePerkInfoTween = selectedPerkGroup.DOFade(0f, hideTime);
			_hidePerkInfoTween.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			_hidePerkInfoTween.SetEase(hideCurve.GetEaseFunction());
			_hidePerkInfoTween.OnComplete(SetInfo);
			_hidePerkInfoTween.Play();
		}
	}
}
