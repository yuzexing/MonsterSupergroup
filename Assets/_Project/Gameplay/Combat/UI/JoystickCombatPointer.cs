using AstralShift.HellMaiden.Player;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralShift.HellMaiden.UI
{
	[RequireComponent(typeof(RectTransform), typeof(Canvas))]
	public class JoystickCombatPointer : UIBehaviour
	{
		public PlayerMovement playerMovement;

		private RectTransform _transform;

		[SerializeField]
		private Canvas canvas;

		protected override void Awake()
		{
			canvas = GetComponent<Canvas>();
			_transform = GetComponent<RectTransform>();
		}

		private void LateUpdate()
		{
			if (canvas.enabled && (bool)playerMovement)
			{
				SetDirection(playerMovement.attackDirection);
			}
		}

		public void SetDirection(Vector2 value)
		{
			_transform.localEulerAngles = new Vector3(45f, 0f, Vector2.SignedAngle(Vector2.down, value));
		}

		public void Enable(bool state)
		{
			base.gameObject.SetActive(state);
		}

		public void Show()
		{
			canvas.enabled = true;
		}

		public void Hide()
		{
			canvas.enabled = false;
		}
	}
}
