using System;
using System.Collections.Generic;
using System.Threading;
using Animancer;
using Assets.Scripts.AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Data;
using AstralShift.Helpers;
using AstralShift.UI;
using Cysharp.Threading.Tasks;
using FMOD.Studio;
using FMODUnity;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Menus.MetaProgression
{
	public class MetaProgressionMenuView : MonoBehaviour
	{
		[Header("References")]
		[SerializeField]
		private AutomaticScroll autoScroll;

		[SerializeField]
		private Transform upgradeViewContentContainer;

		[SerializeField]
		private TMP_Text currencyText;

		[SerializeField]
		private int upgradesPerRow = 4;

		[Header("Prefabs")]
		[SerializeField]
		private MetaProgressionUpgradeView upgradeViewTemplate;

		[SerializeField]
		private GameObject upgradeGridLine;

		[SerializeField]
		private GameObject upgradeGridVerticalDivider;

		[SerializeField]
		private GameObject upgradeGridHorizontalDivider;

		[Header("View Order")]
		[Tooltip("Upgrades appear in the order that are inputed into this list")]
		[SerializeField]
		private MetaProgressionID[] viewOrder;

		[Header("Info Panel")]
		[SerializeField]
		private CanvasGroup infoPanelCanvasGroup;

		[SerializeField]
		private TMP_Text infoTitle;

		[SerializeField]
		private TMP_Text infoDescription;

		[SerializeField]
		private TMP_Text infoCurrentLVL;

		[SerializeField]
		private RawImage infoIcon;

		[SerializeField]
		private GameObject normalStatsInfo;

		[SerializeField]
		private TMP_Text infoCurrentBuff;

		[SerializeField]
		private TMP_Text infoNextBuff;

		[SerializeField]
		private GameObject nextlvLabel;

		[SerializeField]
		private GameObject maxedStatsInfo;

		[SerializeField]
		private TMP_Text infoMaxedBuff;

		[SerializeField]
		private MetaCostView infoCost;

		[SerializeField]
		private CanvasGroup infoLockedOverlay;

		[Header("Glyphs")]
		[SerializeField]
		private CustomUnityUIPlayerControllerElementGlyph buyButton;

		[SerializeField]
		private CustomUnityUIPlayerControllerElementGlyph refundButton;

		public float skipHoldTime = 1f;

		[Header("Animation")]
		[SerializeField]
		private ClipTransition BuyAnim;

		[SerializeField]
		private MetaProgressionUpgradeIconView animatedUpgradeIcon;

		private MetaProgressionUpgradeView _currentSelectedUpgrade;

		private MetaProgressionUpgradeView[] _upgrades;

		private readonly Queue<Func<UniTask>> _buyAnimationQueue = new Queue<Func<UniTask>>();

		private bool _isBuyAnimationQueueRunning;

		private const int MenuInteractionAnimLayer = 1;

		[Header("Sounds")]
		[SerializeField]
		private EventReference upgradeSound;

		[SerializeField]
		private EventReference refundSound;

		[SerializeField]
		private EventReference failToUpgradeSound;

		[SerializeField]
		private EventReference ambienceSound;

		private EventInstance ambienceInstance;

		public AnimancerComponent Animancer { get; set; }

		public void Init()
		{
			int num = 0;
			int num2 = 0;
			Transform parent = null;
			_upgrades = new MetaProgressionUpgradeView[viewOrder.Length];
			MetaProgressionID[] array = viewOrder;
			foreach (MetaProgressionID metaProgressionID in array)
			{
				if (num % upgradesPerRow == 0)
				{
					if (num != 0)
					{
						UnityEngine.Object.Instantiate(upgradeGridHorizontalDivider, upgradeViewContentContainer);
					}
					parent = UnityEngine.Object.Instantiate(upgradeGridLine, upgradeViewContentContainer).transform;
				}
				MetaProgressionUpgradeView view = UnityEngine.Object.Instantiate(upgradeViewTemplate, parent);
				view.Init(metaProgressionID);
				MetaProgressionUpgradeView metaProgressionUpgradeView = view;
				metaProgressionUpgradeView.OnUpgradeSelected = (Action)Delegate.Combine(metaProgressionUpgradeView.OnUpgradeSelected, (Action)delegate
				{
					OnUpgradeSelected(view);
				});
				view.onSubmit.AddListener(delegate
				{
					OnUpgradeSelected(view);
					Upgrade();
				});
				_upgrades[num2] = view;
				num++;
				num2++;
				if (num % upgradesPerRow != 0)
				{
					UnityEngine.Object.Instantiate(upgradeGridVerticalDivider, parent);
				}
			}
			SetMenuNavigation();
			refundButton.SetHold(skipHoldTime);
			autoScroll.RecalculateScrollContentSize();
			UpdateInfoPanel(_upgrades[0]);
			animatedUpgradeIcon.Hide();
		}

		public async void OnOpen()
		{
			try
			{
				currencyText.text = GameDataManager.GetCurrency().ToString();
				GameEvents instance = GameEvents.Instance;
				instance.OnCurrencyChanged = (Action<int>)Delegate.Combine(instance.OnCurrencyChanged, new Action<int>(OnCurrencyChanged));
				await TutorialManager.Instance.META.TryLaunchMetaProgressionTutorial();
			}
			catch (Exception)
			{
			}
			finally
			{
				ambienceInstance = RuntimeManager.CreateInstance(ambienceSound);
				ambienceInstance.start();
				_currentSelectedUpgrade = _upgrades[0];
				SetCurrentSelection();
			}
		}

		public void OnClose()
		{
			CleanBuyAnimationQueue();
			ambienceInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
			ambienceInstance.release();
		}

		private void SetMenuNavigation()
		{
			int num = _upgrades.Length;
			_ = (num - 1) / upgradesPerRow;
			int num2 = (num - 1) % upgradesPerRow;
			for (int i = 0; i < num; i++)
			{
				MetaProgressionUpgradeView obj = _upgrades[i];
				int num3 = i / upgradesPerRow;
				int num4 = i % upgradesPerRow;
				Navigation navigation = new Navigation
				{
					mode = Navigation.Mode.Explicit
				};
				int num5 = Mathf.Min(upgradesPerRow - 1, num - 1 - num3 * upgradesPerRow);
				int num6 = (num - 1 - num4) / upgradesPerRow;
				if (num4 > 0)
				{
					navigation.selectOnLeft = _upgrades[i - 1];
				}
				else
				{
					navigation.selectOnLeft = _upgrades[num3 * upgradesPerRow + num5];
				}
				if (num4 < num5)
				{
					navigation.selectOnRight = _upgrades[i + 1];
				}
				else
				{
					navigation.selectOnRight = _upgrades[num3 * upgradesPerRow];
				}
				if (num3 > 0)
				{
					navigation.selectOnUp = _upgrades[(num3 - 1) * upgradesPerRow + num4];
				}
				else if (num4 > num2)
				{
					navigation.selectOnUp = _upgrades[num - 1];
				}
				else
				{
					navigation.selectOnUp = _upgrades[num6 * upgradesPerRow + num4];
				}
				if (num3 < num6)
				{
					navigation.selectOnDown = _upgrades[(num3 + 1) * upgradesPerRow + num4];
				}
				else if (num4 > num2)
				{
					navigation.selectOnDown = _upgrades[num - 1];
				}
				else
				{
					navigation.selectOnDown = _upgrades[num4];
				}
				obj.navigation = navigation;
			}
		}

		private void OnCurrencyChanged(int currency)
		{
			currencyText.text = currency.ToString();
		}

		public void Close()
		{
			_currentSelectedUpgrade = null;
			GameEvents instance = GameEvents.Instance;
			instance.OnCurrencyChanged = (Action<int>)Delegate.Remove(instance.OnCurrencyChanged, new Action<int>(OnCurrencyChanged));
			GameDataManager.Instance.SaveGameData();
		}

		private void OnUpgradeSelected(MetaProgressionUpgradeView upgradeView)
		{
			_currentSelectedUpgrade = upgradeView;
			UpdateInfoPanel(upgradeView);
			autoScroll.ScrollToSelectedObject(upgradeView.transform.parent as RectTransform);
		}

		private void UpdateInfoPanel(MetaProgressionUpgradeView upgrade)
		{
			MetaStatDatabaseEntry metaStatDatabaseEntry = upgrade.MetaStatDatabaseEntry;
			string term = metaStatDatabaseEntry.name;
			LocalizationMediator.GetTranslation(ref term);
			if (term != null)
			{
				infoTitle.text = term;
			}
			else
			{
				infoTitle.text = metaStatDatabaseEntry.name;
			}
			string term2 = metaStatDatabaseEntry.description;
			LocalizationMediator.GetTranslation(ref term2);
			if (term2 != null)
			{
				infoDescription.text = term2;
			}
			else
			{
				infoDescription.text = metaStatDatabaseEntry.description;
			}
			infoCurrentLVL.text = upgrade.Lvl.ToString();
			infoIcon.texture = upgrade.GetIconTexture();
			if (upgrade.IsMaxlevel)
			{
				normalStatsInfo.SetActive(value: false);
				nextlvLabel.SetActive(value: false);
				maxedStatsInfo.SetActive(value: true);
				float increaseAmmount = metaStatDatabaseEntry.levels[upgrade.Lvl - 1].increaseAmmount;
				if (metaStatDatabaseEntry.type == MetaStatDatabaseEntry.MetaStatDatabaseEntryType.ADD)
				{
					infoMaxedBuff.text = "+ " + increaseAmmount;
				}
				else
				{
					increaseAmmount *= 100f;
					infoMaxedBuff.text = increaseAmmount + "%";
				}
				infoCost.SetMaxedOut();
				SetLock(state: false);
				return;
			}
			maxedStatsInfo.SetActive(value: false);
			normalStatsInfo.SetActive(value: true);
			nextlvLabel.SetActive(value: true);
			int cost = (int)metaStatDatabaseEntry.levels[upgrade.Lvl].cost;
			infoCost.SetCost(cost);
			SetLock(upgrade.IsLocked);
			float num = 0f;
			float num2 = 0f;
			if (upgrade.Lvl != 0)
			{
				num = metaStatDatabaseEntry.levels[upgrade.Lvl - 1].increaseAmmount;
			}
			num2 = metaStatDatabaseEntry.levels[upgrade.Lvl].increaseAmmount;
			if (metaStatDatabaseEntry.type == MetaStatDatabaseEntry.MetaStatDatabaseEntryType.ADD)
			{
				infoCurrentBuff.text = "+ " + num;
				infoNextBuff.text = "+ " + num2;
				return;
			}
			num *= 100f;
			num2 *= 100f;
			infoCurrentBuff.text = num + "%";
			infoNextBuff.text = num2 + "%";
		}

		private void SetLock(bool state)
		{
			infoLockedOverlay.alpha = (state ? 1 : 0);
			infoLockedOverlay.blocksRaycasts = !state;
		}

		internal void SetCurrentSelection()
		{
			if (_currentSelectedUpgrade != null)
			{
				EventSystem.current.SetSelectedGameObject(_currentSelectedUpgrade.gameObject);
			}
		}

		internal void Upgrade()
		{
			if (_currentSelectedUpgrade.Upgrade())
			{
				UpdateInfoPanel(_currentSelectedUpgrade);
				MetaStatDatabaseEntry entry = _currentSelectedUpgrade.MetaStatDatabaseEntry;
				bool isMaxLevel = _currentSelectedUpgrade.IsMaxlevel;
				_buyAnimationQueue.Enqueue(() => RunBuyAnimation(entry, isMaxLevel));
				TryRunBuyAnimationQueue();
			}
			else
			{
				RuntimeManager.PlayOneShot(failToUpgradeSound);
			}
		}

		private async void TryRunBuyAnimationQueue()
		{
			try
			{
				if (!_isBuyAnimationQueueRunning)
				{
					_isBuyAnimationQueueRunning = true;
					while (_buyAnimationQueue.Count > 0)
					{
						await _buyAnimationQueue.Dequeue()();
					}
					_isBuyAnimationQueueRunning = false;
				}
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private async void CleanBuyAnimationQueue()
		{
			_isBuyAnimationQueueRunning = false;
			_buyAnimationQueue.Clear();
			if (Animancer.Layers[1].CurrentState != null)
			{
				Animancer.Layers[1].CurrentState.NormalizedTime = 1f;
			}
			animatedUpgradeIcon.Hide();
		}

		private async UniTask RunBuyAnimation(MetaStatDatabaseEntry metaStatDatabaseEntry, bool isMaxLevel)
		{
			animatedUpgradeIcon.Release();
			MetaProgressionUpgrade3DIcon metaProgression3DIcon = MetaProgressionUpgradeVisualsFactory.GetMetaProgression3DIcon(metaStatDatabaseEntry);
			MetaProgressionUpgrade3DIcon metaProgression3DIcon2 = MetaProgressionUpgradeVisualsFactory.GetMetaProgression3DIcon(metaStatDatabaseEntry, isMaxLevel: true);
			animatedUpgradeIcon.Initialize(metaProgression3DIcon, metaProgression3DIcon2);
			animatedUpgradeIcon.Show();
			RuntimeManager.PlayOneShot(upgradeSound);
			if (isMaxLevel)
			{
				animatedUpgradeIcon.Upgrade();
			}
			else
			{
				animatedUpgradeIcon.Downgrade();
			}
			await AnimancerHelpers.AnimationTask(Animancer, BuyAnim, 1, default(CancellationToken), FadeMode.FromStart);
			await UniTask.NextFrame();
		}

		internal void Refund()
		{
			_currentSelectedUpgrade.Refund();
			UpdateInfoPanel(_currentSelectedUpgrade);
			RuntimeManager.PlayOneShot(refundSound);
		}

		private void OnDestroy()
		{
			GameEvents instance = GameEvents.Instance;
			instance.OnCurrencyChanged = (Action<int>)Delegate.Remove(instance.OnCurrencyChanged, new Action<int>(OnCurrencyChanged));
		}
	}
}
