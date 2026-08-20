using System;
using System.Linq;
using AstralShift.Managers;
using Cysharp.Threading.Tasks;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace AstralShift.UI.PopupWindows
{
	[RequireComponent(typeof(PopupWindowButton), typeof(PopupWindowText))]
	public class PopupWindow : MonoBehaviour
	{
		[SerializeField]
		protected CanvasGroup canvasGroup;

		public PopupLauncher.PopupType type;

		protected PopupController _controller;

		public Button firstButton;

		public Button cancelButton;

		public Action<PopupContext> onContextGet;

		public Action onClose;

		public Action onAfterClose;

		[Header("Sounds")]
		[SerializeField]
		protected EventReference openPopupSound;

		[FormerlySerializedAs("soundOnOpen")]
		[SerializeField]
		protected EventReference soundLoopReference;

		protected EventInstance _soundLoopInstance;

		[SerializeField]
		protected EventReference closePopupSound;

		protected Animator _animator;

		private PopupWindowButton[] _windowButtons;

		private PopupWindowComponent[] _windowComponents;

		protected static readonly int OpenAnim = Animator.StringToHash("Open");

		protected static readonly int CloseAnim = Animator.StringToHash("Close");

		private bool _isInitialized;

		private bool _isTemporary;

		public PopupWindowComponent[] Components => _windowComponents;

		public virtual void Init()
		{
			if (_isInitialized)
			{
				return;
			}
			canvasGroup = GetComponent<CanvasGroup>();
			_animator = GetComponent<Animator>();
			_windowButtons = GetComponents<PopupWindowButton>();
			PopupWindowButton[] windowButtons = _windowButtons;
			for (int i = 0; i < windowButtons.Length; i++)
			{
				windowButtons[i].Init();
			}
			Button button = _windowButtons.First().GetButton();
			if (!button)
			{
				Debug.LogWarning("No Button Assigned to Popup Window!");
			}
			else if (!firstButton)
			{
				firstButton = button;
			}
			cancelButton = _windowButtons.FirstOrDefault((PopupWindowButton f) => f.Index == _windowButtons.Length - 1)?.GetButton();
			_windowComponents = GetComponents<PopupWindowComponent>();
			try
			{
				if (!soundLoopReference.IsNull)
				{
					_soundLoopInstance = RuntimeManager.CreateInstance(soundLoopReference);
				}
			}
			catch (EventNotFoundException ex)
			{
				Debug.LogWarning(ex.Message, this);
			}
			_isInitialized = true;
		}

		public virtual async UniTask Open(PopupContext popupContext, PopupController controller)
		{
			_controller = controller;
			if ((bool)canvasGroup)
			{
				canvasGroup.blocksRaycasts = true;
				canvasGroup.interactable = true;
			}
			RegisterCloseButtonBindings();
			PopupWindowComponent[] windowComponents = _windowComponents;
			foreach (PopupWindowComponent popupWindowComponent in windowComponents)
			{
				onContextGet = (Action<PopupContext>)Delegate.Combine(onContextGet, new Action<PopupContext>(popupWindowComponent.SetContext));
				onClose = (Action)Delegate.Combine(onClose, new Action(popupWindowComponent.ClearContext));
			}
			EventSystem.current.SetSelectedGameObject(base.gameObject);
			if (popupContext.Position != Vector2.zero)
			{
				base.transform.position = popupContext.Position;
			}
			onContextGet?.Invoke(popupContext);
			onContextGet = null;
			_animator.SetTrigger(OpenAnim);
			if (!soundLoopReference.IsNull)
			{
				_soundLoopInstance.start();
			}
			if (!openPopupSound.IsNull)
			{
				RuntimeManager.PlayOneShot(openPopupSound);
			}
		}

		public virtual void OnOpen()
		{
			EventSystem.current.SetSelectedGameObject(firstButton?.gameObject);
		}

		public virtual void InvokeCancelButton()
		{
			cancelButton?.OnSubmit(new BaseEventData(EventSystem.current));
		}

		public virtual void Close()
		{
			if ((bool)canvasGroup)
			{
				canvasGroup.blocksRaycasts = false;
				canvasGroup.interactable = false;
			}
			UnRegisterCloseButtonBindings();
			if (!soundLoopReference.IsNull)
			{
				_soundLoopInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
			}
			if (!closePopupSound.IsNull)
			{
				RuntimeManager.PlayOneShot(closePopupSound);
			}
			if (_animator != null)
			{
				_animator.SetTrigger(CloseAnim);
			}
			EventSystem.current.SetSelectedGameObject(null);
			onClose?.Invoke();
			onClose = null;
		}

		public virtual void OnClose()
		{
			ControllerManager.Instance.YieldGameController();
			onAfterClose?.Invoke();
			onAfterClose = null;
			if (_isTemporary)
			{
				UnityEngine.Object.Destroy(base.gameObject);
			}
		}

		public void SetContext(PopupContext popupContext)
		{
			onContextGet?.Invoke(popupContext);
		}

		public void SetAsTemporary()
		{
			_isTemporary = true;
		}

		protected void OnDestroy()
		{
			if (_soundLoopInstance.isValid())
			{
				_soundLoopInstance.release();
			}
		}

		protected virtual void RegisterCloseButtonBindings()
		{
			_controller.OnUICancelPressed += InvokeCancelButton;
		}

		protected virtual void UnRegisterCloseButtonBindings()
		{
			_controller.OnUICancelPressed -= InvokeCancelButton;
		}
	}
}
