using AstralShift.HellMaiden;
using UnityEngine;

namespace AstralShift.Managers
{
	public class OccludeeManager : MonoBehaviour
	{
		[SerializeField]
		private float fadeDistance = 2f;

		[SerializeField]
		private float fadeSoftness = 2f;

		[SerializeField]
		private float fadeMinAlpha = 0.75f;

		private readonly int _occluderPositionId = Shader.PropertyToID("_OccluderPosition");

		public static OccludeeManager Instance { get; private set; }

		public float FadeDistance => fadeDistance;

		public float FadeSoftness => fadeSoftness;

		public float FadeMinAlpha => fadeMinAlpha;

		private void Awake()
		{
			Instance = this;
		}

		private void LateUpdate()
		{
			if (Time.timeScale != 0f)
			{
				SetPlayerPosition();
			}
		}

		private void SetPlayerPosition()
		{
			Shader.SetGlobalVector(_occluderPositionId, GameDirector.Instance.Player.CurrentPosition);
		}
	}
}
