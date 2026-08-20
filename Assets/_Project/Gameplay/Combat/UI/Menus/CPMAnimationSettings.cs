using UnityEngine;

namespace AstralShift.HellMaiden.UI.Menus
{
	[CreateAssetMenu(fileName = "Card Pick Menu Animation Settings", menuName = "Scriptable Objects/Animations/CPM Animation Settings")]
	public class CPMAnimationSettings : ScriptableObject
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

		public float SpawnMoveTime => spawnMoveTime;

		public float SpawnMoveDelay => spawnMoveDelay;

		public float SpawnRotationTime => spawnRotationTime;

		public float SpawnRotationDelay => spawnRotationDelay;

		public CustomAnimationCurve SpawnMoveEase => spawnMoveEase;

		public float DespawnMoveTime => despawnMoveTime;

		public CustomAnimationCurve DespawnMoveEase => despawnMoveEase;
	}
}
