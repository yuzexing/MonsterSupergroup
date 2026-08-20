using System;
using System.Collections.Generic;
using AstralShift.DebugTools;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.UI.Menus.Achievement;
using AstralShift.Helpers.Attributes;
using AstralShift.Managers;
using DG.Tweening;
using Rewired;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.Controllers
{
	public class AchievementMenuController : TabMenuController
	{
		[SerializeField]
		private CanvasGroup canvasGroup;

		[Header("Achievement Prefabs")]
		[SerializeField]
		[NotNullRef]
		private GameObject normalAchievementPrefab;

		[SerializeField]
		[NotNullRef]
		private GameObject rareAchievementPrefab;

		[SerializeField]
		[NotNullRef]
		private GameObject secretAchievementPrefab;

		protected void Awake()
		{
			RecoverSceneReferences();
			if (canvasGroup == null)
			{
				canvasGroup = GetComponent<CanvasGroup>();
			}
			canvasGroup.alpha = 0f;
			canvasGroup.blocksRaycasts = false;
			ControllerManager.Instance.Subscribe(this, init: true);
		}

		private void RecoverSceneReferences()
		{
			if (canvas == null)
			{
				canvas = GetComponent<Canvas>();
			}
			if (menuAnimator == null)
			{
				menuAnimator = GetComponent<Animancer.AnimancerComponent>();
			}
			if (onOpen == null)
			{
				onOpen = new UnityEngine.Events.UnityEvent();
			}
			if (onClose == null)
			{
				onClose = new UnityEngine.Events.UnityEvent();
			}
			if (tabSelector == null)
			{
				tabSelector = GetComponentInChildren<AstralShift.UI.MenuTabSelector>(includeInactive: true);
			}
			if (tabContents == null || tabContents.Length == 0)
			{
				tabContents = GetComponentsInChildren<AchievementTabContentController>(includeInactive: true);
			}
			AchievementsInformationPanel informationPanel = GetComponentInChildren<AchievementsInformationPanel>(includeInactive: true);
			for (int i = 0; i < tabContents.Length; i++)
			{
				if (tabContents[i] is AchievementTabContentController content)
				{
					content.RecoverSceneReferences(informationPanel);
				}
			}
		}

		public override void Open()
		{
			canvasGroup.blocksRaycasts = true;
			GenerateAchievements();
			base.Open();
		}

		protected override void OnOpeningFinished()
		{
			base.OnOpeningFinished();
			tabSelector.SelectIntroTab();
		}

		public override void Close()
		{
			canvasGroup.blocksRaycasts = false;
			base.Close();
		}

		public override void Init()
		{
			try
			{
				for (int i = 0; i < tabContents.Length; i++)
				{
					if (tabContents[i] is AchievementTabContentController achievementTabContentController)
					{
						achievementTabContentController.mainController = this;
					}
				}
			}
			catch (InvalidCastException ex)
			{
				DBL.Log(DBL.Module.Settings, "Failed to cast tab content to SettingMenuControls: " + ex.Message, 2);
			}
			catch (Exception ex2)
			{
				DBL.Log(DBL.Module.Settings, "Error initializing tab contents: " + ex2.Message, 2);
			}
			base.Init();
		}

		private void GenerateAchievements()
		{
			for (int i = 0; i < tabContents.Length; i++)
			{
				if (!(tabContents[i] is AchievementTabContentController achievementTabContentController))
				{
					continue;
				}
				List<AchievementTabData> tabAchievements = achievementTabContentController.TabAchievements;
				List<AchievementData> list = new List<AchievementData>();
				foreach (AchievementTabData item in tabAchievements)
				{
					if (item.achievements != null)
					{
						list.AddRange(item.achievements);
					}
				}
				if (list.Count > 0)
				{
					GenerateTabAchievements(achievementTabContentController, list);
					achievementTabContentController.UpdateAchievementCounter(list);
				}
			}
			for (int j = 0; j < tabContents.Length; j++)
			{
				if (tabContents[j] is AchievementTabContentController achievementTabContentController2)
				{
					achievementTabContentController2.SetButtonNavigation();
				}
			}
		}

		private void GenerateTabAchievements(AchievementTabContentController tabController, List<AchievementData> achievements)
		{
			if (achievements == null || achievements.Count == 0)
			{
				return;
			}
			List<HorizontalLayoutGroup> list = new List<HorizontalLayoutGroup>();
			for (int i = 0; i < tabController.verticalLayout.transform.childCount; i++)
			{
				if (tabController.verticalLayout.transform.GetChild(i).TryGetComponent<HorizontalLayoutGroup>(out var component))
				{
					list.Add(component);
				}
			}
			if (list.Count == 0)
			{
				DBL.Log(DBL.Module.Settings, "No horizontal layouts found in AchievementTabContentController", 2);
				return;
			}
			int num = 0;
			int num2 = 0;
			while (num < achievements.Count && num2 < list.Count)
			{
				HorizontalLayoutGroup horizontalLayoutGroup = list[num2];
				for (int num3 = horizontalLayoutGroup.transform.childCount - 1; num3 >= 0; num3--)
				{
					Transform child = horizontalLayoutGroup.transform.GetChild(num3);
					if (child.name.StartsWith("Achievement_"))
					{
						UnityEngine.Object.DestroyImmediate(child.gameObject);
					}
				}
				int num4 = 0;
				while (num < achievements.Count && num4 < 6)
				{
					AchievementData achievementData = achievements[num];
					if (achievementData != null)
					{
						GameObject obj = UnityEngine.Object.Instantiate(GetAchievementPrefab(achievementData), horizontalLayoutGroup.transform);
						obj.name = $"Achievement_{achievementData.LinkedAchievementID}";
						if (obj.TryGetComponent<AchievementUIButton>(out var component2))
						{
							component2.Initialize(achievementData);
							if (!achievementData.IsSecret)
							{
								component2.SetSprite(achievementData.Icon);
							}
							if (AchievementManager.Instance.IsAchievementUnlockedInGameData(achievementData))
							{
								if (achievementData.Rarity == RarityType.Rare)
								{
									component2.SetUnlockedBackgroundSprite(achievementData.RareUnlockedBackground);
								}
								if (achievementData.Rarity == RarityType.Common)
								{
									component2.SetUnlockedBackgroundSprite(achievementData.NormalUnlockedBackground);
								}
							}
						}
					}
					num++;
					num4++;
				}
				horizontalLayoutGroup.gameObject.SetActive(value: true);
				num2++;
			}
			for (int j = num2; j < list.Count; j++)
			{
				list[j].gameObject.SetActive(value: false);
			}
		}

		private GameObject GetAchievementPrefab(AchievementData achievementData)
		{
			if (achievementData.IsSecret && !AchievementManager.Instance.IsAchievementUnlockedInGameData(achievementData))
			{
				return secretAchievementPrefab;
			}
			if (achievementData.Rarity == RarityType.Rare)
			{
				return rareAchievementPrefab;
			}
			return normalAchievementPrefab;
		}

		protected override void OnControllerTypeChanged()
		{
			if (_currentMenu != null)
			{
				currentSelectable = _currentMenu.currentSelected;
				base.OnControllerTypeChanged();
			}
		}

		public override void UICancelPressed(InputActionEventData data)
		{
			if (base.IsActive)
			{
				base.UICancelPressed(data);
				CloseMenu();
			}
		}

		private void OnDestroy()
		{
			canvasGroup.DOKill();
			ControllerManager.Instance.UnSubscribe(this);
		}
	}
}
