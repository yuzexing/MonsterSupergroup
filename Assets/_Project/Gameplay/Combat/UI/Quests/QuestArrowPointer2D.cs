using System;
using System.Collections;
using AstralShift.HellMaiden.UI.HUD;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Quests
{
	public class QuestArrowPointer2D : MonoBehaviour
	{
		[SerializeField]
		private RectTransform arrowRectTransform;

		[SerializeField]
		private Image questIcon;

		[SerializeField]
		private UIIconPing iconPing;

		[SerializeField]
		private CustomAnimationCurve animationCurve;

		[SerializeField]
		private RectTransform ghostPointer;

		[SerializeField]
		private float scaleTweenDuration = 2f;

		[SerializeField]
		private int scaleTweenVibrato = 1;

		[SerializeField]
		private float scaleTweenElasticity;

		private Transform _target;

		private RectTransform _parentRectTransform;

		private MinimapIcon.PingMode _pingMode;

		private Action _pingAction;

		private bool _isPinging;

		private Vector2 _offset;

		private bool _showing;

		private bool _isInMoveAnimation;

		[SerializeField]
		private MinimapUIManager.MinimapIconType iconType = MinimapUIManager.MinimapIconType.Quest;

		private Vector2 startingPosition;

		private float duration;

		private Vector2 size;

		public bool Initialized { get; private set; }

		public void Init()
		{
			_parentRectTransform = base.transform.parent.GetComponent<RectTransform>();
			if (iconPing != null)
			{
				iconPing.PulseStarted -= PlayPingSound;
				iconPing.PulseStarted += PlayPingSound;
			}
			Initialized = true;
		}

		private void LateUpdate()
		{
			if (_showing)
			{
				MoveCalc();
				if (!_isInMoveAnimation)
				{
					ApplyPosition();
				}
			}
		}

		private void MoveCalc()
		{
			_offset = (_target.position - MinimapUIManager.Instance.FollowTarget.position) / MinimapUIManager.Instance.HeightInUnits * _parentRectTransform.rect.width;
			_offset = Vector3.ClampMagnitude(_offset, _parentRectTransform.rect.height / 2f);
		}

		private void ApplyPosition()
		{
			arrowRectTransform.anchoredPosition = new Vector2(_offset.x, _offset.y);
		}

		public void SetTarget(Transform target)
		{
			_target = target;
		}

		public void SetQuestIcon(Sprite sprite)
		{
			if (!(sprite == null))
			{
				questIcon.sprite = sprite;
			}
		}

		public void SetPingMode(MinimapIcon.PingMode pingMode = MinimapIcon.PingMode.Continuous)
		{
			_pingMode = pingMode;
			switch (_pingMode)
			{
			case MinimapIcon.PingMode.Continuous:
				_pingAction = RunContinuousPing;
				break;
			case MinimapIcon.PingMode.Once:
				_pingAction = RunPingOnce;
				break;
			}
		}

		private async void RunPingOnce()
		{
			try
			{
				if (!_isPinging)
				{
					_isPinging = true;
					await iconPing.RunPingOnce();
					_isPinging = false;
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private void RunContinuousPing()
		{
			_isPinging = true;
			iconPing.RunContinuousPing();
		}

		private void CancelPing()
		{
			iconPing.CancelPing();
			_isPinging = false;
		}

		public void Show()
		{
			_showing = true;
			questIcon.gameObject.SetActive(value: true);
		}

		public void Hide()
		{
			_showing = false;
			questIcon.gameObject.SetActive(value: false);
			CancelPing();
		}

		public void StartMoveTweenCoroutine(Vector2 startingPosition, float duration, Vector2 size)
		{
			if (base.gameObject.activeInHierarchy)
			{
				this.startingPosition = startingPosition;
				this.duration = duration;
				this.size = size;
				StartCoroutine(MoveTween());
			}
		}

		private IEnumerator MoveTween()
		{
			_isInMoveAnimation = true;
			arrowRectTransform.position = startingPosition;
			float heightInUnits = MinimapUIManager.Instance.HeightInUnits;
			Vector2 vector = MinimapUIManager.Instance.FollowTarget.position;
			Vector3 vector2 = Vector3.ClampMagnitude((Vector2)_target.position - vector, heightInUnits);
			vector2 = vector2 / heightInUnits * (_parentRectTransform.rect.width / 2f);
			ghostPointer.anchoredPosition = vector2;
			arrowRectTransform.rect.Set(1f, 1f, size.x, size.y);
			Tween tween = arrowRectTransform.DOSizeDelta(ghostPointer.sizeDelta, duration);
			Tween tween2 = arrowRectTransform.DOAnchorPos(ghostPointer.localPosition, duration);
			animationCurve.AddEase(tween2);
			animationCurve.AddEase(tween);
			tween2.Play();
			tween.Play();
			yield return new WaitForSeconds(duration);
			_isInMoveAnimation = false;
			_pingAction?.Invoke();
			_pingAction = null;
		}

		private void OnDisable()
		{
			if (_isInMoveAnimation)
			{
				StopAllCoroutines();
				float heightInUnits = MinimapUIManager.Instance.HeightInUnits;
				Vector2 vector = MinimapUIManager.Instance.FollowTarget.position;
				Vector3 vector2 = Vector3.ClampMagnitude((Vector2)_target.position - vector, heightInUnits);
				vector2 = vector2 / heightInUnits * (_parentRectTransform.rect.width / 2f);
				ghostPointer.anchoredPosition = vector2;
				arrowRectTransform.rect.Set(1f, 1f, size.x, size.y);
				arrowRectTransform.sizeDelta = ghostPointer.sizeDelta;
				arrowRectTransform.anchoredPosition = ghostPointer.localPosition;
			}
		}

		private void PlayPingSound(int pingNumber)
		{
			if (iconType != MinimapUIManager.MinimapIconType.None)
			{
				MinimapUIManager.Instance?.PlayPingSound(iconType, pingNumber);
			}
		}

		private void OnDestroy()
		{
			if (iconPing != null)
			{
				iconPing.PulseStarted -= PlayPingSound;
			}
		}
	}
}
