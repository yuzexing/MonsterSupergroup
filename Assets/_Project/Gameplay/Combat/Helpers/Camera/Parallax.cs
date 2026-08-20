using System;
using Com.LuisPedroFonseca.ProCamera2D;
using UnityEngine;

namespace AstralShift.Helpers.Camera
{
	public class Parallax : MonoBehaviour
	{
		public enum ASParallaxUpdateMode
		{
			LateUpdate = 0,
			FixedUpdate = 1,
			Update = 2
		}

		public GameObject Focus;

		public ASParallaxUpdateMode UpdateMode;

		public bool usingProCamera2D = true;

		public bool LockXaxis;

		public float XAxisEffectStrength = 0.1f;

		public bool InvertXDirection;

		public bool LockYaxis;

		public float YAxisEffectStrength = 0.1f;

		public bool InvertYDirection;

		protected int xdirection = -1;

		protected int ydirection = -1;

		protected Vector3 originalPosition;

		private Action OnUpdate;

		private Action OnLateUpdate;

		private Action OnFixedUpdate;

		private void Start()
		{
			if (Focus == null)
			{
				if (usingProCamera2D)
				{
					Focus = ProCamera2D.Instance.GameCamera.gameObject;
				}
				else
				{
					Focus = UnityEngine.Camera.main.gameObject;
				}
			}
			originalPosition = base.transform.position;
			xdirection = (InvertXDirection ? 1 : (-1));
			ydirection = (InvertYDirection ? 1 : (-1));
			switch (UpdateMode)
			{
			case ASParallaxUpdateMode.Update:
				OnUpdate = ApplyParallax;
				break;
			case ASParallaxUpdateMode.LateUpdate:
				OnLateUpdate = ApplyParallax;
				break;
			case ASParallaxUpdateMode.FixedUpdate:
				OnFixedUpdate = ApplyParallax;
				break;
			default:
				OnLateUpdate = ApplyParallax;
				break;
			}
		}

		private void Update()
		{
			OnUpdate?.Invoke();
		}

		private void LateUpdate()
		{
			OnLateUpdate?.Invoke();
		}

		private void FixedUpdate()
		{
			OnFixedUpdate?.Invoke();
		}

		private void ApplyParallax()
		{
			if (Application.isFocused)
			{
				Vector3 vector = Focus.transform.position - originalPosition;
				float num = XAxisEffectStrength * (float)xdirection;
				float num2 = YAxisEffectStrength * (float)ydirection;
				float x = (LockXaxis ? originalPosition.x : (originalPosition.x + vector.x * num));
				float y = (LockYaxis ? originalPosition.y : (originalPosition.y + vector.y * num2));
				base.transform.position = new Vector3(x, y, base.transform.position.z);
			}
		}
	}
}
