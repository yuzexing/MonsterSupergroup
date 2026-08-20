using System;
using System.Threading;
using Animancer;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.DevDebug;
using AstralShift.HellMaiden.Quests;
using AstralShift.HellMaiden.UI.HUD;
using AstralShift.HellMaiden.UI.Menus;
using AstralShift.Managers;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace AstralShift.HellMaiden.UI
{
	public class CombatUIManager : SceneUIManager
	{
		public struct SelectiveHudRequest
		{
			public bool keepBars;

			public bool keepUltimate;

			public bool keepMinimap;

			public bool keepClock;

			public bool keepCooldownCards;

			public bool keepPerksHolder;
		}

		public static CombatUIManager Instance;

		[SerializeField]
		protected Canvas hudCanvas;

		[Header("Player Bars (HP, XP, Level)")]
		[SerializeField]
		private CombatHudElement playerBarElement;

		[SerializeField]
		private StatusBar xpBar;

		[SerializeField]
		private StatusBar hpBar;

		[SerializeField]
		private TMP_Text levelText;

		[SerializeField]
		private TMP_Text currentHealthText;

		[SerializeField]
		private TMP_Text maxHealthText;

		[Space]
		[Header("Ultimate Icon")]
		[SerializeField]
		private CombatHudElement ultimateIconElement;

		[SerializeField]
		private UltimateIcon ultimateIcon;

		[Space]
		[Header("Dash Charges")]
		[SerializeField]
		private CombatHudElement dashChargesElement;

		[FormerlySerializedAs("dashCounter")]
		[SerializeField]
		private DashCounter uiDashCounter;

		[SerializeField]
		private Transform playerDashCounterPrefab;

		private DashCounter _playerDashCounter;

		private GameObject _playerDashCounterParent;

		[Space]
		[Header("Minimap")]
		[SerializeField]
		private CombatHudElement minimapElement;

		[SerializeField]
		private MinimapUIView minimap;

		[Space]
		[Header("Clock")]
		[SerializeField]
		private CombatHudElement mainClockElement;

		[SerializeField]
		private UIClock mainClock;

		[Space]
		[Header("Secondary Clock")]
		[SerializeField]
		private CombatHudElement secondaryClockElement;

		[SerializeField]
		private UIClock secondaryClock;

		[SerializeField]
		private bool isCountdownMode = true;

		[Header("Quest Time Management")]
		private CancellationTokenSource secondaryClockCts;

		private bool isSecondaryClockPaused;

		[Space]
		[Header("Cards Display")]
		[SerializeField]
		private CombatHudElement cardHolderElement;

		[SerializeField]
		private CooldownCardHolder cardHolder;

		[Space]
		[Header("Perks Display")]
		[SerializeField]
		private CombatHudElement perksHolderElement;

		[SerializeField]
		private PerksHolder perksHolder;

		[Space]
		[Header("Menus")]
		public UICardPickMenuView cardPickView;

		[SerializeField]
		private PerkMenuView perkMenu;

		[SerializeField]
		private BestowalMenuView bestowalMenu;

		[FormerlySerializedAs("deathView")]
		[SerializeField]
		private UIEndView endView;

		[SerializeField]
		private StatsMenuController statsMenuController;

		[Header("HUD Animation")]
		[SerializeField]
		private AnimancerComponent animancer;

		[SerializeField]
		protected ClipTransition openAnimation;

		[SerializeField]
		protected ClipTransition closeAnimation;

		[SerializeField]
		private CircleTimeoutOverlay circleTimeoutOverlay;

		[Header("Level Animation")]
		[SerializeField]
		private Animator frameAnimator;

		[SerializeField]
		private Animator textAnimator;

		private readonly int LevelAnimHash = Animator.StringToHash("Play");

		public void Awake()
		{
			Instance = this;
		}

		public override async UniTask Initialize()
		{
			_playerDashCounterParent = UnityEngine.Object.Instantiate(playerDashCounterPrefab, GameDirector.Instance.Player.transform).gameObject;
			_playerDashCounter = _playerDashCounterParent.GetComponentInChildren<DashCounter>();
			int maxHP = GameDirector.Instance.Player.PlayerStats.MaxHP;
			int hP = GameDirector.Instance.Player.PlayerStats.currentStats.HP;
			hpBar?.InitializeBar(maxHP);
			SetMaxHP(maxHP);
			SetHP(hP);
			SetLevel(GameDirector.Instance.Player.leveler.Level);
			ResetDashCharges();
			CloseHUD();
			if ((bool)circleTimeoutOverlay)
			{
				circleTimeoutOverlay.Initialize();
			}
			await UniTask.NextFrame();
			if ((bool)cardHolder)
			{
				cardHolder.Initialize();
			}
			await cardPickView.Init();
			SubscribeGameEvents();
			await UniTask.NextFrame();
		}

		private void OnDestroy()
		{
			secondaryClockCts?.Cancel();
			secondaryClockCts?.Dispose();
			UnityEngine.Object.Destroy(_playerDashCounterParent);
			UnSubscribeGameEvents();
			Instance = null;
		}

		public void OpenHUD(bool instant = false)
		{
			AllHudOn();
			if (instant)
			{
				animancer.Layers[0].Play(openAnimation, openAnimation.FadeDuration).MoveTime(1f, normalized: true);
			}
			else
			{
				animancer?.Layers[0].Play(openAnimation, openAnimation.FadeDuration);
			}
		}

		public void CloseHUD(bool instant = false)
		{
			if (instant)
			{
				animancer.Layers[0].Play(closeAnimation, closeAnimation.FadeDuration).MoveTime(1f, normalized: true);
			}
			else
			{
				animancer?.Layers[0].Play(closeAnimation, closeAnimation.FadeDuration);
			}
			_playerDashCounter?.gameObject.SetActive(value: false);
		}

		public void AllHudOn()
		{
			playerBarElement?.ShowElement();
			mainClockElement?.ShowElement();
			ultimateIconElement?.ShowElement();
			minimapElement?.ShowElement();
			cardHolderElement?.ShowElement();
			cardHolder?.Refresh();
			perksHolderElement?.ShowElement();
			perksHolder?.Refresh();
			_playerDashCounter?.gameObject.SetActive(value: true);
		}

		public void SelectiveHUD(SelectiveHudRequest hudRequest)
		{
			if (playerBarElement != null && !hudRequest.keepBars)
			{
				playerBarElement.HideElement();
			}
			if (mainClockElement != null && !hudRequest.keepClock)
			{
				mainClockElement.HideElement();
			}
			if (ultimateIconElement != null && !hudRequest.keepUltimate)
			{
				ultimateIconElement.HideElement();
			}
			if (minimapElement != null && !hudRequest.keepMinimap)
			{
				minimapElement.HideElement();
			}
			if (cardHolderElement != null && !hudRequest.keepCooldownCards)
			{
				cardHolderElement.HideElement();
			}
			if (perksHolderElement != null && !hudRequest.keepPerksHolder)
			{
				perksHolderElement.HideElement();
			}
		}

		public void OpenStatsMenu(int tabIndex = -1, bool instant = false)
		{
			if (!statsMenuController.blockInputs)
			{
				ControllerManager.Instance.OverrideGameController<StatsMenuController>();
				statsMenuController.Open();
				if (tabIndex != -1)
				{
					statsMenuController.SelectTab(tabIndex, instant);
				}
			}
		}

		public void ShowXP(float xp)
		{
			if ((bool)xpBar)
			{
				xpBar.StatusChangePercentage(xp);
			}
		}

		public void SetHP(int hp)
		{
			if ((bool)hpBar)
			{
				if (currentHealthText != null)
				{
					SetHPText(hp);
				}
				hpBar.StatusChange(hp);
			}
		}

		public void SetHPText(int hp)
		{
			if ((bool)currentHealthText)
			{
				currentHealthText.text = hp.ToString();
			}
		}

		public void SetMaxHP(int maxHP)
		{
			if ((bool)hpBar)
			{
				if (maxHealthText != null)
				{
					SetMaxHPText(maxHP);
				}
				hpBar.SetMaxValue(maxHP);
			}
		}

		public void SetMaxHPText(int maxHP)
		{
			if ((bool)maxHealthText)
			{
				maxHealthText.text = maxHP.ToString();
			}
		}

		private void SubscribeGameEvents()
		{
			GameEvents instance = GameEvents.Instance;
			instance.OnHealthUpdate = (Action<int>)Delegate.Combine(instance.OnHealthUpdate, new Action<int>(SetHP));
			GameEvents instance2 = GameEvents.Instance;
			instance2.OnMaxHealthUpdate = (Action<int>)Delegate.Combine(instance2.OnMaxHealthUpdate, new Action<int>(SetMaxHP));
			GameEvents instance3 = GameEvents.Instance;
			instance3.OnAfterPlayerDeath = (Action)Delegate.Combine(instance3.OnAfterPlayerDeath, new Action(ShowDeathScreen));
			GameEvents instance4 = GameEvents.Instance;
			instance4.OnIncreaseXP = (Action<float>)Delegate.Combine(instance4.OnIncreaseXP, new Action<float>(ShowXP));
			GameEvents instance5 = GameEvents.Instance;
			instance5.OnLevelIncrease = (Action<int>)Delegate.Combine(instance5.OnLevelIncrease, new Action<int>(SetLevel));
			GameEvents instance6 = GameEvents.Instance;
			instance6.ShowOfferingsScreen = (Action)Delegate.Combine(instance6.ShowOfferingsScreen, new Action(LevelUpLogic));
			GameEvents instance7 = GameEvents.Instance;
			instance7.ShowPerksScreen = (Action)Delegate.Combine(instance7.ShowPerksScreen, new Action(OpenPerksMenu));
			GameEvents instance8 = GameEvents.Instance;
			instance8.DashUsed = (Action<int>)Delegate.Combine(instance8.DashUsed, new Action<int>(LooseDashCharge));
			GameEvents instance9 = GameEvents.Instance;
			instance9.OnMaxDashesUpdate = (Action)Delegate.Combine(instance9.OnMaxDashesUpdate, new Action(ResetDashCharges));
			GameEvents instance10 = GameEvents.Instance;
			instance10.DashRestored = (Action<int>)Delegate.Combine(instance10.DashRestored, new Action<int>(RestoreDashCharge));
			GameEvents instance11 = GameEvents.Instance;
			instance11.UltimateGained = (Action)Delegate.Combine(instance11.UltimateGained, new Action(GainUltimateCharge));
			GameEvents instance12 = GameEvents.Instance;
			instance12.UltimateUsed = (Action)Delegate.Combine(instance12.UltimateUsed, new Action(StartUltimateAnimation));
			GameEvents instance13 = GameEvents.Instance;
			instance13.OnCountDownStarted = (Action<float>)Delegate.Combine(instance13.OnCountDownStarted, new Action<float>(StartCountdown));
			GameEvents instance14 = GameEvents.Instance;
			instance14.OnVisualClockChange = (Action<float>)Delegate.Combine(instance14.OnVisualClockChange, new Action<float>(SetClock));
			QuestTimeoutObserver.OnAnyQuestTimeoutStarted += HandleQuestTimeoutStarted;
			QuestTimeoutObserver.OnAnyQuestTimeoutStopped += HandleQuestTimeoutStopped;
			QuestTimeoutObserver.OnAnyQuestTimeoutTick += HandleQuestTimeoutTick;
		}

		private void UnSubscribeGameEvents()
		{
			GameEvents instance = GameEvents.Instance;
			instance.OnHealthUpdate = (Action<int>)Delegate.Remove(instance.OnHealthUpdate, new Action<int>(SetHP));
			GameEvents instance2 = GameEvents.Instance;
			instance2.OnMaxHealthUpdate = (Action<int>)Delegate.Remove(instance2.OnMaxHealthUpdate, new Action<int>(SetMaxHP));
			GameEvents instance3 = GameEvents.Instance;
			instance3.OnAfterPlayerDeath = (Action)Delegate.Remove(instance3.OnAfterPlayerDeath, new Action(ShowDeathScreen));
			GameEvents instance4 = GameEvents.Instance;
			instance4.OnIncreaseXP = (Action<float>)Delegate.Remove(instance4.OnIncreaseXP, new Action<float>(ShowXP));
			GameEvents instance5 = GameEvents.Instance;
			instance5.OnLevelIncrease = (Action<int>)Delegate.Remove(instance5.OnLevelIncrease, new Action<int>(SetLevel));
			GameEvents instance6 = GameEvents.Instance;
			instance6.ShowOfferingsScreen = (Action)Delegate.Remove(instance6.ShowOfferingsScreen, new Action(LevelUpLogic));
			GameEvents instance7 = GameEvents.Instance;
			instance7.ShowPerksScreen = (Action)Delegate.Remove(instance7.ShowPerksScreen, new Action(OpenPerksMenu));
			GameEvents instance8 = GameEvents.Instance;
			instance8.DashUsed = (Action<int>)Delegate.Remove(instance8.DashUsed, new Action<int>(LooseDashCharge));
			GameEvents instance9 = GameEvents.Instance;
			instance9.OnMaxDashesUpdate = (Action)Delegate.Remove(instance9.OnMaxDashesUpdate, new Action(ResetDashCharges));
			GameEvents instance10 = GameEvents.Instance;
			instance10.DashRestored = (Action<int>)Delegate.Remove(instance10.DashRestored, new Action<int>(RestoreDashCharge));
			GameEvents instance11 = GameEvents.Instance;
			instance11.UltimateGained = (Action)Delegate.Remove(instance11.UltimateGained, new Action(GainUltimateCharge));
			GameEvents instance12 = GameEvents.Instance;
			instance12.UltimateUsed = (Action)Delegate.Remove(instance12.UltimateUsed, new Action(StartUltimateAnimation));
			GameEvents instance13 = GameEvents.Instance;
			instance13.OnCountDownStarted = (Action<float>)Delegate.Remove(instance13.OnCountDownStarted, new Action<float>(StartCountdown));
			GameEvents instance14 = GameEvents.Instance;
			instance14.OnVisualClockChange = (Action<float>)Delegate.Remove(instance14.OnVisualClockChange, new Action<float>(SetClock));
			QuestTimeoutObserver.OnAnyQuestTimeoutStarted -= HandleQuestTimeoutStarted;
			QuestTimeoutObserver.OnAnyQuestTimeoutStopped -= HandleQuestTimeoutStopped;
			QuestTimeoutObserver.OnAnyQuestTimeoutTick -= HandleQuestTimeoutTick;
		}

		private void LevelUpLogic()
		{
			OpenCardPickMenu();
		}

		private void OpenPerksMenu()
		{
			if (!DeveloperDebug.PlayWithoutCards)
			{
				perkMenu.Open();
			}
		}

		public void OpenBestowalMenu()
		{
			if (!DeveloperDebug.PlayWithoutCards)
			{
				bestowalMenu.Open();
			}
		}

		private void SetLevel(int level)
		{
			levelText.text = level.ToString() ?? "";
			textAnimator.SetTrigger(LevelAnimHash);
			frameAnimator.SetTrigger(LevelAnimHash);
		}

		public void OpenCardPickMenu()
		{
			if (!DeveloperDebug.PlayWithoutCards)
			{
				cardPickView.Open();
			}
		}

		public void ShowDeathScreen()
		{
			endView.OpenDeathScreen();
		}

		public void ShowWinScreen()
		{
			endView.OpenWinScreen();
		}

		public void SetClock(float seconds)
		{
			mainClock.SetValue(seconds);
		}

		private void SetSecondaryClock(float seconds)
		{
			if (secondaryClock != null)
			{
				secondaryClock.SetValue(seconds);
			}
		}

		public void GainUltimateCharge()
		{
			ultimateIcon.SetCharge(ultimate: true);
		}

		public void LooseUltimateCharge()
		{
			ultimateIcon.SetCharge(ultimate: false);
		}

		private void StartUltimateAnimation()
		{
			ultimateIcon.SetCharge(ultimate: false);
		}

		private void ResetDashCharges()
		{
			uiDashCounter.ResetChargeSlots();
			_playerDashCounter.ResetChargeSlots();
		}

		private void LooseDashCharge(int order)
		{
			uiDashCounter.LooseDashCharge(order);
			_playerDashCounter.LooseDashCharge(order);
		}

		private void RestoreDashCharge(int number)
		{
			uiDashCounter.GainDashCharge(number);
			_playerDashCounter.GainDashCharge(number);
		}

		private void StartCountdown(float time)
		{
			mainClock.StartCountdown(runup: false);
			if ((bool)circleTimeoutOverlay)
			{
				circleTimeoutOverlay.RunStartAnimation();
			}
		}

		private void HandleQuestTimeoutStarted(float timeoutDuration)
		{
			secondaryClockCts?.Cancel();
			secondaryClockCts?.Dispose();
			secondaryClockCts = new CancellationTokenSource();
			secondaryClockElement?.ShowElement();
			secondaryClock.Activate();
			secondaryClock.SetValue(timeoutDuration);
			secondaryClock.StartCountdown(runup: true);
		}

		private void HandleQuestTimeoutStopped()
		{
			secondaryClockCts?.Cancel();
			secondaryClockCts?.Dispose();
			secondaryClockCts = null;
			isSecondaryClockPaused = false;
			secondaryClockElement?.HideElement();
		}

		private void HandleQuestTimeoutTick(float time)
		{
			secondaryClock.SetValue(time);
		}

		public void ToggleSecondaryClockPause()
		{
			if (secondaryClockCts != null && !secondaryClockCts.IsCancellationRequested)
			{
				isSecondaryClockPaused = !isSecondaryClockPaused;
			}
		}

		public void ResetSecondaryClock()
		{
			secondaryClockCts?.Cancel();
			secondaryClockCts?.Dispose();
			secondaryClockCts = null;
			isSecondaryClockPaused = false;
			secondaryClockElement?.HideElement();
		}

		private void Update()
		{
			if ((bool)hudCanvas && Input.GetKeyDown(KeyCode.H))
			{
				DeveloperDebug.DebugHudShowSwitch();
			}
		}

		public void ShowHud()
		{
			minimap?.ShowMinimap();
			minimap?.ActivateMinimap();
			mainClock.Activate();
			hudCanvas.enabled = true;
		}

		public void HideHud()
		{
			hudCanvas.enabled = false;
			mainClock.Deactivate();
		}

		public void HideMinimap()
		{
			minimap?.HideMinimap();
			minimap?.DeactivateMinimap();
		}
	}
}
