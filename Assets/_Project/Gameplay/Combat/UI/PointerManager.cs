using System;
using AstralShift.Control;
using DG.Tweening;
using Rewired;
using Unity.Mathematics;
using UnityEngine;

namespace AstralShift.HellMaiden.UI
{
	public class PointerManager : MonoBehaviour
	{
		public static PointerManager Instance;

		public UIMouseCursorHandler mouseCursorHandler;

		public JoystickCombatPointer combatJoystickPointer;

		[SerializeField]
		private CanvasGroup _mouseCursorCanvasGroup;

		public void Init()
		{
			if (Instance == null)
			{
				Instance = this;
			}
			else
			{
				UnityEngine.Object.Destroy(base.gameObject);
			}
			HideMouseCursor();
			GameDirector.Instance.Settings.OnCursorScaleChange += SetMouseCursorScale;
			GameDirector.Instance.Settings.OnCursorColorChange += SetMouseCursorColor;
			SetMouseCursorScale(GameDirector.Instance.Settings.CursorScale);
			SetMouseCursorColor(GameDirector.Instance.Settings.CursorHue, GameDirector.Instance.Settings.CursorSaturation);
		}

		public void EnableBattlePointer(bool state)
		{
			combatJoystickPointer.Enable(state);
		}

		public void SetBattlePointer()
		{
			if (ControllerLifetime.ActiveControllerType == ControllerType.Joystick)
			{
				mouseCursorHandler.Hide();
				combatJoystickPointer.Show();
			}
			else
			{
				mouseCursorHandler.SetCursorType(UIMouseCursorHandler.CursorType.Battle);
				mouseCursorHandler.Show();
				combatJoystickPointer.Hide();
			}
		}

		public void SetUIPointer()
		{
			if (ControllerLifetime.ActiveControllerType == ControllerType.Joystick)
			{
				mouseCursorHandler.Hide();
				combatJoystickPointer.Hide();
			}
			else
			{
				mouseCursorHandler.SetCursorType(UIMouseCursorHandler.CursorType.UI);
				mouseCursorHandler.Show();
				combatJoystickPointer.Hide();
			}
		}

		public void SetPointerForMenuNavigation()
		{
			if (ControllerLifetime.ActiveControllerType == ControllerType.Joystick || ControllerLifetime.ActiveControllerType == ControllerType.Keyboard)
			{
				mouseCursorHandler.Hide();
				combatJoystickPointer.Hide();
			}
			else
			{
				mouseCursorHandler.SetCursorType(UIMouseCursorHandler.CursorType.UI);
				mouseCursorHandler.Show();
				combatJoystickPointer.Hide();
			}
		}

		public void SetMouseCursorScale(float value)
		{
			float cursorScale = ((!(value >= 5f)) ? math.remap(0f, 5f, 0.25f, 1f, value) : math.remap(5f, 10f, 1f, 2f, value));
			mouseCursorHandler.SetCursorScale(cursorScale);
		}

		public void SetMouseCursorColor(float hueValue, float saturationValue)
		{
			float hueValue2 = math.remap(0f, 10f, 0f, 1f, hueValue);
			float saturationValue2 = math.remap(0f, 10f, 0f, 1f, saturationValue);
			mouseCursorHandler.SetCursorColor(hueValue2, saturationValue2);
		}

		public void HideMouseCursor()
		{
			mouseCursorHandler.Hide();
		}

		public void FadeInMouseCursor(float duration = -1f, Action onComplete = null)
		{
			if (!(mouseCursorHandler == null))
			{
				float duration2 = ((duration > 0f) ? duration : 0.5f);
				mouseCursorHandler.Show();
				_mouseCursorCanvasGroup.DOKill();
				_mouseCursorCanvasGroup.alpha = 0f;
				_mouseCursorCanvasGroup.DOFade(1f, duration2).OnComplete(delegate
				{
					onComplete?.Invoke();
				});
			}
		}

		public void FadeOutMouseCursor(float duration = -1f, Action onComplete = null)
		{
			if (!(mouseCursorHandler == null))
			{
				float duration2 = ((duration > 0f) ? duration : 0.5f);
				_mouseCursorCanvasGroup.DOKill();
				_mouseCursorCanvasGroup.DOFade(0f, duration2).OnComplete(delegate
				{
					mouseCursorHandler.Hide();
					onComplete?.Invoke();
				});
			}
		}
	}
}
