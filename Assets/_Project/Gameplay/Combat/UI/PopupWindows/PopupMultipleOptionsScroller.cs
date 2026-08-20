using System;
using System.Collections.Generic;
using AstralShift.DebugTools;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralShift.UI.PopupWindows
{
	public class PopupMultipleOptionsScroller : PopupWindow
	{
		[SerializeField]
		private RectTransform buttonContainerTransform;

		[SerializeField]
		private Button resolutionOptionButtonPrefab;

		[SerializeField]
		private float fadeDurationOnButtonSelection = 0.5f;

		[Header("Scroll Settings")]
		[SerializeField]
		private AutomaticScroll automaticScroll;

		private readonly List<Button> createdButtons = new List<Button>();

		private readonly List<Action> buttonClickHandlers = new List<Action>();

		private readonly List<CanvasGroup> selectedBGsCanvasGroups = new List<CanvasGroup>();

		private PopupContext _context;

		private int closeMenuActionIndex;

		public override async UniTask Open(PopupContext popupContext, PopupController controller)
		{
			try
			{
				_controller = controller;
				_controller.OnUICancelPressed += Close;
				_context = popupContext;
				for (int i = 0; i < popupContext.Texts.Count; i++)
				{
					Button newButton = UnityEngine.Object.Instantiate(resolutionOptionButtonPrefab, buttonContainerTransform);
					newButton.GetComponentInChildren<TMP_Text>().text = popupContext.Texts[i];
					Transform transform = newButton.transform.Find("SelectedBG");
					if (transform != null)
					{
						CanvasGroup canvasGroup = transform.gameObject.GetComponent<CanvasGroup>();
						if (canvasGroup == null)
						{
							canvasGroup = transform.gameObject.AddComponent<CanvasGroup>();
						}
						canvasGroup.alpha = 0f;
						selectedBGsCanvasGroups.Add(canvasGroup);
					}
					int originalIndex = ((popupContext.Indices.Count > i) ? popupContext.Indices[i] : i);
					Action clickHandler = delegate
					{
						OnOptionButtonClicked(originalIndex, newButton);
					};
					newButton.onClick.AddListener(delegate
					{
						clickHandler();
					});
					AddSelectionListener(newButton, i);
					RegisterCloseButtonBindings();
					buttonClickHandlers.Add(clickHandler);
					createdButtons.Add(newButton);
				}
				SetupButtonNavigation(createdButtons);
				if (automaticScroll != null)
				{
					automaticScroll.ScrollTo(1f, instant: true);
				}
				if ((bool)base.canvasGroup)
				{
					base.canvasGroup.blocksRaycasts = true;
				}
				if (automaticScroll != null)
				{
					automaticScroll.RecalculateScrollContentSize();
				}
				_animator.SetTrigger(PopupWindow.OpenAnim);
			}
			catch (Exception ex)
			{
				DBL.Log(DBL.Module.UIMenuWindow, "PopupMultipleOptionsScroller : Failed to load and populate buttons" + ex, 2);
			}
		}

		public override void OnOpen()
		{
			foreach (CanvasGroup selectedBGsCanvasGroup in selectedBGsCanvasGroups)
			{
				if (selectedBGsCanvasGroup != null)
				{
					selectedBGsCanvasGroup.DOFade(0f, fadeDurationOnButtonSelection).SetUpdate(isIndependentUpdate: true);
				}
			}
			int index = 0;
			if (selectedBGsCanvasGroups[index] != null)
			{
				EventSystem.current.SetSelectedGameObject(createdButtons[0].gameObject);
				selectedBGsCanvasGroups[index].DOFade(1f, fadeDurationOnButtonSelection).SetUpdate(isIndependentUpdate: true);
			}
			if ((bool)canvasGroup)
			{
				canvasGroup.interactable = true;
			}
		}

		public override void Close()
		{
			if (_controller != null)
			{
				_controller.OnUICancelPressed -= Close;
			}
			base.Close();
		}

		public override void OnClose()
		{
			foreach (CanvasGroup selectedBGsCanvasGroup in selectedBGsCanvasGroups)
			{
				if (selectedBGsCanvasGroup != null)
				{
					selectedBGsCanvasGroup.DOKill();
				}
			}
			ClearExistingButtons();
			base.OnClose();
			_context.Actions?[closeMenuActionIndex]();
			closeMenuActionIndex = 0;
		}

		private void AddSelectionListener(Button button, int index)
		{
			EventTrigger eventTrigger = button.gameObject.GetComponent<EventTrigger>();
			if (eventTrigger == null)
			{
				eventTrigger = button.gameObject.AddComponent<EventTrigger>();
			}
			eventTrigger.triggers.Clear();
			EventTrigger.Entry entry = new EventTrigger.Entry();
			entry.eventID = EventTriggerType.Select;
			entry.callback.AddListener(delegate
			{
				OnButtonSelected(index, button);
			});
			eventTrigger.triggers.Add(entry);
		}

		private void OnButtonSelected(int index, Button selectedButton)
		{
			foreach (CanvasGroup selectedBGsCanvasGroup in selectedBGsCanvasGroups)
			{
				if (selectedBGsCanvasGroup != null)
				{
					selectedBGsCanvasGroup.DOFade(0f, fadeDurationOnButtonSelection).SetUpdate(isIndependentUpdate: true);
				}
			}
			if (index < selectedBGsCanvasGroups.Count && selectedBGsCanvasGroups[index] != null)
			{
				selectedBGsCanvasGroups[index].DOFade(1f, fadeDurationOnButtonSelection).SetUpdate(isIndependentUpdate: true);
			}
			if (automaticScroll != null && selectedButton != null)
			{
				automaticScroll.ScrollToSelectedObject(selectedButton.GetComponent<RectTransform>());
			}
			if (automaticScroll != null && selectedButton != null)
			{
				if (index == 0)
				{
					automaticScroll.ScrollTo(1f, instant: true);
				}
				else if (index == createdButtons.Count - 1)
				{
					automaticScroll.ScrollTo(0f, instant: true);
				}
				else
				{
					automaticScroll.ScrollToSelectedObject(selectedButton.GetComponent<RectTransform>());
				}
			}
		}

		private void ClearExistingButtons()
		{
			foreach (CanvasGroup selectedBGsCanvasGroup in selectedBGsCanvasGroups)
			{
				if (selectedBGsCanvasGroup != null)
				{
					selectedBGsCanvasGroup.DOKill();
				}
			}
			foreach (Button createdButton in createdButtons)
			{
				if (createdButton != null)
				{
					createdButton.onClick.RemoveAllListeners();
					UnityEngine.Object.Destroy(createdButton.gameObject);
				}
			}
			createdButtons.Clear();
			buttonClickHandlers.Clear();
			selectedBGsCanvasGroups.Clear();
			firstButton = null;
		}

		private void OnOptionButtonClicked(int index, Button clickedButton)
		{
			if (_context.IndicesActions != null && _context.IndicesActions.Count > 0)
			{
				_context.IndicesActions[0]?.Invoke(index);
			}
			closeMenuActionIndex = 1;
			Close();
		}

		private void SetupButtonNavigation(List<Button> buttons)
		{
			if (buttons == null || buttons.Count == 0)
			{
				return;
			}
			for (int i = 0; i < buttons.Count; i++)
			{
				Button button = buttons[i];
				Navigation navigation = new Navigation
				{
					mode = Navigation.Mode.Explicit
				};
				if (i > 0)
				{
					navigation.selectOnUp = buttons[i - 1];
				}
				if (i < buttons.Count - 1)
				{
					navigation.selectOnDown = buttons[i + 1];
					automaticScroll.ScrollTo(0f);
				}
				if (i == 0)
				{
					navigation.selectOnUp = buttons[buttons.Count - 1];
					automaticScroll.ScrollTo(1f);
				}
				if (i == buttons.Count - 1)
				{
					navigation.selectOnDown = buttons[0];
				}
				button.navigation = navigation;
			}
			if (buttons.Count > 0 && EventSystem.current != null)
			{
				EventSystem.current.SetSelectedGameObject(buttons[0].gameObject);
			}
		}
	}
}
