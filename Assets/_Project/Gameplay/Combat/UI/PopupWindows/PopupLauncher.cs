using System;
using System.Collections.Generic;
using AstralShift.Helpers;
using AstralShift.Managers;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.UI.PopupWindows
{
	public class PopupLauncher : MonoBehaviour
	{
		private struct PopupRequestData
		{
			public PopupType Type;

			public PopupContext Context;

			public Button FirstButton;

			public Action<PopupWindow> Callback;

			public PopupController Controller;
		}

		public enum PopupType
		{
			SmallAlert = 0,
			SmallChoice = 1,
			MediumAlert = 2,
			MediumChoice = 3,
			ControllerDisconnect = 4,
			ScrollBarMultipleChoice = 5,
			UIGeneric = 6,
			LargeInfo = 7,
			Multipage = 8,
			Overlay = 9
		}

		public static PopupLauncher Instance;

		private CanvasGroup canvasGroup;

		private Dictionary<PopupType, PopupWindow> _popupWindowsLUT = new Dictionary<PopupType, PopupWindow>();

		private List<PopupType> _pendingRequests = new List<PopupType>();

		private bool _isControllerDisconnectOpen;

		private const string PopupKeyPrefix = "Popup/";

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			canvasGroup = GetComponent<CanvasGroup>();
			TryCachePreInstantiatedPopups();
		}

		private void TryCachePreInstantiatedPopups()
		{
			PopupWindow[] componentsInChildren = GetComponentsInChildren<PopupWindow>(includeInactive: true);
			foreach (PopupWindow popupWindow in componentsInChildren)
			{
				_popupWindowsLUT[popupWindow.type] = popupWindow;
			}
		}

		public async UniTask<PopupWindow> RequestPopup(PopupType popupType, PopupContext popupContext, Button firstButton = null, Action<PopupWindow> callback = null)
		{
			PopupController popupController = ControllerManager.Instance.OverrideGameController<PopupController>();
			if (popupType == PopupType.ControllerDisconnect)
			{
				if (_isControllerDisconnectOpen)
				{
					Debug.LogWarning("Controller disconnect is already open, preventing another one from appearing.");
					return null;
				}
				_isControllerDisconnectOpen = true;
			}
			else
			{
				if (popupType == PopupType.Overlay)
				{
					popupController.KeepHUDOpen();
				}
				popupController.onPopupLaunch += delegate
				{
					if ((bool)canvasGroup)
					{
						canvasGroup.blocksRaycasts = true;
						canvasGroup.interactable = true;
					}
				};
				popupController.onDeactivate += delegate
				{
					if ((bool)canvasGroup)
					{
						canvasGroup.blocksRaycasts = false;
						canvasGroup.interactable = false;
					}
				};
			}
			if (_popupWindowsLUT.TryGetValue(popupType, out var popupWindow))
			{
				if (firstButton != null)
				{
					popupWindow.firstButton = firstButton;
				}
				await popupController.LaunchPopup(popupWindow, popupContext);
				callback?.Invoke(popupWindow);
				return popupWindow;
			}
			if (_pendingRequests.Contains(popupType))
			{
				return null;
			}
			string text = popupType.ToString().ToLowerInvariant();
			string addressableKey = "Popup/" + text;
			PopupRequestData data = new PopupRequestData
			{
				Type = popupType,
				Context = popupContext,
				FirstButton = firstButton,
				Callback = callback,
				Controller = popupController
			};
			_pendingRequests.Add(popupType);
			return await InstantiateNewPopup(addressableKey, data);
		}

		private async UniTask<PopupWindow> InstantiateNewPopup(string addressableKey, PopupRequestData data)
		{
			GameObject gameObject = UnityEngine.Object.Instantiate(await AddressableHelpers.LoadAssetAsyncWithHandle<GameObject>(addressableKey).Task, base.transform);
			PopupWindow popupWindow = gameObject.GetComponent<PopupWindow>();
			popupWindow.firstButton = data.FirstButton;
			popupWindow.Init();
			await data.Controller.LaunchPopup(popupWindow, data.Context);
			data.Callback?.Invoke(popupWindow);
			_pendingRequests.Remove(data.Type);
			if (data.Type == PopupType.ControllerDisconnect)
			{
				data.Controller.onDeactivate += delegate
				{
					Debug.Log("Closing controller disconnect");
					_isControllerDisconnectOpen = false;
				};
			}
			_popupWindowsLUT.TryAdd(data.Type, popupWindow);
			return popupWindow;
		}
	}
}
