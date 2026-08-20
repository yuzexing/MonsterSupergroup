using System;
using AstralShift.Helpers;
using AstralShift.Managers;
using Cysharp.Threading.Tasks;
using FMODUnity;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralShift.UI.PopupWindows
{
	public class MultipagePopupWindow : PopupWindow
	{
		[Header("References")]
		[SerializeField]
		private Button nextButton;

		[SerializeField]
		private Button previousButton;

		[SerializeField]
		private PopupWindowButton finishButton;

		[SerializeField]
		private CanvasGroup pagesLabel;

		[SerializeField]
		private TMP_Text pageIndex;

		[SerializeField]
		private TMP_Text totalPages;

		[SerializeField]
		private RectTransform contentContainer;

		[Header("Glyphs")]
		[SerializeField]
		private LayoutGroup glyphsLayoutGroup;

		[SerializeField]
		private RectTransform previousGlyph;

		[SerializeField]
		private RectTransform nextGlyph;

		[SerializeField]
		private RectTransform confirmGlyph;

		[Header("Multipage Sounds")]
		[SerializeField]
		private EventReference pageMoveSound;

		[SerializeField]
		private EventReference pageMoveSuccesfullSound;

		[SerializeField]
		private EventReference pageMoveFailedSound;

		private int _pageIndex;

		private PopupContext _context;

		private PopupWindowContent _content;

		public int TotalPages => _content.Pages.Count;

		public override async UniTask Open(PopupContext popupContext, PopupController controller)
		{
			_ = 1;
			try
			{
				firstButton = null;
				_controller = controller;
				_context = popupContext;
				if ((bool)canvasGroup)
				{
					canvasGroup.blocksRaycasts = true;
					canvasGroup.interactable = true;
				}
				GameObject gameObject = await AddressableHelpers.LoadAssetAsyncWithHandle<GameObject>(_context.ContentValue.AssetReference).Task;
				if (!gameObject)
				{
					Debug.LogError("MultipagePopupWindow : Missing PopupWindowContent addressable reference.");
					return;
				}
				if (!UnityEngine.Object.Instantiate(gameObject, contentContainer).TryGetComponent<PopupWindowContent>(out _content))
				{
					Debug.LogError("MultipagePopupWindow : PopupWindowContent not found in loaded prefab.");
					return;
				}
				RectTransform component = _content.GetComponent<RectTransform>();
				component.anchoredPosition = contentContainer.anchoredPosition;
				component.anchorMax = contentContainer.anchorMax;
				component.anchorMin = contentContainer.anchorMin;
				component.sizeDelta = contentContainer.sizeDelta;
				component.localScale = contentContainer.localScale;
				component.pivot = contentContainer.pivot;
				await UniTask.NextFrame(PlayerLoopTiming.PostLateUpdate);
				_pageIndex = 0;
				if (TotalPages == 1)
				{
					pagesLabel.alpha = 0f;
					nextButton?.gameObject.SetActive(value: false);
					previousButton?.gameObject.SetActive(value: false);
					previousGlyph?.gameObject.SetActive(value: false);
					nextGlyph?.gameObject.SetActive(value: false);
					confirmGlyph?.gameObject.SetActive(value: false);
					finishButton?.GetButton().gameObject.SetActive(value: true);
					RegisterCloseButtonBindings();
					if ((bool)glyphsLayoutGroup)
					{
						LayoutRebuilder.ForceRebuildLayoutImmediate(glyphsLayoutGroup.transform as RectTransform);
					}
				}
				else
				{
					pagesLabel.alpha = 1f;
					previousButton?.gameObject.SetActive(value: false);
					previousGlyph?.gameObject.SetActive(value: false);
					nextGlyph?.gameObject.SetActive(value: true);
					nextButton?.gameObject.SetActive(value: true);
					confirmGlyph?.gameObject.SetActive(value: false);
					RegisterPreviousPageBindings();
					RegisterNextPageBindings();
					totalPages.text = "/" + TotalPages;
					pageIndex.text = "1";
					glyphsLayoutGroup?.gameObject.SetActive(value: true);
					if ((bool)glyphsLayoutGroup)
					{
						LayoutRebuilder.ForceRebuildLayoutImmediate(glyphsLayoutGroup.transform as RectTransform);
					}
					finishButton?.GetButton().gameObject.SetActive(value: false);
				}
				finishButton?.SetContext(_context);
				onClose = (Action)Delegate.Combine(onClose, (Action)delegate
				{
					finishButton?.ClearContext();
				});
				_content.Pages[_pageIndex].Open();
				_animator.SetTrigger(PopupWindow.OpenAnim);
				if (!soundLoopReference.IsNull)
				{
					_soundLoopInstance.start();
				}
			}
			catch (Exception)
			{
				Debug.LogError("MultipagePopupWindow : Failed to load PopupWindowContent");
			}
		}

		public override void Close()
		{
			glyphsLayoutGroup?.gameObject.SetActive(value: false);
			UnRegisterPreviousPageBindings();
			UnRegisterNextPageBindings();
			base.Close();
		}

		public override void OnClose()
		{
			ControllerManager.Instance.YieldGameController();
			onAfterClose?.Invoke();
			onAfterClose = null;
			DestroyContent();
		}

		public void NextPage()
		{
			if (_pageIndex == TotalPages - 1)
			{
				if (!pageMoveFailedSound.IsNull)
				{
					RuntimeManager.PlayOneShot(pageMoveFailedSound);
				}
				return;
			}
			if (!pageMoveSuccesfullSound.IsNull)
			{
				RuntimeManager.PlayOneShot(pageMoveSuccesfullSound);
			}
			if (!pageMoveSound.IsNull)
			{
				RuntimeManager.PlayOneShot(pageMoveSound);
			}
			_content.Pages[_pageIndex].Close();
			_pageIndex++;
			_content.Pages[_pageIndex].Open();
			pageIndex.text = (_pageIndex + 1).ToString();
			nextButton?.gameObject.SetActive(_pageIndex < TotalPages - 1);
			nextGlyph?.gameObject.SetActive(_pageIndex < TotalPages - 1);
			previousButton?.gameObject.SetActive(_pageIndex > 0);
			previousGlyph?.gameObject.SetActive(_pageIndex > 0);
			if (_pageIndex == TotalPages - 1)
			{
				RegisterCloseButtonBindings();
			}
			previousButton?.gameObject.SetActive(value: true);
		}

		public void PreviousPage()
		{
			if (_pageIndex == 0)
			{
				if (!pageMoveFailedSound.IsNull)
				{
					RuntimeManager.PlayOneShot(pageMoveFailedSound);
				}
				return;
			}
			if (!pageMoveSuccesfullSound.IsNull)
			{
				RuntimeManager.PlayOneShot(pageMoveSuccesfullSound);
			}
			if (!pageMoveSound.IsNull)
			{
				RuntimeManager.PlayOneShot(pageMoveSound);
			}
			if (_pageIndex < TotalPages - 1)
			{
				UnRegisterCloseButtonBindings();
			}
			_content.Pages[_pageIndex].Close();
			_pageIndex--;
			_content.Pages[_pageIndex].Open();
			pageIndex.text = (_pageIndex + 1).ToString();
			nextButton?.gameObject.SetActive(_pageIndex < TotalPages - 1);
			nextGlyph?.gameObject.SetActive(_pageIndex < TotalPages - 1);
			previousButton?.gameObject.SetActive(_pageIndex > 0);
			previousGlyph?.gameObject.SetActive(_pageIndex > 0);
		}

		private void DestroyContent()
		{
			_context.ContentValue.AssetReference.ReleaseAsset();
			UnityEngine.Object.Destroy(_content.gameObject);
			_content = null;
		}

		protected override void RegisterCloseButtonBindings()
		{
			_controller.OnUISubmitPressed += InvokeFinishButton;
			finishButton?.GetButton().gameObject.SetActive(value: true);
			confirmGlyph?.gameObject.SetActive(value: true);
		}

		protected override void UnRegisterCloseButtonBindings()
		{
			_controller.OnUISubmitPressed -= InvokeFinishButton;
			finishButton?.GetButton().gameObject.SetActive(value: false);
			confirmGlyph?.gameObject.SetActive(value: false);
		}

		private void InvokeFinishButton()
		{
			finishButton?.GetButton().OnSubmit(new BaseEventData(EventSystem.current));
		}

		private void RegisterPreviousPageBindings()
		{
			_controller.OnUIDirectionLeftPressed += PreviousPage;
			previousButton?.onClick.AddListener(PreviousPage);
		}

		private void UnRegisterPreviousPageBindings()
		{
			_controller.OnUIDirectionLeftPressed -= PreviousPage;
			previousButton?.onClick.RemoveAllListeners();
		}

		private void RegisterNextPageBindings()
		{
			_controller.OnUISubmitPressed += NextPage;
			_controller.OnUIDirectionRightPressed += NextPage;
			nextButton?.onClick.AddListener(NextPage);
		}

		private void UnRegisterNextPageBindings()
		{
			_controller.OnUISubmitPressed -= NextPage;
			_controller.OnUIDirectionRightPressed -= NextPage;
			nextButton?.onClick.RemoveAllListeners();
		}
	}
}
