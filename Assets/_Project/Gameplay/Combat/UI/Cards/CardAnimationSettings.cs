using UnityEngine;

namespace AstralShift.HellMaiden.UI.Cards
{
	[CreateAssetMenu(fileName = "Card Animation Settings", menuName = "Scriptable Objects/Animations/Card Animation Settings")]
	public class CardAnimationSettings : ScriptableObject
	{
		[SerializeField]
		protected float followSpeed = 5f;

		[SerializeField]
		protected float followRotationAmount = 100f;

		[SerializeField]
		protected float followRotationSpeed = 20f;

		[SerializeField]
		protected float followRotationMaxAngle = 60f;

		[SerializeField]
		protected float followScaleSpeed = 8f;

		[SerializeField]
		protected float followScaleSensibilityTime = 0.2f;

		[SerializeField]
		protected float idleMoveOffset = 50f;

		[SerializeField]
		protected float idleMoveTime = 10f;

		[SerializeField]
		protected CustomAnimationCurve idleMoveOffsetEase;

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

		[SerializeField]
		protected Color hoverGlowColorHSV = Color.white;

		[SerializeField]
		protected float equipRotationTime = 0.25f;

		[SerializeField]
		protected CustomAnimationCurve equipRotationEase;

		[SerializeField]
		protected float equipScaleTime = 0.1f;

		[SerializeField]
		protected CustomAnimationCurve equipScaleEase;

		[Header("Merge Start")]
		[SerializeField]
		protected float mergeStartPositioningTime = 0.5f;

		[SerializeField]
		protected CustomAnimationCurve mergeStartPositioningEase;

		[SerializeField]
		protected float mergeStartGlowTime = 1f;

		[SerializeField]
		protected CustomAnimationCurve mergeStartGlowEase;

		[Space]
		[Header("Merge End")]
		[SerializeField]
		protected float mergeEndPositioningTime = 0.5f;

		[SerializeField]
		protected float mergeEndPositioningDelayTime = 0.75f;

		[SerializeField]
		protected CustomAnimationCurve mergeEndPositioningEase;

		[SerializeField]
		protected float mergeEndPinchAmount = 0.03f;

		[SerializeField]
		protected float mergeEndPinchInTime = 0.2f;

		[SerializeField]
		protected CustomAnimationCurve mergeEndPinchInEase;

		[SerializeField]
		protected float mergeEndPinchOutTime = 0.05f;

		[SerializeField]
		protected CustomAnimationCurve mergeEndPinchOutEase;

		[SerializeField]
		protected float mergeEndScaleMultiplier = 1.1f;

		[SerializeField]
		protected float mergeEndScaleUpTime = 0.3f;

		[SerializeField]
		protected CustomAnimationCurve mergeEndScaleUpEase;

		[SerializeField]
		protected float mergeEndScaleDownTime = 0.1f;

		[SerializeField]
		protected CustomAnimationCurve mergeEndScaleDownEase;

		[SerializeField]
		protected float mergeEndGlowTime = 1f;

		[SerializeField]
		protected CustomAnimationCurve mergeEndGlowEase;

		[Space]
		[SerializeField]
		protected float mergeEndSheenTime = 0.5f;

		[SerializeField]
		protected float mergeEndSheenMinWidth = 0.1f;

		[SerializeField]
		protected float mergeEndSheenMaxWidth = 0.15f;

		[SerializeField]
		protected CustomAnimationCurve mergeEndSheenEase;

		[Space]
		[Header("Merge Spiral")]
		[SerializeField]
		protected float mergeStartSpiralTime = 2f;

		[SerializeField]
		protected float mergeStartSpiralRadius = 300f;

		[SerializeField]
		protected float mergeStartSpiralLaps = 2f;

		[SerializeField]
		protected CustomAnimationCurve mergeStartSpiralEase;

		[SerializeField]
		protected CustomAnimationCurve mergeStartSpiralAttractionEase;

		[Space]
		[Header("Merge Glow")]
		[SerializeField]
		protected float mergeGlowColorIntensity = 50f;

		[SerializeField]
		protected float mergeGlowGlobalIntensity = 100f;

		[SerializeField]
		protected float mergeGlowBlurIntensity = 100f;

		[SerializeField]
		protected float mergeGlowChromaticAberrationIntensity = 1f;

		[SerializeField]
		[Tooltip("We use speed because distance is variable")]
		protected float magnetLockMovementSpeed = 2000f;

		[SerializeField]
		protected CustomAnimationCurve magnetLockMovementEase;

		[SerializeField]
		protected float magnetLockRotationSpeed = 2000f;

		[SerializeField]
		protected CustomAnimationCurve magnetLockRotationEase;

		[SerializeField]
		protected float magnetLockScale = 5.5f;

		[SerializeField]
		protected float magnetLockScaleSpeed = 2000f;

		[SerializeField]
		protected CustomAnimationCurve magnetLockScaleEase;

		[SerializeField]
		protected float magnetUnlockTime = 0.5f;

		[SerializeField]
		protected CustomAnimationCurve magnetUnlockScaleEase;

		[SerializeField]
		protected Color32 magnetLockOverlayColor = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, 50);

		[SerializeField]
		protected float magnetLockOverlayFadeTime = 0.15f;

		[SerializeField]
		protected float magnetLockOverlayBlinkTime = 1.5f;

		[SerializeField]
		protected float magnetLockOverlayMinFadeAmount = 0.1f;

		[SerializeField]
		protected float magnetLockOverlayMaxFadeAmount = 0.2f;

		[SerializeField]
		protected float magnetLockOverlayGlowAmount = 6f;

		[SerializeField]
		protected Color32 weaponCompatibilityColor = new Color32(0, 150, byte.MaxValue, byte.MaxValue);

		[SerializeField]
		protected float weaponCompatibilityFadeTime = 0.15f;

		[SerializeField]
		protected float weaponCompatibilityBlinkTime = 1.5f;

		[SerializeField]
		protected float weaponCompatibilityMinFadeAmount = 0.1f;

		[SerializeField]
		protected float weaponCompatibilityMaxFadeAmount = 0.2f;

		[SerializeField]
		protected float weaponCompatibilityOverlayGlowAmount = 6f;

		[SerializeField]
		protected Color32 mergeCompatibilityColor = new Color32(225, 150, 0, byte.MaxValue);

		[SerializeField]
		protected float mergeCompatibilityFadeTime = 0.15f;

		[SerializeField]
		protected float mergeCompatibilityBlinkTime = 1.5f;

		[SerializeField]
		protected float mergeCompatibilityMagnetLockBlinkTime = 1f;

		[SerializeField]
		protected float mergeCompatibilityMinFadeAmount = 0.1f;

		[SerializeField]
		protected float mergeCompatibilityMaxFadeAmount = 0.2f;

		[SerializeField]
		protected float mergeCompatibilityOverlayGlowAmount = 6f;

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

		[SerializeField]
		protected Color32 discardGlowColor = new Color32(200, 0, 0, byte.MaxValue);

		[SerializeField]
		protected float discardOverlayGlowAmount = 6f;

		[SerializeField]
		protected float discardOverlayBlendMaxAmount = 1f;

		[SerializeField]
		protected float discardPunchAngle = 10f;

		[SerializeField]
		protected int discardPunchVibrato = 10;

		[SerializeField]
		protected float discardPunchRandomness = 5f;

		[SerializeField]
		protected CustomAnimationCurve discardPunchEase;

		[SerializeField]
		protected float discardScaleMultiplier = 1.1f;

		[SerializeField]
		protected Color32 banishGlowColor = new Color32(150, 0, 225, byte.MaxValue);

		[SerializeField]
		protected float banishOverlayGlowAmount = 6f;

		[SerializeField]
		protected float banishOverlayBlendMaxAmount = 1f;

		[SerializeField]
		protected float banishPunchAngle = 10f;

		[SerializeField]
		protected int banishPunchVibrato = 10;

		[SerializeField]
		protected float banishPunchRandomness = 5f;

		[SerializeField]
		protected CustomAnimationCurve banishPunchEase;

		[SerializeField]
		protected float banishScaleMultiplier = 1.1f;

		[SerializeField]
		protected float reRollPunchAngle = 10f;

		[SerializeField]
		protected int reRollPunchVibrato = 10;

		[SerializeField]
		protected float reRollPunchRandomness = 5f;

		[SerializeField]
		protected CustomAnimationCurve reRollPunchEase;

		[SerializeField]
		protected float reRollScaleMultiplier = 0.85f;

		[SerializeField]
		protected Color32 reRollGlowColor = new Color32(100, byte.MaxValue, 100, byte.MaxValue);

		[SerializeField]
		protected float reRollGlowFadeAmount = 0.2f;

		[SerializeField]
		protected float reRollGlowOverlayAmount = 6f;

		[SerializeField]
		protected float reRollStopFadeTime = 0.15f;

		public float FollowSpeed => followSpeed;

		public float FollowRotationAmount => followRotationAmount;

		public float FollowRotationSpeed => followRotationSpeed;

		public float FollowRotationMaxAngle => followRotationMaxAngle;

		public float FollowScaleSpeed => followScaleSpeed;

		public float FollowScaleSensibilityTime => followScaleSensibilityTime;

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

		public Color HoverGlowColorHSV => hoverGlowColorHSV;

		public float EquipRotationTime => equipRotationTime;

		public CustomAnimationCurve EquipRotationEase => equipRotationEase;

		public float EquipScaleTime => equipScaleTime;

		public CustomAnimationCurve EquipScaleEase => equipScaleEase;

		public float MergeStartPositioningTime => mergeStartPositioningTime;

		public CustomAnimationCurve MergeStartPositioningEase => mergeStartPositioningEase;

		public float MergeStartGlowTime => mergeStartGlowTime;

		public CustomAnimationCurve MergeStartGlowEase => mergeStartGlowEase;

		public float MergeEndPositioningTime => mergeEndPositioningTime;

		public float MergeEndPositioningDelayTime => mergeEndPositioningDelayTime;

		public CustomAnimationCurve MergeEndPositioningEase => mergeEndPositioningEase;

		public float MergeEndPinchAmount => mergeEndPinchAmount;

		public float MergeEndPinchInTime => mergeEndPinchInTime;

		public CustomAnimationCurve MergeEndPinchInEase => mergeEndPinchInEase;

		public float MergeEndPinchOutTime => mergeEndPinchOutTime;

		public CustomAnimationCurve MergeEndPinchOutEase => mergeEndPinchOutEase;

		public float MergeEndScaleMultiplier => mergeEndScaleMultiplier;

		public float MergeEndScaleUpTime => mergeEndScaleUpTime;

		public CustomAnimationCurve MergeEndScaleUpEase => mergeEndScaleUpEase;

		public float MergeEndScaleDownTime => mergeEndScaleDownTime;

		public CustomAnimationCurve MergeEndScaleDownEase => mergeEndScaleDownEase;

		public float MergeEndGlowTime => mergeEndGlowTime;

		public CustomAnimationCurve MergeEndGlowEase => mergeEndGlowEase;

		public float MergeEndSheenTime => mergeEndSheenTime;

		public float MergeEndSheenMinWidth => mergeEndSheenMinWidth;

		public float MergeEndSheenMaxWidth => mergeEndSheenMaxWidth;

		public CustomAnimationCurve MergeEndSheenEase => mergeEndSheenEase;

		public float MergeStartSpiralTime => mergeStartSpiralTime;

		public float MergeStartSpiralRadius => mergeStartSpiralRadius;

		public float MergeStartSpiralLaps => mergeStartSpiralLaps;

		public CustomAnimationCurve MergeStartSpiralEase => mergeStartSpiralEase;

		public CustomAnimationCurve MergeStartSpiralAttractionEase => mergeStartSpiralAttractionEase;

		public float MergeGlowColorIntensity => mergeGlowColorIntensity;

		public float MergeGlowGlobalIntensity => mergeGlowGlobalIntensity;

		public float MergeGlowBlurIntensity => mergeGlowBlurIntensity;

		public float MergeGlowChromaticAberrationIntensity => mergeGlowChromaticAberrationIntensity;

		public float MagnetLockMovementSpeed => magnetLockMovementSpeed;

		public CustomAnimationCurve MagnetLockMovementEase => magnetLockMovementEase;

		public float MagnetLockRotationSpeed => magnetLockRotationSpeed;

		public CustomAnimationCurve MagnetLockRotationEase => magnetLockRotationEase;

		public float MagnetLockScale => magnetLockScale;

		public float MagnetLockScaleSpeed => magnetLockScaleSpeed;

		public CustomAnimationCurve MagnetLockScaleEase => magnetLockScaleEase;

		public float MagnetUnlockTime => magnetUnlockTime;

		public CustomAnimationCurve MagnetUnlockScaleEase => magnetUnlockScaleEase;

		public Color32 MagnetLockOverlayColor => magnetLockOverlayColor;

		public float MagnetLockOverlayFadeTime => magnetLockOverlayFadeTime;

		public float MagnetLockOverlayBlinkTime => magnetLockOverlayBlinkTime;

		public float MagnetLockOverlayMinFadeAmount => magnetLockOverlayMinFadeAmount;

		public float MagnetLockOverlayMaxFadeAmount => magnetLockOverlayMaxFadeAmount;

		public float MagnetLockOverlayGlowAmount => magnetLockOverlayGlowAmount;

		public Color32 WeaponCompatibilityColor => weaponCompatibilityColor;

		public float WeaponCompatibilityFadeTime => weaponCompatibilityFadeTime;

		public float WeaponCompatibilityBlinkTime => weaponCompatibilityBlinkTime;

		public float WeaponCompatibilityMinFadeAmount => weaponCompatibilityMinFadeAmount;

		public float WeaponCompatibilityMaxFadeAmount => weaponCompatibilityMaxFadeAmount;

		public float WeaponCompatibilityOverlayGlowAmount => weaponCompatibilityOverlayGlowAmount;

		public Color32 MergeCompatibilityColor => mergeCompatibilityColor;

		public float MergeCompatibilityFadeTime => mergeCompatibilityFadeTime;

		public float MergeCompatibilityBlinkTime => mergeCompatibilityBlinkTime;

		public float MergeCompatibilityMagnetLockBlinkTime => mergeCompatibilityMagnetLockBlinkTime;

		public float MergeCompatibilityMinFadeAmount => mergeCompatibilityMinFadeAmount;

		public float MergeCompatibilityMaxFadeAmount => mergeCompatibilityMaxFadeAmount;

		public float MergeCompatibilityOverlayGlowAmount => mergeCompatibilityOverlayGlowAmount;

		public float RarityGlowFadeTime => rarityGlowFadeTime;

		public CustomAnimationCurve RarityGlowFadeEase => rarityGlowFadeEase;

		public float RarityGlowScaleAmount => rarityGlowScaleAmount;

		public float RarityGlowScaleTime => rarityGlowScaleTime;

		public CustomAnimationCurve RarityGlowScaleEase => rarityGlowScaleEase;

		public Color32 DiscardGlowColor => discardGlowColor;

		public float DiscardOverlayGlowAmount => discardOverlayGlowAmount;

		public float DiscardOverlayBlendMaxAmount => discardOverlayBlendMaxAmount;

		public float DiscardPunchAngle => discardPunchAngle;

		public int DiscardPunchVibrato => discardPunchVibrato;

		public float DiscardPunchRandomness => discardPunchRandomness;

		public CustomAnimationCurve DiscardPunchEase => discardPunchEase;

		public float DiscardScaleMultiplier => discardScaleMultiplier;

		public Color32 BanishGlowColor => banishGlowColor;

		public float BanishOverlayGlowAmount => banishOverlayGlowAmount;

		public float BanishOverlayBlendMaxAmount => banishOverlayBlendMaxAmount;

		public float BanishPunchAngle => banishPunchAngle;

		public int BanishPunchVibrato => banishPunchVibrato;

		public float BanishPunchRandomness => banishPunchRandomness;

		public CustomAnimationCurve BanishPunchEase => banishPunchEase;

		public float BanishScaleMultiplier => banishScaleMultiplier;

		public float ReRollPunchAngle => reRollPunchAngle;

		public int ReRollPunchVibrato => reRollPunchVibrato;

		public float ReRollPunchRandomness => reRollPunchRandomness;

		public CustomAnimationCurve ReRollPunchEase => reRollPunchEase;

		public float ReRollScaleMultiplier => reRollScaleMultiplier;

		public Color32 ReRollGlowColor => reRollGlowColor;

		public float ReRollGlowFadeAmount => reRollGlowFadeAmount;

		public float ReRollGlowOverlayAmount => reRollGlowOverlayAmount;

		public float ReRollStopFadeTime => reRollStopFadeTime;
	}
}
