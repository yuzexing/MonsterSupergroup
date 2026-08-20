using System.Collections.Generic;
using System.Linq;
using Animancer;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Achievements;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.GameStats;
using AstralShift.HellMaiden.Player;
using AstralShift.Managers;
using AstralShift.UI;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using FMODUnity;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Menus
{
	public class RunStatsPanel : MonoBehaviour
	{
		[SerializeField]
		private CanvasGroup mainPanel;

		[SerializeField]
		private GameObject panelsContainer;

		[SerializeField]
		private RunStatsWeaponPanel runStatsWeaponPanelPrefab;

		[SerializeField]
		private RunStatsPerkPanel perkPanelPrefab;

		[SerializeField]
		private RunStatsPlayerPanel runStatsPlayerPanel;

		[SerializeField]
		private Image dividerPrefab;

		[SerializeField]
		private AutomaticScroll autoScroll;

		[SerializeField]
		private CustomUIButton closeButton;

		[Header("Animations")]
		[SerializeField]
		private AnimancerComponent animancer;

		[SerializeField]
		private ClipTransition openAnimation;

		[SerializeField]
		private ClipTransition closeAnimation;

		[SerializeField]
		private float delayBetweenPannels = 0.5f;

		[SerializeField]
		private float pannelsFadeTime = 0.5f;

		[Header("Player Bar")]
		[SerializeField]
		private StatusBar healthBar;

		[SerializeField]
		private TextMeshProUGUI maxHealthText;

		[SerializeField]
		private TextMeshProUGUI currentHealthText;

		[SerializeField]
		private TextMeshProUGUI levelText;

		[SerializeField]
		private DashCounter dashCounter;

		[Header("Clock")]
		[SerializeField]
		private UIClock _clock;

		private List<RunStatsWeaponPanel> weaponStatsPanels = new List<RunStatsWeaponPanel>();

		private List<RunStatsPerkPanel> perkStatsPanels = new List<RunStatsPerkPanel>();

		private List<Image> dividerList = new List<Image>();

		private Color clearColor = new Color(1f, 1f, 1f, 0f);

		private EndScreenController _controller;

		[Header("Sounds")]
		[SerializeField]
		private EventReference menuEnterSound;

		[SerializeField]
		private EventReference crossEnterSound;

		[SerializeField]
		private EventReference numberEnterSound;

		[SerializeField]
		private EventReference titleEnterSound;

		[FormerlySerializedAs("enterSound")]
		[SerializeField]
		private EventReference panelEnterSound;

		[SerializeField]
		private EventReference exitSound;

		[Header("Rect Transforms to rebuild")]
		[SerializeField]
		private List<RectTransform> rectTransformsToRebuild = new List<RectTransform>();

		private Sequence _panelsEntranceSequence;

		private AnimancerState _openCloseAnimationState;

		private void Awake()
		{
			mainPanel.alpha = 0f;
			SetMainPanelInteractable(interactable: false);
			closeButton.onSubmit.AddListener(Close);
		}

		public async void Open()
		{
			if (ControllerManager.Instance.CurrentController is EndScreenController)
			{
				_controller = ControllerManager.Instance.CurrentController as EndScreenController;
				_controller.OnLeftStickVertical += autoScroll.ContinuousScroll;
				_controller.OnCenter2Pressed += TryClose;
				_controller.OnUISubmitPressed += TryClose;
				SetPlayerBar();
				CreateStatPanels();
				autoScroll.RecalculateScrollContentSize();
				GameDataManager.JoinRunStats(RunStatsTracker.Instance);
				VerifyAchievements();
				RunStatsTracker.Instance.CleanStatsEntriesLinkedEvents();
				RunStatsTracker.Instance.ResetRunStats();
				base.gameObject.SetActive(value: true);
				foreach (RectTransform item in rectTransformsToRebuild)
				{
					LayoutRebuilder.MarkLayoutForRebuild(item);
				}
				PlayMenuEnterSound();
				await OpenAnimation();
				animancer.Layers[0].Speed = 1f;
				await RunStatsPanelsEntranceSequence();
				SetMainPanelInteractable(interactable: true);
			}
			else
			{
				Debug.LogError("Invalid controller type");
			}
		}

		public async void TryClose()
		{
			pannelsFadeTime = 0.2f;
			animancer.Layers[0].Speed = 2f;
			closeButton?.OnSubmit(null);
		}

		public async void Close()
		{
			SetMainPanelInteractable(interactable: false);
			_controller.OnCenter2Pressed -= TryClose;
			_controller.OnUISubmitPressed -= TryClose;
			PlayExitSound();
			await CloseAnimation();
			_controller.ReturnToHub();
		}

		private void SetMainPanelInteractable(bool interactable)
		{
			mainPanel.interactable = interactable;
			mainPanel.blocksRaycasts = interactable;
		}

		private void SetPlayerBar()
		{
			PlayerMovement player = GameDirector.Instance.Player;
			healthBar.InitializeBar(player.PlayerStats.MaxHP);
			healthBar.SetMaxValue(player.PlayerStats.MaxHP);
			maxHealthText.SetText(player.PlayerStats.MaxHP.ToString());
			healthBar.StatusChange(player.PlayerStats.currentStats.HP);
			healthBar.StatusChange(player.PlayerStats.currentStats.HP);
			currentHealthText.SetText(player.PlayerStats.currentStats.HP.ToString());
			levelText.text = player.leveler.Level.ToString();
			dashCounter.ResetChargeSlots();
		}

		private void VerifyAchievements()
		{
			AchievementVerifier.VerifyAchievements();
		}

		private void CreateStatPanels()
		{
			dividerPrefab.color = clearColor;
			runStatsWeaponPanelPrefab.CanvasGroup.alpha = 0f;
			perkPanelPrefab.CanvasGroup.alpha = 0f;
			CreateWeaponPanel();
			CreatePerksPanel(PlayerHand.Instance.PerksList);
			InitializePlayerStatsPanel(RunStatsTracker.Instance.PlayerStatsEntry);
		}

		private void CreateWeaponPanel()
		{
			int num = 0;
			foreach (PlayerHandSlot slot in PlayerHand.Instance.Slots)
			{
				RuntimeWeaponData runtimeWeaponData = slot.RuntimeWeaponData;
				if (runtimeWeaponData == null)
				{
					continue;
				}
				num++;
				if (RunStatsTracker.Instance.WeaponStatsEntries.TryGetValue(runtimeWeaponData.Data.ID, out var value))
				{
					RunStatsWeaponPanel runStatsWeaponPanel = Object.Instantiate(runStatsWeaponPanelPrefab, panelsContainer.transform);
					runStatsWeaponPanel.Initialize(value, runtimeWeaponData, slot.Equipments);
					weaponStatsPanels.Add(runStatsWeaponPanel);
					if (PlayerHand.Instance.WeaponCount - num > 0)
					{
						Image item = Object.Instantiate(dividerPrefab, panelsContainer.transform);
						dividerList.Add(item);
					}
				}
			}
		}

		private void CreatePerksPanel(List<RuntimePerk> chosenPerks)
		{
			int num = chosenPerks.Count / 3;
			if (chosenPerks.Count % 3 != 0)
			{
				num++;
			}
			if (num == 0)
			{
				return;
			}
			Image item = Object.Instantiate(dividerPrefab, panelsContainer.transform);
			dividerList.Add(item);
			int num2 = 0;
			for (int i = 0; i < num; i++)
			{
				List<RuntimePerk> perksVisuals = chosenPerks.Skip(num2).Take(3).ToList();
				RunStatsPerkPanel runStatsPerkPanel = Object.Instantiate(perkPanelPrefab, panelsContainer.transform);
				runStatsPerkPanel.SetPerksVisuals(perksVisuals);
				perkStatsPanels.Add(runStatsPerkPanel);
				num2 += 3;
				if (num - 1 > i)
				{
					item = Object.Instantiate(dividerPrefab, panelsContainer.transform);
					dividerList.Add(item);
				}
			}
		}

		private void InitializePlayerStatsPanel(PlayerStatsEntry playerStatsEntry)
		{
			runStatsPlayerPanel.PlayerStatsEntry = playerStatsEntry;
			_clock.SetValue(playerStatsEntry.TotalTimeSurvived);
			runStatsPlayerPanel.RefreshValues();
		}

		private async UniTask RunStatsPanelsEntranceSequence()
		{
			_panelsEntranceSequence = DOTween.Sequence();
			int num = 0;
			foreach (RunStatsWeaponPanel weaponStatsPanel in weaponStatsPanels)
			{
				_panelsEntranceSequence.Insert((float)num * delayBetweenPannels, weaponStatsPanel.CanvasGroup.DOFade(1f, pannelsFadeTime));
				_panelsEntranceSequence.InsertCallback((float)num * delayBetweenPannels, PlayPanelEnterSound);
				if (dividerList.Count > num)
				{
					_panelsEntranceSequence.Join(dividerList[num].DOFade(1f, pannelsFadeTime));
				}
				num++;
			}
			foreach (RunStatsPerkPanel perkStatsPanel in perkStatsPanels)
			{
				_panelsEntranceSequence.Insert((float)num * delayBetweenPannels, perkStatsPanel.CanvasGroup.DOFade(1f, pannelsFadeTime));
				_panelsEntranceSequence.InsertCallback((float)num * delayBetweenPannels, PlayPanelEnterSound);
				if (dividerList.Count > num)
				{
					_panelsEntranceSequence.Join(dividerList[num].DOFade(1f, pannelsFadeTime));
				}
				num++;
			}
			_panelsEntranceSequence.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			_panelsEntranceSequence.Play();
			await _panelsEntranceSequence.AsyncWaitForCompletion();
		}

		private async UniTask OpenAnimation()
		{
			await Awaitable.EndOfFrameAsync();
			_openCloseAnimationState = animancer.Layers[0].Play(openAnimation, openAnimation.FadeDuration);
			while (_openCloseAnimationState.IsPlayingAndNotEnding())
			{
				await Awaitable.NextFrameAsync();
			}
		}

		private async UniTask CloseAnimation()
		{
			await Awaitable.EndOfFrameAsync();
			_openCloseAnimationState = animancer.Layers[0].Play(closeAnimation, closeAnimation.FadeDuration);
			while (_openCloseAnimationState.IsPlayingAndNotEnding())
			{
				await Awaitable.NextFrameAsync();
			}
		}

		public void PlayMenuEnterSound()
		{
			if (!panelEnterSound.IsNull)
			{
				RuntimeManager.PlayOneShot(menuEnterSound);
			}
		}

		public void PlayCrossEnterSound()
		{
			if (!crossEnterSound.IsNull)
			{
				RuntimeManager.PlayOneShot(crossEnterSound);
			}
		}

		public void PlayNumbersEnterSound()
		{
			if (!numberEnterSound.IsNull)
			{
				RuntimeManager.PlayOneShot(numberEnterSound);
			}
		}

		public void PlayTitleEnterSound()
		{
			if (!titleEnterSound.IsNull)
			{
				RuntimeManager.PlayOneShot(titleEnterSound);
			}
		}

		public void PlayPanelEnterSound()
		{
			if (!panelEnterSound.IsNull)
			{
				RuntimeManager.PlayOneShot(panelEnterSound);
			}
		}

		public void PlayExitSound()
		{
			if (!exitSound.IsNull)
			{
				RuntimeManager.PlayOneShot(exitSound);
			}
		}
	}
}
