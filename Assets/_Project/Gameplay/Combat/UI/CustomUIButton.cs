using FMODUnity;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace AstralShift.UI
{
	public class CustomUIButton : UISelectable, ISubmitHandler, IEventSystemHandler, IPointerClickHandler
	{
		public UnityEvent onSubmit;

		public UnityEvent onButtonDown;

		public UnityEvent onButtonUp;

		[SerializeField]
		private CanvasGroup canvasGroup;

		[SerializeField]
		private TextMeshProUGUI text;

		[Header("Sound")]
		[SerializeField]
		private float soundCooldown = 0.3f;

		[FormerlySerializedAs("onClickSucessfull")]
		[SerializeField]
		private EventReference onClickSucessfullSound;

		[FormerlySerializedAs("onClickFailed")]
		[SerializeField]
		private EventReference onClickFailedSound;

		private readonly int FlashingBool = Animator.StringToHash("Flashing");

		private float _lastSuccessfulSoundPlayTime = -999f;

		private float _lastFailedSoundPlayTime = -999f;

		public CanvasGroup CanvasGroup
		{
			get
			{
				if (canvasGroup == null)
				{
					canvasGroup = GetComponent<CanvasGroup>();
				}
				return canvasGroup;
			}
		}

		public TextMeshProUGUI Text => text;

		private new void Awake()
		{
			if (canvasGroup != null)
			{
				canvasGroup = GetComponent<CanvasGroup>();
			}
		}

		public void OnSubmit(BaseEventData eventData)
		{
			if (!IsInteractable())
			{
				PlayClickFailedSound();
				return;
			}
			PlayClickSuccessfulSound();
			onSubmit.Invoke();
		}

		public void OnPointerClick(PointerEventData eventData)
		{
			if (!IsInteractable())
			{
				PlayClickFailedSound();
				return;
			}
			PlayClickSuccessfulSound();
			onSubmit?.Invoke();
		}

		public override void OnPointerDown(PointerEventData eventData)
		{
			if (IsInteractable())
			{
				onButtonDown?.Invoke();
			}
		}

		public override void OnPointerUp(PointerEventData eventData)
		{
			if (IsInteractable())
			{
				onButtonUp?.Invoke();
			}
		}

		public virtual void Flashing(bool flashing)
		{
			highlight.SetBool(FlashingBool, flashing);
		}

		private void PlayClickFailedSound()
		{
			if (!onClickFailedSound.IsNull && Time.unscaledTime - _lastFailedSoundPlayTime >= soundCooldown)
			{
				RuntimeManager.PlayOneShot(onClickFailedSound);
				_lastFailedSoundPlayTime = Time.unscaledTime;
			}
		}

		private void PlayClickSuccessfulSound()
		{
			if (!onClickSucessfullSound.IsNull && Time.unscaledTime - _lastSuccessfulSoundPlayTime >= soundCooldown)
			{
				RuntimeManager.PlayOneShot(onClickSucessfullSound);
				_lastSuccessfulSoundPlayTime = Time.unscaledTime;
				_lastFailedSoundPlayTime = Time.unscaledTime;
			}
		}
	}
}
