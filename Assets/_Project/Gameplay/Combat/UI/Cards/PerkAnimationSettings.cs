using UnityEngine;

namespace AstralShift.HellMaiden.UI.Cards
{
	[CreateAssetMenu(fileName = "Perk Animation Settings", menuName = "HellMaiden/Data/Perks/PerkAnimationSettings")]
	public class PerkAnimationSettings : ScriptableObject
	{
		[Header("Perk Spawn/Despawn")]
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
		protected float spawnRotationAmount = 360f;

		[SerializeField]
		protected float spawnDescriptionFadeTime = 0.5f;

		[SerializeField]
		protected float spawnDescriptionFadeDelay = 0.5f;

		[SerializeField]
		protected CustomAnimationCurve spawnMoveEase;

		[SerializeField]
		protected CustomAnimationCurve spawnRotationEase;

		[SerializeField]
		protected CustomAnimationCurve spawnDescriptionFadeEase;

		[SerializeField]
		protected float despawnMoveTime = 0.5f;

		[SerializeField]
		protected float despawnMoveDelay = 0.2f;

		[SerializeField]
		protected float despawnRotationTime = 0.2f;

		[SerializeField]
		protected float despawnRotationDelay = 0.2f;

		[SerializeField]
		protected float despawnRotationAmount = 360f;

		[SerializeField]
		protected float despawnDescriptionFadeTime = 0.5f;

		[SerializeField]
		protected float despawnDescriptionFadeDelay = 0.5f;

		[SerializeField]
		protected CustomAnimationCurve despawnMoveEase;

		[SerializeField]
		protected CustomAnimationCurve despawnRotationEase;

		[SerializeField]
		protected CustomAnimationCurve despawnDescriptionFadeEase;

		[Space]
		[Header("Perk Idle")]
		[Space]
		[SerializeField]
		protected float idleMoveOffset = 50f;

		[SerializeField]
		protected float idleMoveTime = 10f;

		[SerializeField]
		protected CustomAnimationCurve idleMoveOffsetEase;

		[Space]
		[Header("Perk Hover")]
		[Space]
		[SerializeField]
		protected float hoverRotationTime = 1f;

		[SerializeField]
		protected float hoverScaleMultiplier = 1.1f;

		[SerializeField]
		protected float hoverScaleTime = 0.5f;

		[SerializeField]
		protected float hoverPunchAngle = 5f;

		[SerializeField]
		protected int hoverVibration = 10;

		[SerializeField]
		protected float hoverElasticity = 1f;

		[SerializeField]
		protected float hoverTiltAmount = 5f;

		[SerializeField]
		protected float hoverTiltSpeed = 10f;

		[SerializeField]
		protected float hoverTiltStopSpeed = 20f;

		[SerializeField]
		protected float hoverGlowFadeTime = 0.15f;

		[SerializeField]
		protected Color hoverGlowColor = Color.white;

		[Space]
		[Header("Perk Selected")]
		[Space]
		[SerializeField]
		protected float selectedVerticalStrength = 0.5f;

		[SerializeField]
		protected float selectedVerticalTime = 0.5f;

		[SerializeField]
		protected int selectedVerticalVibrato = 1;

		[SerializeField]
		protected float selectedVerticalElasticity = 1f;

		[SerializeField]
		protected float selectedRotationTime = 0.5f;

		[SerializeField]
		protected float selectedRotationAmount = 360f;

		[SerializeField]
		protected CustomAnimationCurve selectedRotationEase;

		[SerializeField]
		protected CustomAnimationCurve selectedVerticalEase;

		[Space]
		[Header("Perk Rarity")]
		[Space]
		[SerializeField]
		protected float rarityGlowFadeTime = 0.3f;

		[SerializeField]
		protected CustomAnimationCurve rarityGlowFadeEase;

		[SerializeField]
		protected float rarityGlowScaleAmount = 1.1f;

		[SerializeField]
		protected float rarityGlowScaleTime = 2f;

		[SerializeField]
		protected CustomAnimationCurve rarityGlowScaleEase;

		public float SpawnMoveTime => spawnMoveTime;

		public float SpawnMoveDelay => spawnMoveDelay;

		public float SpawnRotationTime => spawnRotationTime;

		public float SpawnRotationDelay => spawnRotationDelay;

		public float SpawnRotationAmount => spawnRotationAmount;

		public float SpawnDescriptionFadeTime => spawnDescriptionFadeTime;

		public float SpawnDescriptionFadeDelay => spawnDescriptionFadeDelay;

		public CustomAnimationCurve SpawnMoveEase => spawnMoveEase;

		public CustomAnimationCurve SpawnRotationEase => spawnRotationEase;

		public CustomAnimationCurve SpawnDescriptionFadeEase => spawnDescriptionFadeEase;

		public float DespawnMoveTime => despawnMoveTime;

		public float DespawnMoveDelay => despawnMoveDelay;

		public float DespawnRotationTime => despawnRotationTime;

		public float DespawnRotationDelay => despawnRotationDelay;

		public float DespawnRotationAmount => despawnRotationAmount;

		public float DespawnDescriptionFadeTime => despawnDescriptionFadeTime;

		public float DespawnDescriptionFadeDelay => despawnDescriptionFadeDelay;

		public CustomAnimationCurve DespawnMoveEase => despawnMoveEase;

		public CustomAnimationCurve DespawnRotationEase => despawnRotationEase;

		public CustomAnimationCurve DespawnDescriptionFadeEase => despawnDescriptionFadeEase;

		public float IdleMoveOffset => idleMoveOffset;

		public float IdleMoveTime => idleMoveTime;

		public CustomAnimationCurve IdleMoveOffsetEase => idleMoveOffsetEase;

		public float HoverRotationTime => hoverRotationTime;

		public float HoverScaleMultiplier => hoverScaleMultiplier;

		public float HoverScaleTime => hoverScaleTime;

		public float HoverPunchAngle => hoverPunchAngle;

		public int HoverVibration => hoverVibration;

		public float HoverElasticity => hoverElasticity;

		public float HoverTiltAmount => hoverTiltAmount;

		public float HoverTiltSpeed => hoverTiltSpeed;

		public float HoverTiltStopSpeed => hoverTiltStopSpeed;

		public float HoverGlowFadeTime => hoverGlowFadeTime;

		public Color HoverGlowColor => hoverGlowColor;

		public float SelectedVerticalStrength => selectedVerticalStrength;

		public float SelectedVerticalTime => selectedVerticalTime;

		public int SelectedVerticalVibrato => selectedVerticalVibrato;

		public float SelectedVerticalElasticity => selectedVerticalElasticity;

		public float SelectedRotationTime => selectedRotationTime;

		public float SelectedRotationAmount => selectedRotationAmount;

		public CustomAnimationCurve SelectedRotationEase => selectedRotationEase;

		public CustomAnimationCurve SelectedVerticalEase => selectedVerticalEase;

		public float RarityGlowFadeTime => rarityGlowFadeTime;

		public CustomAnimationCurve RarityGlowFadeEase => rarityGlowFadeEase;

		public float RarityGlowScaleAmount => rarityGlowScaleAmount;

		public float RarityGlowScaleTime => rarityGlowScaleTime;

		public CustomAnimationCurve RarityGlowScaleEase => rarityGlowScaleEase;
	}
}
