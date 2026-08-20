using System;
using Assets.Scripts.AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data;
using AstralShift.UI;
using I2.Loc;
using TMPro;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Menus.MetaProgression
{
	public class MetaProgressionUpgradeView : CustomUIButton
	{
		[SerializeField]
		private MetaProgressionUpgradeIconView icon;

		[SerializeField]
		private TMP_Text title;

		[SerializeField]
		private TMP_Text lvl;

		[SerializeField]
		private MetaCostView cost;

		[SerializeField]
		private CanvasGroup infoLockedOverlay;

		private MetaProgressionID _metaProgressionID;

		public Action OnUpgradeSelected;

		private bool _isMaxlevel;

		public MetaStatDatabaseEntry MetaStatDatabaseEntry { get; set; }

		public int Lvl { get; set; }

		public int Maxlvl { get; set; }

		public bool IsMaxlevel => _isMaxlevel;

		public bool IsLocked { get; internal set; }

		public void Init(MetaProgressionID metaProgressionID)
		{
			_metaProgressionID = metaProgressionID;
			Lvl = GameDataManager.GetMetaProgressionLevel(metaProgressionID);
			MetaStatDatabaseEntry = GameDirector.Instance.runtimeDB.MetaStatsDB.entries[metaProgressionID];
			SetTitle();
			SetLvl();
			InitializeIcon();
			SetLock();
			onSelect.AddListener(OnSelect);
			onPointerEnter.AddListener(OnSelect);
			AchievementManager.Instance.OnAchievementUnlocked += OnAchievementUnlock;
			LocalizationManager.OnLocalizeEvent += SetTitle;
		}

		public bool Upgrade()
		{
			if (!IsLocked && Lvl < Maxlvl)
			{
				int upgradeCost = GetUpgradeCost(Lvl);
				if (GameDataManager.GetCurrency() < upgradeCost)
				{
					return false;
				}
				GameDataManager.DecreaseCurrency(upgradeCost);
				GameData.Instance.GameStatsTracker.totalCurrencySpent += upgradeCost;
				if (GameData.Instance.GameStatsTracker.totalCurrencySpent > 5000)
				{
					AchievementManager.Instance.UnlockAchievement(AchievementManager.AchievementID.AbyssalSpender);
				}
				Lvl++;
				GameDataManager.SetMetaProgressionLevel(_metaProgressionID, Lvl);
				SetLvl();
				SetIcon();
				SetLock();
				return true;
			}
			return false;
		}

		public void Refund()
		{
			for (int i = 0; i < Lvl; i++)
			{
				int upgradeCost = GetUpgradeCost(i);
				GameDataManager.IncreaseCurrency(upgradeCost);
				GameData.Instance.GameStatsTracker.totalCurrencySpent -= upgradeCost;
			}
			Lvl = 0;
			GameDataManager.SetMetaProgressionLevel(_metaProgressionID, Lvl);
			SetLvl();
			SetIcon();
			SetLock();
		}

		private void SetTitle()
		{
			string term = MetaStatDatabaseEntry.name;
			LocalizationMediator.GetTranslation(ref term);
			if (term != null)
			{
				title.text = term;
			}
			else
			{
				title.text = MetaStatDatabaseEntry.name;
			}
		}

		private void SetLvl()
		{
			Maxlvl = MetaStatDatabaseEntry.levels.Length;
			lvl.text = Lvl.ToString();
			if (Lvl < Maxlvl)
			{
				cost.SetCost(GetUpgradeCost(Lvl));
				_isMaxlevel = false;
			}
			else
			{
				cost.SetMaxedOut();
				_isMaxlevel = true;
			}
		}

		private void SetIcon()
		{
			if (_isMaxlevel)
			{
				icon.Upgrade();
			}
			else
			{
				icon.Downgrade();
			}
		}

		private void SetLock()
		{
			bool flag = false;
			if (!IsMaxlevel)
			{
				MetaStatDatabaseEntry.MetaStatDatabaseEntryLevel metaStatDatabaseEntryLevel = MetaStatDatabaseEntry.levels[Lvl];
				if (metaStatDatabaseEntryLevel.hasLockVerification && !AchievementManager.Instance.IsAchievementUnlockedInGameData(metaStatDatabaseEntryLevel.achievementID))
				{
					flag = true;
				}
			}
			infoLockedOverlay.alpha = (flag ? 1 : 0);
			infoLockedOverlay.blocksRaycasts = !flag;
			IsLocked = flag;
		}

		public RenderTexture GetIconTexture()
		{
			return icon.GetRenderTexture();
		}

		private void InitializeIcon()
		{
			try
			{
				MetaProgressionUpgrade3DIcon metaProgression3DIcon = MetaProgressionUpgradeVisualsFactory.GetMetaProgression3DIcon(MetaStatDatabaseEntry);
				MetaProgressionUpgrade3DIcon metaProgression3DIcon2 = MetaProgressionUpgradeVisualsFactory.GetMetaProgression3DIcon(MetaStatDatabaseEntry, isMaxLevel: true);
				icon.Initialize(metaProgression3DIcon, metaProgression3DIcon2);
				icon.Show();
				SetIcon();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
				Debug.Log("Failed to set icon: " + icon.gameObject.name);
			}
		}

		private int GetUpgradeCost(int level)
		{
			return (int)GameDirector.Instance.runtimeDB.MetaStatsDB.entries[_metaProgressionID].levels[level].cost;
		}

		public void OnSelect()
		{
			OnUpgradeSelected?.Invoke();
		}

		private void OnAchievementUnlock(AchievementManager.AchievementID achievementID)
		{
			SetLock();
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();
			AchievementManager.Instance.OnAchievementUnlocked -= OnAchievementUnlock;
		}
	}
}
