using System.Collections.Generic;
using AstralShift.Control;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.UI.Menus.Achievement;
using AstralShift.UI;
using Rewired;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.Controllers
{
	public class AchievementTabContentController : TabContentController
	{
		private const string ACH_COUNTER_TEXT_KEY = "ACH_Tab_Chapter_Achievements_Complete";

		[SerializeField]
		private List<AchievementTabData> tabAchievements;

		[Header("Scroll")]
		[SerializeField]
		private AutomaticScroll autoScroll;

		[SerializeField]
		private float scrollBuff = 0.15f;

		[Header("Information Panel")]
		[SerializeField]
		private AchievementsInformationPanel achievementsInformationPanel;

		[Header("Achievement Counter")]
		[SerializeField]
		private TMP_Text achievementCounterText;

		[SerializeField]
		private int achievementsPerRow = 6;

		public VerticalLayoutGroup verticalLayout;

		private AchievementUIButton[] _achievementButtons;

		private new AchievementUIButton previousSelected;

		private int previousIndex = -1;

		public List<AchievementTabData> TabAchievements => tabAchievements;

		public AchievementMenuController mainController { get; set; }

		public void RecoverSceneReferences(AchievementsInformationPanel informationPanel)
		{
			if (tabAchievements == null)
			{
				tabAchievements = new List<AchievementTabData>();
			}
			if (autoScroll == null)
			{
				autoScroll = GetComponent<AutomaticScroll>();
			}
			if (achievementsInformationPanel == null)
			{
				achievementsInformationPanel = informationPanel;
			}
			if (verticalLayout == null)
			{
				verticalLayout = GetComponentInChildren<VerticalLayoutGroup>(includeInactive: true);
			}
			if (menuAnimator == null)
			{
				menuAnimator = GetComponent<Animancer.AnimancerComponent>();
			}
		}

		protected override void OnOpeningFinished()
		{
			base.OnOpeningFinished();
			SelectFirstSelectable();
		}

		private void SelectFirstSelectable()
		{
			if (_achievementButtons == null || _achievementButtons.Length == 0)
			{
				EventSystem.current.SetSelectedGameObject(null);
				return;
			}
			firstSelected = _achievementButtons[0];
			if (ControllerLifetime.ActiveControllerType == ControllerType.Mouse)
			{
				EventSystem.current.SetSelectedGameObject(null);
				currentSelected = firstSelected;
				return;
			}
			EventSystem.current.SetSelectedGameObject(firstSelected.gameObject);
			currentSelected = firstSelected;
			if (firstSelected != null)
			{
				AchievementUIButton component = firstSelected.GetComponent<AchievementUIButton>();
				if (component != null)
				{
					OnAchievementSelected(component);
				}
			}
		}

		private void OnAchievementSelected(AchievementUIButton selectedButton)
		{
			if (!(selectedButton == null))
			{
				AchievementData achievementData = selectedButton.GetAchievementData();
				if (achievementsInformationPanel != null)
				{
					achievementsInformationPanel.DisplayAchievement(achievementData);
				}
				if (EventSystem.current.currentSelectedGameObject == selectedButton.gameObject && autoScroll != null)
				{
					autoScroll.ScrollToSelectedObject(selectedButton.transform.parent as RectTransform, scrollBuff);
				}
			}
		}

		public void UpdateAchievementCounter(List<AchievementData> achievements)
		{
			if (achievementCounterText == null || achievements == null)
			{
				return;
			}
			int count = achievements.Count;
			int num = 0;
			foreach (AchievementData achievement in achievements)
			{
				if (achievement != null && AchievementManager.Instance.IsAchievementUnlockedInGameData(achievement))
				{
					num++;
				}
			}
			string text = LocalizationMediator.GetTranslation("ACH_Tab_Chapter_Achievements_Complete");
			if (string.IsNullOrEmpty(text))
			{
				text = "Chapter Achievements: <color=blue>{unlocked}</color> / {total}";
			}
			string text2 = text.Replace("{unlocked}", num.ToString()).Replace("{total}", count.ToString());
			achievementCounterText.text = text2;
			Canvas.ForceUpdateCanvases();
		}

		public void SetButtonNavigation()
		{
			List<AchievementUIButton> list = new List<AchievementUIButton>();
			CollectAchievementButtons(verticalLayout.transform, list);
			if (list.Count == 0)
			{
				return;
			}
			_achievementButtons = list.ToArray();
			int num = _achievementButtons.Length;
			_ = (num - 1) / achievementsPerRow;
			int num2 = (num - 1) % achievementsPerRow;
			for (int i = 0; i < num; i++)
			{
				AchievementUIButton achievementUIButton = _achievementButtons[i];
				int num3 = i / achievementsPerRow;
				int num4 = i % achievementsPerRow;
				Navigation navigation = new Navigation
				{
					mode = Navigation.Mode.Explicit
				};
				int num5 = Mathf.Min(achievementsPerRow - 1, num - 1 - num3 * achievementsPerRow);
				int num6 = (num - 1 - num4) / achievementsPerRow;
				if (num4 > 0)
				{
					navigation.selectOnLeft = _achievementButtons[i - 1];
				}
				else
				{
					navigation.selectOnLeft = _achievementButtons[num3 * achievementsPerRow + num5];
				}
				if (num4 < num5)
				{
					navigation.selectOnRight = _achievementButtons[i + 1];
				}
				else
				{
					navigation.selectOnRight = _achievementButtons[num3 * achievementsPerRow];
				}
				if (num3 > 0)
				{
					navigation.selectOnUp = _achievementButtons[(num3 - 1) * achievementsPerRow + num4];
				}
				else if (num4 > num2)
				{
					navigation.selectOnUp = _achievementButtons[num - 1];
				}
				else
				{
					navigation.selectOnUp = _achievementButtons[num6 * achievementsPerRow + num4];
				}
				if (num3 < num6)
				{
					navigation.selectOnDown = _achievementButtons[(num3 + 1) * achievementsPerRow + num4];
				}
				else if (num4 > num2)
				{
					navigation.selectOnDown = _achievementButtons[num - 1];
				}
				else
				{
					navigation.selectOnDown = _achievementButtons[num4];
				}
				achievementUIButton.navigation = navigation;
				Selectable component = achievementUIButton.GetComponent<Selectable>();
				if (component != null)
				{
					component.navigation = navigation;
				}
				SetupButtonListeners(achievementUIButton);
			}
			if (num > 0)
			{
				firstSelected = _achievementButtons[0];
			}
			if (autoScroll != null)
			{
				autoScroll.RecalculateScrollContentSize();
			}
		}

		private void CollectAchievementButtons(Transform parent, List<AchievementUIButton> buttons)
		{
			for (int i = 0; i < parent.childCount; i++)
			{
				Transform child = parent.GetChild(i);
				AchievementUIButton component = child.GetComponent<AchievementUIButton>();
				if (component != null)
				{
					buttons.Add(component);
				}
				if (child.childCount > 0)
				{
					CollectAchievementButtons(child, buttons);
				}
			}
		}

		private void SetupButtonListeners(AchievementUIButton button)
		{
			UISelectable selectable = button.GetComponent<UISelectable>();
			if (selectable == null)
			{
				return;
			}
			selectable.onSelect.RemoveAllListeners();
			selectable.onPointerEnter.RemoveAllListeners();
			selectable.onPointerExit.RemoveAllListeners();
			selectable.onDeSelect.RemoveAllListeners();
			selectable.onSelect.AddListener(delegate
			{
				currentSelected = selectable;
				AchievementUIButton component = selectable.GetComponent<AchievementUIButton>();
				if (component != null)
				{
					OnAchievementSelected(component);
				}
			});
			selectable.onPointerEnter.AddListener(delegate
			{
				currentSelected = selectable;
				AchievementUIButton component = selectable.GetComponent<AchievementUIButton>();
				if (component != null)
				{
					OnAchievementSelected(component);
				}
			});
			selectable.onPointerExit.AddListener(delegate
			{
				_ = achievementsInformationPanel != null;
			});
			selectable.onDeSelect.AddListener(delegate
			{
				_ = achievementsInformationPanel != null;
			});
		}
	}
}
