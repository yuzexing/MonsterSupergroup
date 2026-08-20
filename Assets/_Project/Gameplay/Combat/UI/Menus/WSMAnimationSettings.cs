using UnityEngine;

namespace AstralShift.HellMaiden.UI.Menus
{
	[CreateAssetMenu(fileName = "Weapon Selection Menu Animation Settings", menuName = "Scriptable Objects/Animations/WSM Animation Settings")]
	public class WSMAnimationSettings : ScriptableObject
	{
		[Header("Card Spawn / Despawn")]
		[Space]
		[SerializeField]
		protected float spawnMoveTime = 0.5f;

		[SerializeField]
		protected float spawnMoveDelay = 0.5f;

		[SerializeField]
		protected float spawnRotationTime = 0.2f;

		[SerializeField]
		protected float spawnRotationDelay = 0.2f;

		[SerializeField]
		protected CustomAnimationCurve spawnMoveEase;

		[SerializeField]
		protected float despawnMoveTime = 0.5f;

		[SerializeField]
		protected CustomAnimationCurve despawnMoveEase;

		[Space]
		[Header("Light Rays")]
		[Space]
		[SerializeField]
		protected float lightRaysFadeDuration = 0.1f;

		[SerializeField]
		protected float lightRaysColorShiftDuration = 0.1f;

		[SerializeField]
		protected float frameGemsColorShiftDuration = 0.1f;

		[Space]
		[Header("Info Panel")]
		[Space]
		[SerializeField]
		protected float infoPanelTextFadeDuration = 0.1f;

		public float SpawnMoveTime => spawnMoveTime;

		public float SpawnMoveDelay => spawnMoveDelay;

		public float SpawnRotationTime => spawnRotationTime;

		public float SpawnRotationDelay => spawnRotationDelay;

		public CustomAnimationCurve SpawnMoveEase => spawnMoveEase;

		public float DespawnMoveTime => despawnMoveTime;

		public CustomAnimationCurve DespawnMoveEase => despawnMoveEase;

		public float LightRaysFadeDuration => lightRaysFadeDuration;

		public float LightRaysColorShiftDuration => lightRaysColorShiftDuration;

		public float FrameGemsColorShiftDuration => frameGemsColorShiftDuration;

		public float InfoPanelTextFadeDuration => infoPanelTextFadeDuration;
	}
}
