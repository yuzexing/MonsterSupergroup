using System.Collections.Generic;
using DG.Tweening;
using I2.Loc;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.UI
{
	public class MenuTabSelector : MonoBehaviour
	{
		private enum IndicatorMovement
		{
			None = 0,
			Vertical = 1,
			Horizontal = 2
		}

		[Header("Tab Indicator")]
		[SerializeField]
		private IndicatorMovement indicatorMovement;

		[SerializeField]
		private List<Image> tabIndicator;

		[SerializeField]
		private RectTransform originalIndicatorPosition;

		[SerializeField]
		private RectTransform selectedIndicatorPosition;

		[SerializeField]
		private Color selectedIndicatorColor = Color.white;

		[SerializeField]
		private Color unselectedIndicatorColor = Color.grey;

		[SerializeField]
		private float indicatorMoveDuration;

		[Header("Tab Selector Text")]
		[SerializeField]
		private CanvasGroup textCanvasGroup;

		[SerializeField]
		private CustomUIButton previousButton;

		[SerializeField]
		private RectTransform previousButtonGlyph;

		[SerializeField]
		private CustomUIButton nextButton;

		[SerializeField]
		private RectTransform nextButtonGlyph;

		[SerializeField]
		private float tabTextFadeDuration = 0.5f;

		[SerializeField]
		private Color tabButtonColor = Color.gray;

		[SerializeField]
		private TextMeshProUGUI selectedTabText;

		[SerializeField]
		private List<string> optionsKey;

		private Sequence _buttonHighlightSequence;

		private Sequence _adjacentTabsSequence;

		private Sequence _selectTabSequence;

		private Sequence _unselectTabSequence;

		private Sequence _introSequence;

		private bool _canWrap;

		private int _currentTabIdx = -1;

		public CustomUIButton PreviousButton => previousButton;

		public CustomUIButton NextButton => nextButton;

		public int NextTabIndex => (_currentTabIdx + 1) % optionsKey.Count;

		public int PreviousTabIndex
		{
			get
			{
				if (_currentTabIdx <= 0)
				{
					return optionsKey.Count - 1;
				}
				return _currentTabIdx - 1;
			}
		}

		public bool IsLastTab => _currentTabIdx == optionsKey.Count - 1;

		public bool IsFirstTab => _currentTabIdx == 0;

		private string CurrentTabLocKey => optionsKey[_currentTabIdx];

		private string NextTabLocKey
		{
			get
			{
				if (_currentTabIdx != optionsKey.Count - 1)
				{
					return optionsKey[_currentTabIdx + 1];
				}
				return optionsKey[0];
			}
		}

		private string PreviousTabLocKey
		{
			get
			{
				if (_currentTabIdx != 0)
				{
					return optionsKey[_currentTabIdx - 1];
				}
				List<string> list = optionsKey;
				return list[list.Count - 1];
			}
		}

		public void Init(bool canWrap = true)
		{
			_canWrap = canWrap;
			Canvas.ForceUpdateCanvases();
			for (int i = 0; i < tabIndicator.Count; i++)
			{
				tabIndicator[i].color = unselectedIndicatorColor;
				if (indicatorMovement == IndicatorMovement.Horizontal)
				{
					tabIndicator[i].rectTransform.anchoredPosition = new Vector2(originalIndicatorPosition.anchoredPosition.x, tabIndicator[i].rectTransform.anchoredPosition.y);
				}
				if (indicatorMovement == IndicatorMovement.Vertical)
				{
					tabIndicator[i].rectTransform.anchoredPosition = new Vector2(tabIndicator[i].rectTransform.anchoredPosition.x, originalIndicatorPosition.anchoredPosition.y);
				}
			}
			EnableAdjacentTabButtons(state: false);
			LocalizationManager.OnLocalizeEvent += UpdateSelectedTabText;
			LocalizationManager.OnLocalizeEvent += UpdateAdjacentTabsText;
		}

		public void SelectIntroTab(bool rememberLastTab = true)
		{
			_introSequence?.Kill();
			int targetTab = 0;
			if (rememberLastTab && _currentTabIdx != -1)
			{
				targetTab = _currentTabIdx;
			}
			_currentTabIdx = -1;
			_introSequence = DOTween.Sequence();
			_introSequence.AppendCallback(delegate
			{
				for (int i = 0; i < tabIndicator.Count; i++)
				{
					if (!(tabIndicator[i] == null))
					{
						if (indicatorMovement == IndicatorMovement.Vertical)
						{
							float y = originalIndicatorPosition.anchoredPosition.y;
							tabIndicator[i].rectTransform.anchoredPosition = new Vector2(tabIndicator[i].rectTransform.anchoredPosition.x, y);
						}
						else if (indicatorMovement == IndicatorMovement.Horizontal)
						{
							float x = originalIndicatorPosition.anchoredPosition.x;
							tabIndicator[i].rectTransform.anchoredPosition = new Vector2(x, tabIndicator[i].rectTransform.anchoredPosition.y);
						}
						tabIndicator[i].color = unselectedIndicatorColor;
					}
				}
				SelectTabInstant(targetTab);
			});
			_introSequence.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			_introSequence.Play();
		}

		public void SelectTab(int tabIdx)
		{
			if (_currentTabIdx != -1)
			{
				UnselectTabIndicatorTween(_currentTabIdx);
			}
			_currentTabIdx = tabIdx;
			UpdateSelectedTabText();
			AdjacentTabButtonsTween();
			SelectTabIndicatorTween(_currentTabIdx);
		}

		private void AdjacentTabButtonsTween()
		{
			_adjacentTabsSequence?.Kill(complete: true);
			_adjacentTabsSequence = DOTween.Sequence();
			EnableAdjacentTabButtons(state: false);
			_adjacentTabsSequence.Append(textCanvasGroup.DOFade(0f, tabTextFadeDuration));
			_adjacentTabsSequence.AppendCallback(UpdateAdjacentTabsText);
			_adjacentTabsSequence.Append(textCanvasGroup.DOFade(1f, tabTextFadeDuration));
			_adjacentTabsSequence.AppendCallback(delegate
			{
				EnableAdjacentTabButtons(state: true);
			});
			_adjacentTabsSequence.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			_adjacentTabsSequence.Play();
		}

		private void EnableAdjacentTabButtons(bool state)
		{
			if (PreviousButton != null && NextButton != null)
			{
				textCanvasGroup.blocksRaycasts = state;
				textCanvasGroup.interactable = state;
			}
		}

		public void SelectTabInstant(int tabIdx)
		{
			_currentTabIdx = tabIdx;
			UpdateSelectedTabText();
			UpdateAdjacentTabsText();
			EnableAdjacentTabButtons(state: true);
			SelectTabIndicatorInstant(_currentTabIdx);
		}

		private void SelectTabIndicatorInstant(int tabIdx)
		{
			tabIndicator[tabIdx].color = selectedIndicatorColor;
			if (indicatorMovement == IndicatorMovement.Vertical)
			{
				tabIndicator[tabIdx].rectTransform.anchoredPosition = new Vector2(tabIndicator[tabIdx].rectTransform.anchoredPosition.x, selectedIndicatorPosition.anchoredPosition.y);
			}
			if (indicatorMovement == IndicatorMovement.Horizontal)
			{
				tabIndicator[tabIdx].rectTransform.anchoredPosition = new Vector2(selectedIndicatorPosition.anchoredPosition.x, tabIndicator[tabIdx].rectTransform.anchoredPosition.y);
			}
		}

		private void SelectTabIndicatorTween(int tabIdx)
		{
			_selectTabSequence?.Kill(complete: true);
			_selectTabSequence = DOTween.Sequence(this);
			_selectTabSequence.Join(tabIndicator[tabIdx].DOColor(selectedIndicatorColor, indicatorMoveDuration));
			if (indicatorMovement == IndicatorMovement.Vertical)
			{
				_selectTabSequence.Join(tabIndicator[tabIdx].rectTransform.DOAnchorPosY(selectedIndicatorPosition.anchoredPosition.y, indicatorMoveDuration));
			}
			if (indicatorMovement == IndicatorMovement.Horizontal)
			{
				_selectTabSequence.Join(tabIndicator[tabIdx].rectTransform.DOAnchorPosX(selectedIndicatorPosition.anchoredPosition.x, indicatorMoveDuration));
			}
			_selectTabSequence.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			_selectTabSequence.Play();
		}

		private void UnselectTabIndicatorTween(int tabIdx)
		{
			_unselectTabSequence?.Kill(complete: true);
			_unselectTabSequence = DOTween.Sequence(this);
			_unselectTabSequence.Join(tabIndicator[tabIdx].DOColor(unselectedIndicatorColor, indicatorMoveDuration));
			if (indicatorMovement == IndicatorMovement.Vertical)
			{
				_unselectTabSequence.Join(tabIndicator[tabIdx].rectTransform.DOAnchorPosY(originalIndicatorPosition.anchoredPosition.y, indicatorMoveDuration));
			}
			if (indicatorMovement == IndicatorMovement.Horizontal)
			{
				_unselectTabSequence.Join(tabIndicator[tabIdx].rectTransform.DOAnchorPosX(originalIndicatorPosition.anchoredPosition.x, indicatorMoveDuration));
			}
			_unselectTabSequence.SetUpdate(UpdateType.Late, isIndependentUpdate: true);
			_unselectTabSequence.Play();
		}

		private void UpdateSelectedTabText()
		{
			if (_currentTabIdx >= 0 && _currentTabIdx <= optionsKey.Count - 1)
			{
				selectedTabText?.SetText(LocalizationMediator.GetTranslation(CurrentTabLocKey));
			}
		}

		private void UpdateAdjacentTabsText()
		{
			if (!(PreviousButton == null) && !(NextButton == null) && _currentTabIdx != -1)
			{
				if (!_canWrap && IsFirstTab)
				{
					PreviousButton.CanvasGroup.alpha = 0f;
					previousButtonGlyph.gameObject.SetActive(value: false);
				}
				else
				{
					PreviousButton.CanvasGroup.alpha = 1f;
					previousButtonGlyph.gameObject.SetActive(value: true);
					PreviousButton?.Text?.SetText(LocalizationMediator.GetTranslation(PreviousTabLocKey));
				}
				if (!_canWrap && IsLastTab)
				{
					NextButton.CanvasGroup.alpha = 0f;
					nextButtonGlyph.gameObject.SetActive(value: false);
				}
				else
				{
					NextButton.CanvasGroup.alpha = 1f;
					nextButtonGlyph.gameObject.SetActive(value: true);
					NextButton?.Text?.SetText(LocalizationMediator.GetTranslation(NextTabLocKey));
				}
			}
		}

		private void OnDisable()
		{
			_introSequence?.Kill();
		}

		private void OnDestroy()
		{
			LocalizationManager.OnLocalizeEvent -= UpdateSelectedTabText;
			LocalizationManager.OnLocalizeEvent -= UpdateAdjacentTabsText;
			_introSequence?.Kill();
		}
	}
}
