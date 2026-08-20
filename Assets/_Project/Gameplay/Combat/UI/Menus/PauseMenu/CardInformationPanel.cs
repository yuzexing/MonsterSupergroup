using System;
using System.Linq;
using System.Text;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player.Attacks;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Menus.PauseMenu
{
	public class CardInformationPanel : MonoBehaviour
	{
		private enum WeaponStatDisplayContext
		{
			Signature = 0,
			Weapon = 1,
			OwnSource = 2,
			OtherSource = 3
		}

		[Serializable]
		private struct InfoPanelStatEntry
		{
			[Header("Card")]
			[SerializeField]
			private TextMeshProUGUI changedStatText;

			[SerializeField]
			private TextMeshProUGUI totalStatText;

			[SerializeField]
			private TextMeshProUGUI arrowText;

			[Space]
			[Header("Perk")]
			[SerializeField]
			private CanvasGroup perkStatCanvasGroup;

			[SerializeField]
			private PerkModifierID perkModifierID;

			[SerializeField]
			private TextMeshProUGUI perkStatText;

			[Space]
			[Header("Shrine")]
			[SerializeField]
			private CanvasGroup shrineStatCanvasGroup;

			[SerializeField]
			private PerkModifierID shrineModifierID;

			[SerializeField]
			private TextMeshProUGUI shrineStatText;

			public TextMeshProUGUI ChangedStatText => changedStatText;

			public TextMeshProUGUI TotalStatText => totalStatText;

			public TextMeshProUGUI ArrowText => arrowText;

			public CanvasGroup PerkStatCanvasGroup => perkStatCanvasGroup;

			public PerkModifierID PerkModifierID => perkModifierID;

			public TextMeshProUGUI PerkStatText => perkStatText;

			public CanvasGroup ShrineStatCanvasGroup => shrineStatCanvasGroup;

			public PerkModifierID ShrineModifierID => shrineModifierID;

			public TextMeshProUGUI ShrineStatText => shrineStatText;
		}

		[Header("Card Title And Description")]
		[SerializeField]
		private TextMeshProUGUI cardNameTxt;

		[SerializeField]
		private TextMeshProUGUI cardDescriptionTxt;

		[Space]
		[Header("Stats")]
		[SerializeField]
		private RectTransform statsContainer;

		[Space]
		[SerializeField]
		private bool displayShrineValues;

		[SerializeField]
		private GameObject[] shrinesGroups;

		[Space]
		[SerializeField]
		private bool displayPerkValues;

		[SerializeField]
		private GameObject[] perksGroups;

		[Space]
		[SerializeField]
		private InfoPanelStatEntry damageStat;

		[Space]
		[SerializeField]
		private InfoPanelStatEntry speedStat;

		[Space]
		[SerializeField]
		private InfoPanelStatEntry critDamageStat;

		[Space]
		[SerializeField]
		private InfoPanelStatEntry critRateStat;

		[Space]
		[SerializeField]
		private InfoPanelStatEntry sizeStat;

		[Space]
		[SerializeField]
		private InfoPanelStatEntry durationStat;

		[Space]
		[SerializeField]
		private InfoPanelStatEntry projectileCountStat;

		[Space]
		[SerializeField]
		private TextMeshProUGUI effectsTxt;

		[SerializeField]
		private string onHitStringKey = "STT_OnHit";

		[SerializeField]
		private string onKillStringKey = "STT_OnKill";

		[Space]
		[SerializeField]
		private Color decreaseColor = Color.red;

		[SerializeField]
		private Color increaseColor = Color.green;

		[SerializeField]
		private Color modifiedColor = Color.orange;

		[SerializeField]
		private Color deHighlightedColor = new Color(1f, 1f, 1f, 0.3f);

		[Space]
		[Header("Ultimate Info")]
		[SerializeField]
		private TextMeshProUGUI ultimateLabelTxt;

		[SerializeField]
		private TextMeshProUGUI ultimateNameTxt;

		[SerializeField]
		private TextMeshProUGUI ultimateDescriptionTxt;

		[SerializeField]
		private string ultimateLabelKey = "ULT_Label";

		[SerializeField]
		private string naKey = "STT_na";

		private const string ModifierNameI2Prefix = "STT_";

		private const string OnHitStringFormat = ": {0} {1} {2 }%";

		private const string OnKillStringFormat = ": {0} {1} {2} %";

		private const uint WEAPON_HOMER_SHIELD_ID = 302u;

		public async void ShowWeaponStatsText(WeaponBehaviour weapon, WeaponData weaponData)
		{
			ShowEmptyText();
			if ((bool)weapon)
			{
				cardNameTxt.text = weaponData.GetTitle();
				cardDescriptionTxt.text = weaponData.GetDescription();
				WeaponStatDisplayContext displayContext = WeaponStatDisplayContext.Weapon;
				WeaponBehaviourStats.StatFormulaMultipliers formulaMultipliers = WeaponBehaviourStats.StatFormulaMultipliers.BaseAndPlayer;
				float statValue = weapon.StatsBehaviour.GetStatValue(AttackStatType.Damage, formulaMultipliers);
				float statValue2 = weapon.StatsBehaviour.GetStatValue(AttackStatType.Speed, formulaMultipliers);
				float statValue3 = weapon.StatsBehaviour.GetStatValue(AttackStatType.CritDamage, formulaMultipliers);
				float statValue4 = weapon.StatsBehaviour.GetStatValue(AttackStatType.CritRate, formulaMultipliers);
				float statValue5 = weapon.StatsBehaviour.GetStatValue(AttackStatType.Size, formulaMultipliers);
				float statValue6 = weapon.StatsBehaviour.GetStatValue(AttackStatType.Duration, formulaMultipliers);
				float statValue7 = weapon.StatsBehaviour.GetStatValue(AttackStatType.ProjectileCount, formulaMultipliers);
				SetWeaponStat(ref damageStat, weaponData.GetBaseDamage(), statValue, AttackStatType.Damage, displayContext);
				SetWeaponStat(ref speedStat, weaponData.GetBaseSpeed(), weapon.GetAttacksPerSecond(statValue2), AttackStatType.Speed, displayContext);
				SetWeaponStat(ref critDamageStat, weaponData.GetBaseCritMultiplier(), statValue3, AttackStatType.CritDamage, displayContext);
				SetWeaponStat(ref critRateStat, weaponData.GetBaseCritRate(), statValue4, AttackStatType.CritRate, displayContext);
				SetWeaponStat(ref sizeStat, weaponData.GetBaseSize(), statValue5, AttackStatType.Size, displayContext);
				SetWeaponStat(ref durationStat, weaponData.GetBaseDuration(), statValue6, AttackStatType.Duration, displayContext);
				SetWeaponStat(ref projectileCountStat, weaponData.GetBaseProjectileCount(), statValue7, AttackStatType.ProjectileCount, displayContext);
				SetShrineStats(weapon, weaponData);
				SetPerkStats(weapon, weaponData);
				SetInvalidStatsText(weaponData);
				SetOnHitAndKillModifierIcons(weapon);
				RefreshPanel().Forget();
			}
		}

		public void ShowSignatureWeaponStatsText(WeaponData weaponData)
		{
			ShowEmptyText();
			if ((bool)weaponData)
			{
				cardNameTxt.text = weaponData.GetTitle();
				cardDescriptionTxt.text = weaponData.GetDescription();
				WeaponStatDisplayContext displayContext = WeaponStatDisplayContext.Signature;
				SetWeaponStat(ref damageStat, 0f, weaponData.GetBaseDamage(), AttackStatType.Damage, displayContext);
				SetWeaponStat(ref speedStat, 0f, weaponData.GetBaseSpeed(), AttackStatType.Speed, displayContext);
				SetWeaponStat(ref critDamageStat, 0f, weaponData.GetBaseCritMultiplier(), AttackStatType.CritDamage, displayContext);
				SetWeaponStat(ref critRateStat, 0f, weaponData.GetBaseCritRate(), AttackStatType.CritRate, displayContext);
				SetWeaponStat(ref sizeStat, 0f, weaponData.GetBaseSize(), AttackStatType.Size, displayContext);
				SetWeaponStat(ref durationStat, 0f, weaponData.GetBaseDuration(), AttackStatType.Duration, displayContext);
				SetWeaponStat(ref projectileCountStat, 0f, weaponData.GetBaseProjectileCount(), AttackStatType.ProjectileCount, displayContext);
				SetInvalidStatsText(weaponData);
				if (weaponData.IsSignature && (bool)weaponData.UltimateData)
				{
					ultimateLabelTxt.text = LocalizationMediator.GetTranslation(ultimateLabelKey);
					ultimateNameTxt.text = weaponData.UltimateData.GetTitle();
					ultimateDescriptionTxt.text = weaponData.UltimateData.GetDescription();
				}
				RefreshPanel().Forget();
			}
		}

		public void ShowEquipmentStatsText(WeaponBehaviour weapon, WeaponData weaponData, RuntimeEquipmentData equipment)
		{
			EquipmentData data = equipment.Data;
			cardNameTxt.text = data.GetTitle();
			cardDescriptionTxt.text = data.GetDescription(equipment.LevelIndex);
			EquipmentLevelModifiersData equipmentLevelModifiersData = data.Levels[equipment.LevelIndex];
			EquipmentDataModifier[] staticStatModifiers = equipmentLevelModifiersData.GetStaticStatModifiers();
			staticStatModifiers = staticStatModifiers.Where((EquipmentDataModifier modifier) => !modifier.HasMultiSlotConfig || (modifier.HasMultiSlotConfig && modifier.MultiSlot.IsSelfApplied)).ToArray();
			WeaponStatDisplayContext displayContext = WeaponStatDisplayContext.OtherSource;
			ShowEmptyText();
			WeaponBehaviourStats.StatFormulaMultipliers formulaMultipliers = WeaponBehaviourStats.StatFormulaMultipliers.BaseAndPlayer;
			float statValue = weapon.StatsBehaviour.GetStatValue(AttackStatType.Damage, formulaMultipliers);
			float statValue2 = weapon.StatsBehaviour.GetStatValue(AttackStatType.Speed, formulaMultipliers);
			float statValue3 = weapon.StatsBehaviour.GetStatValue(AttackStatType.CritDamage, formulaMultipliers);
			float statValue4 = weapon.StatsBehaviour.GetStatValue(AttackStatType.CritRate, formulaMultipliers);
			float statValue5 = weapon.StatsBehaviour.GetStatValue(AttackStatType.Size, formulaMultipliers);
			float statValue6 = weapon.StatsBehaviour.GetStatValue(AttackStatType.Duration, formulaMultipliers);
			float statValue7 = weapon.StatsBehaviour.GetStatValue(AttackStatType.ProjectileCount, formulaMultipliers);
			SetWeaponStat(ref damageStat, weaponData.GetBaseDamage(), statValue, AttackStatType.Damage, displayContext);
			SetWeaponStat(ref speedStat, weaponData.GetBaseSpeed(), weapon.GetAttacksPerSecond(statValue2), AttackStatType.Speed, displayContext);
			SetWeaponStat(ref critDamageStat, weaponData.GetBaseCritMultiplier(), statValue3, AttackStatType.CritDamage, displayContext);
			SetWeaponStat(ref critRateStat, weaponData.GetBaseCritRate(), statValue4, AttackStatType.CritRate, displayContext);
			SetWeaponStat(ref sizeStat, weaponData.GetBaseSize(), statValue5, AttackStatType.Size, displayContext);
			SetWeaponStat(ref durationStat, weaponData.GetBaseDuration(), statValue6, AttackStatType.Duration, displayContext);
			SetWeaponStat(ref projectileCountStat, weaponData.GetBaseProjectileCount(), statValue7, AttackStatType.ProjectileCount, displayContext);
			int idx = 0;
			WeaponStatDisplayContext displayContext2 = WeaponStatDisplayContext.OwnSource;
			for (int num = 0; num < staticStatModifiers.Length; num++)
			{
				DataModifierResolver.TryGetEquipmentModifierClassTypeByID(staticStatModifiers[num].ModifierID, out var type);
				float parameterByIndex = staticStatModifiers[num].GetParameterByIndex(idx);
				if (parameterByIndex == 0f)
				{
					continue;
				}
				Type type2 = type;
				if ((object)type2 != null)
				{
					if (type2 == typeof(DamageStatModifier))
					{
						float value = weaponData.GetBaseDamage() * parameterByIndex;
						SetWeaponStat(ref damageStat, value, statValue, AttackStatType.Damage, displayContext2);
					}
					else if (type2 == typeof(SpeedStatModifier))
					{
						float value2 = weaponData.GetBaseSpeed() * parameterByIndex;
						SetWeaponStat(ref speedStat, value2, weapon.GetAttacksPerSecond(statValue2), AttackStatType.Speed, displayContext2);
					}
					else if (type2 == typeof(CritMultiplierStatModifier))
					{
						SetWeaponStat(ref critDamageStat, parameterByIndex, statValue3, AttackStatType.CritDamage, displayContext2);
					}
					else if (type2 == typeof(CritRateStatModifier))
					{
						SetWeaponStat(ref critRateStat, parameterByIndex, statValue4, AttackStatType.CritRate, displayContext2);
					}
					else if (type2 == typeof(SizeStatModifier))
					{
						float value3 = weaponData.GetBaseSize() * parameterByIndex;
						SetWeaponStat(ref sizeStat, value3, statValue5, AttackStatType.Size, displayContext2);
					}
					else if (type2 == typeof(DurationStatModifier))
					{
						float value4 = weaponData.GetBaseDuration() * parameterByIndex;
						SetWeaponStat(ref durationStat, value4, statValue6, AttackStatType.Duration, displayContext2);
					}
					else if (type2 == typeof(ProjectileRaiseEquipmentModifier))
					{
						SetWeaponStat(ref projectileCountStat, parameterByIndex, statValue7, AttackStatType.ProjectileCount, displayContext2);
					}
				}
			}
			SetShrineStats(weapon, weaponData);
			SetPerkStats(weapon, weaponData);
			SetInvalidStatsText(weaponData);
			SetOnHitAndKillModifierIcons(equipmentLevelModifiersData);
			RefreshPanel().Forget();
		}

		private void SetWeaponStat(ref InfoPanelStatEntry statEntry, float value, float totalValue, AttackStatType statType, WeaponStatDisplayContext displayContext)
		{
			float num = 0f;
			switch (displayContext)
			{
			case WeaponStatDisplayContext.Signature:
				statEntry.TotalStatText.gameObject.SetActive(value: true);
				statEntry.TotalStatText.text = DataModifierUtils.FormatTotalValue(totalValue, statType);
				statEntry.TotalStatText.color = Color.white;
				break;
			case WeaponStatDisplayContext.Weapon:
				statEntry.ArrowText.gameObject.SetActive(value: false);
				statEntry.ChangedStatText.gameObject.SetActive(value: false);
				statEntry.TotalStatText.gameObject.SetActive(value: true);
				statEntry.TotalStatText.text = DataModifierUtils.FormatTotalValue(totalValue, statType);
				num = totalValue - value;
				if (Mathf.Approximately(num, 0f))
				{
					statEntry.TotalStatText.color = Color.white;
					statEntry.ChangedStatText.color = Color.white;
					statEntry.ArrowText.color = Color.white;
				}
				else if (num >= 0f)
				{
					statEntry.TotalStatText.color = increaseColor;
					statEntry.ChangedStatText.color = increaseColor;
					statEntry.ArrowText.color = increaseColor;
				}
				else
				{
					statEntry.TotalStatText.color = decreaseColor;
					statEntry.ChangedStatText.color = decreaseColor;
					statEntry.ArrowText.color = decreaseColor;
				}
				break;
			case WeaponStatDisplayContext.OwnSource:
				statEntry.ArrowText.gameObject.SetActive(value: true);
				statEntry.ChangedStatText.gameObject.SetActive(value: true);
				statEntry.TotalStatText.gameObject.SetActive(value: true);
				if (value >= 0f)
				{
					statEntry.ChangedStatText.color = increaseColor;
					statEntry.ArrowText.color = increaseColor;
					statEntry.TotalStatText.color = increaseColor;
				}
				else if (Mathf.Approximately(value, float.MinValue))
				{
					statEntry.ArrowText.gameObject.SetActive(value: false);
					statEntry.ChangedStatText.gameObject.SetActive(value: false);
					statEntry.TotalStatText.color = decreaseColor;
				}
				else
				{
					statEntry.ArrowText.color = decreaseColor;
					statEntry.ChangedStatText.color = decreaseColor;
					statEntry.TotalStatText.color = decreaseColor;
				}
				statEntry.ChangedStatText.text = DataModifierUtils.FormatStatChange(value, statType, appendUnits: false);
				statEntry.TotalStatText.text = DataModifierUtils.FormatTotalValue(totalValue, statType);
				break;
			case WeaponStatDisplayContext.OtherSource:
				statEntry.ArrowText.gameObject.SetActive(value: false);
				statEntry.ChangedStatText.gameObject.SetActive(value: false);
				statEntry.TotalStatText.gameObject.SetActive(value: true);
				statEntry.TotalStatText.text = DataModifierUtils.FormatTotalValue(totalValue, statType);
				num = totalValue - value;
				if (Mathf.Approximately(num, 0f))
				{
					statEntry.TotalStatText.color = deHighlightedColor;
					statEntry.ChangedStatText.color = deHighlightedColor;
					statEntry.ArrowText.color = deHighlightedColor;
				}
				else
				{
					statEntry.TotalStatText.color = modifiedColor;
					statEntry.ChangedStatText.color = modifiedColor;
					statEntry.ArrowText.color = modifiedColor;
				}
				break;
			}
		}

		private void SetShrineStats(WeaponBehaviour weaponBehaviour, WeaponData weaponData)
		{
			if (!displayShrineValues)
			{
				return;
			}
			if (PlayerHand.Instance.AllShrines.Count == 0)
			{
				ToggleSection(shrinesGroups, state: false);
				return;
			}
			ModifierFlags modifierFlags = weaponData.modifierFlags;
			if (weaponData.ID == 302)
			{
				modifierFlags &= ~ModifierFlags.Duration;
			}
			bool flag = false;
			float value;
			bool flag2 = TryGetShrineModifierValue(damageStat.ShrineModifierID, out value) && modifierFlags.HasFlag(ModifierFlags.Damage);
			float statValue = weaponBehaviour.StatsBehaviour.GetStatValue(AttackStatType.Damage, WeaponBehaviourStats.StatFormulaMultipliers.Base);
			SetShrineStat(damageStat, statValue * value, AttackStatType.Damage);
			damageStat.ShrineStatCanvasGroup.alpha = (flag2 ? 1 : 0);
			float value2;
			bool flag3 = TryGetShrineModifierValue(speedStat.ShrineModifierID, out value2) && modifierFlags.HasFlag(ModifierFlags.Speed);
			float statValue2 = weaponBehaviour.StatsBehaviour.GetStatValue(AttackStatType.Speed, WeaponBehaviourStats.StatFormulaMultipliers.Base);
			SetShrineStat(speedStat, weaponBehaviour.GetAttacksPerSecond(statValue2) * value2, AttackStatType.Speed);
			speedStat.ShrineStatCanvasGroup.alpha = (flag3 ? 1 : 0);
			float value3;
			bool flag4 = TryGetShrineModifierValue(critDamageStat.ShrineModifierID, out value3) && modifierFlags.HasFlag(ModifierFlags.CritDamage);
			SetShrineStat(critDamageStat, value3, AttackStatType.CritDamage);
			critDamageStat.ShrineStatCanvasGroup.alpha = (flag4 ? 1 : 0);
			float value4;
			bool flag5 = TryGetShrineModifierValue(critRateStat.ShrineModifierID, out value4) && modifierFlags.HasFlag(ModifierFlags.CritRate);
			SetShrineStat(critRateStat, value4, AttackStatType.CritRate);
			critRateStat.ShrineStatCanvasGroup.alpha = (flag5 ? 1 : 0);
			float value5;
			bool flag6 = TryGetShrineModifierValue(sizeStat.ShrineModifierID, out value5) && modifierFlags.HasFlag(ModifierFlags.Size);
			float statValue3 = weaponBehaviour.StatsBehaviour.GetStatValue(AttackStatType.Size, WeaponBehaviourStats.StatFormulaMultipliers.Base);
			SetShrineStat(sizeStat, statValue3 * value5, AttackStatType.Size);
			sizeStat.ShrineStatCanvasGroup.alpha = (flag6 ? 1 : 0);
			float value6;
			bool flag7 = TryGetShrineModifierValue(durationStat.ShrineModifierID, out value6) && modifierFlags.HasFlag(ModifierFlags.Duration);
			float statValue4 = weaponBehaviour.StatsBehaviour.GetStatValue(AttackStatType.Duration, WeaponBehaviourStats.StatFormulaMultipliers.Base);
			SetShrineStat(durationStat, statValue4 * value6, AttackStatType.Duration);
			durationStat.ShrineStatCanvasGroup.alpha = (flag7 ? 1 : 0);
			float value7;
			bool flag8 = TryGetShrineModifierValue(projectileCountStat.ShrineModifierID, out value7) && modifierFlags.HasFlag(ModifierFlags.ProjectileCount);
			SetShrineStat(projectileCountStat, value7, AttackStatType.ProjectileCount);
			projectileCountStat.ShrineStatCanvasGroup.alpha = (flag8 ? 1 : 0);
			flag = flag || flag2;
			flag = flag || flag3;
			flag = flag || flag4;
			flag = flag || flag5;
			flag = flag || flag6;
			flag = flag || flag7;
			flag = flag || flag8;
			ToggleSection(shrinesGroups, flag);
		}

		private bool TryGetShrineModifierValue(PerkModifierID id, out float value)
		{
			if (PlayerHand.Instance.TryGetShrineByModifierID(id, out var resultShrine))
			{
				if (resultShrine.ModifiersCount == 0)
				{
					value = 0f;
					return false;
				}
				value = resultShrine.GetAtIndexModifierParameterValue(0);
				return true;
			}
			value = 0f;
			return false;
		}

		private void SetShrineStat(InfoPanelStatEntry statEntry, float value, AttackStatType statType)
		{
			statEntry.ShrineStatText.text = DataModifierUtils.FormatStatChange(value, statType);
		}

		private void SetPerkStats(WeaponBehaviour weaponBehaviour, WeaponData weaponData)
		{
			if (!displayPerkValues)
			{
				return;
			}
			if (PlayerHand.Instance.PerksList.Count == 0)
			{
				ToggleSection(perksGroups, state: false);
				return;
			}
			ModifierFlags modifierFlags = weaponData.modifierFlags;
			if (weaponData.ID == 302)
			{
				modifierFlags &= ~ModifierFlags.Duration;
			}
			bool flag = false;
			float value;
			bool flag2 = TryGetPerkModifierValue(damageStat.PerkModifierID, out value) && modifierFlags.HasFlag(ModifierFlags.Damage);
			float statValue = weaponBehaviour.StatsBehaviour.GetStatValue(AttackStatType.Damage, WeaponBehaviourStats.StatFormulaMultipliers.Base);
			SetPerkStat(damageStat, statValue * value, AttackStatType.Damage);
			damageStat.PerkStatCanvasGroup.alpha = (flag2 ? 1 : 0);
			float value2;
			bool flag3 = TryGetPerkModifierValue(speedStat.PerkModifierID, out value2) && modifierFlags.HasFlag(ModifierFlags.Speed);
			float statValue2 = weaponBehaviour.StatsBehaviour.GetStatValue(AttackStatType.Speed, WeaponBehaviourStats.StatFormulaMultipliers.Base);
			SetPerkStat(speedStat, weaponBehaviour.GetAttacksPerSecond(statValue2) * value2, AttackStatType.Speed);
			speedStat.PerkStatCanvasGroup.alpha = (flag3 ? 1 : 0);
			float value3;
			bool flag4 = TryGetPerkModifierValue(critDamageStat.PerkModifierID, out value3) && modifierFlags.HasFlag(ModifierFlags.CritDamage);
			SetPerkStat(critDamageStat, value3, AttackStatType.CritDamage);
			critDamageStat.PerkStatCanvasGroup.alpha = (flag4 ? 1 : 0);
			float value4;
			bool flag5 = TryGetPerkModifierValue(critRateStat.PerkModifierID, out value4) && modifierFlags.HasFlag(ModifierFlags.CritRate);
			SetPerkStat(critRateStat, value4, AttackStatType.CritRate);
			critRateStat.PerkStatCanvasGroup.alpha = (flag5 ? 1 : 0);
			float value5;
			bool flag6 = TryGetPerkModifierValue(sizeStat.PerkModifierID, out value5) && modifierFlags.HasFlag(ModifierFlags.Size);
			float statValue3 = weaponBehaviour.StatsBehaviour.GetStatValue(AttackStatType.Size, WeaponBehaviourStats.StatFormulaMultipliers.Base);
			SetPerkStat(sizeStat, statValue3 * value5, AttackStatType.Size);
			sizeStat.PerkStatCanvasGroup.alpha = (flag6 ? 1 : 0);
			float value6;
			bool flag7 = TryGetPerkModifierValue(durationStat.PerkModifierID, out value6) && modifierFlags.HasFlag(ModifierFlags.Duration);
			float statValue4 = weaponBehaviour.StatsBehaviour.GetStatValue(AttackStatType.Duration, WeaponBehaviourStats.StatFormulaMultipliers.Base);
			SetPerkStat(durationStat, statValue4 * value6, AttackStatType.Duration);
			durationStat.PerkStatCanvasGroup.alpha = (flag7 ? 1 : 0);
			float value7;
			bool flag8 = TryGetPerkModifierValue(projectileCountStat.PerkModifierID, out value7) && modifierFlags.HasFlag(ModifierFlags.ProjectileCount);
			SetPerkStat(projectileCountStat, value7, AttackStatType.ProjectileCount);
			projectileCountStat.PerkStatCanvasGroup.alpha = (flag8 ? 1 : 0);
			flag = flag || flag2;
			flag = flag || flag3;
			flag = flag || flag4;
			flag = flag || flag5;
			flag = flag || flag6;
			flag = flag || flag7;
			flag = flag || flag8;
			ToggleSection(perksGroups, flag);
		}

		private bool TryGetPerkModifierValue(PerkModifierID id, out float value)
		{
			if (PlayerHand.Instance.TryGetPerkByModifierID(id, out var resultPerk))
			{
				value = resultPerk.GetAtIndexModifierParameterValue(0);
				return true;
			}
			value = 0f;
			return false;
		}

		private void SetPerkStat(InfoPanelStatEntry statEntry, float value, AttackStatType statType)
		{
			statEntry.PerkStatText.text = DataModifierUtils.FormatStatChange(value, statType, appendUnits: false);
		}

		private void ToggleSection(GameObject[] group, bool state)
		{
			for (int i = 0; i < group.Length; i++)
			{
				group[i].gameObject.SetActive(state);
			}
		}

		public void ShowEmptyText()
		{
			SetInvalidStatsText(ModifierFlags.None);
		}

		private void SetInvalidStatsText(ModifierFlags modifierFlags)
		{
			string term = naKey;
			LocalizationMediator.GetTranslation(ref term);
			if (!modifierFlags.HasFlag(ModifierFlags.Damage))
			{
				SetInvalidStatText(damageStat, ref term);
			}
			if (!modifierFlags.HasFlag(ModifierFlags.Speed))
			{
				SetInvalidStatText(speedStat, ref term);
			}
			if (!modifierFlags.HasFlag(ModifierFlags.CritDamage))
			{
				SetInvalidStatText(critDamageStat, ref term);
			}
			if (!modifierFlags.HasFlag(ModifierFlags.CritRate))
			{
				SetInvalidStatText(critRateStat, ref term);
			}
			if (!modifierFlags.HasFlag(ModifierFlags.Size))
			{
				SetInvalidStatText(sizeStat, ref term);
			}
			if (!modifierFlags.HasFlag(ModifierFlags.Duration))
			{
				SetInvalidStatText(durationStat, ref term);
			}
			if (!modifierFlags.HasFlag(ModifierFlags.ProjectileCount))
			{
				SetInvalidStatText(projectileCountStat, ref term);
			}
		}

		private void SetInvalidStatsText(WeaponData data)
		{
			string term = naKey;
			LocalizationMediator.GetTranslation(ref term);
			ModifierFlags modifierFlags = data.modifierFlags;
			if (data.ID == 302)
			{
				modifierFlags &= ~ModifierFlags.Duration;
			}
			if (!modifierFlags.HasFlag(ModifierFlags.Damage))
			{
				SetInvalidStatText(damageStat, ref term);
			}
			if (!modifierFlags.HasFlag(ModifierFlags.Speed))
			{
				SetInvalidStatText(speedStat, ref term);
			}
			if (!modifierFlags.HasFlag(ModifierFlags.CritDamage))
			{
				SetInvalidStatText(critDamageStat, ref term);
			}
			if (!modifierFlags.HasFlag(ModifierFlags.CritRate))
			{
				SetInvalidStatText(critRateStat, ref term);
			}
			if (!modifierFlags.HasFlag(ModifierFlags.Size))
			{
				SetInvalidStatText(sizeStat, ref term);
			}
			if (!modifierFlags.HasFlag(ModifierFlags.Duration))
			{
				if (data.ID == 302)
				{
					term = "???";
				}
				SetInvalidStatText(durationStat, ref term);
			}
			if (!modifierFlags.HasFlag(ModifierFlags.ProjectileCount))
			{
				SetInvalidStatText(projectileCountStat, ref term);
			}
		}

		private void SetInvalidStatText(InfoPanelStatEntry statEntry, ref string invalidString)
		{
			if (statEntry.ArrowText != null)
			{
				statEntry.ArrowText.gameObject.SetActive(value: false);
			}
			if (statEntry.ChangedStatText != null)
			{
				statEntry.ChangedStatText.gameObject.SetActive(value: false);
			}
			statEntry.TotalStatText.gameObject.SetActive(value: true);
			statEntry.TotalStatText.text = invalidString;
			statEntry.TotalStatText.color = deHighlightedColor;
		}

		private void SetOnHitAndKillModifierIcons(EquipmentLevelModifiersData levelModifiersData)
		{
			string translation = LocalizationMediator.GetTranslation(onHitStringKey);
			string translation2 = LocalizationMediator.GetTranslation(onKillStringKey);
			translation += ": {0} {1} {2 }%";
			translation2 += ": {0} {1} {2} %";
			StringBuilder stringBuilder = new StringBuilder();
			EquipmentDataModifier[] onHitModifiers = levelModifiersData.GetOnHitModifiers();
			EquipmentDataModifier[] onKillModifiers = levelModifiersData.GetOnKillModifiers();
			int num = 0;
			for (int i = 0; i < onHitModifiers.Length; i++)
			{
				num++;
				EquipmentDataModifier equipmentDataModifier = onHitModifiers[i];
				string translation3 = LocalizationMediator.GetTranslation("STT_" + ModifiersStringHelpers.GetEquipmentModifierNameLocKey(equipmentDataModifier.ModifierID));
				string arg = DataModifierUtils.FormatMultiplierToPercentage(equipmentDataModifier.GetParameterByIndex(0));
				stringBuilder.AppendFormat(translation, ModifiersStringHelpers.GetEquipmentModifierStringIcon(equipmentDataModifier.ModifierID), translation3, arg);
				if (num == 3)
				{
					stringBuilder.AppendFormat("\n");
					num = 0;
				}
				else if (i + 1 < onHitModifiers.Length)
				{
					stringBuilder.AppendFormat(" | ");
				}
				else if (i == onHitModifiers.Length - 1 && onKillModifiers.Length != 0)
				{
					stringBuilder.AppendFormat(" | ");
				}
			}
			for (int j = 0; j < onKillModifiers.Length; j++)
			{
				num++;
				EquipmentDataModifier equipmentDataModifier2 = onKillModifiers[j];
				string translation4 = LocalizationMediator.GetTranslation("STT_" + ModifiersStringHelpers.GetEquipmentModifierNameLocKey(equipmentDataModifier2.ModifierID));
				string arg2 = DataModifierUtils.FormatMultiplierToPercentage(equipmentDataModifier2.GetParameterByIndex(0));
				stringBuilder.AppendFormat(translation2, ModifiersStringHelpers.GetEquipmentModifierStringIcon(equipmentDataModifier2.ModifierID), translation4, arg2);
				if (num == 3)
				{
					stringBuilder.AppendFormat("\n");
					num = 0;
				}
				else if (j + 1 < onKillModifiers.Length)
				{
					stringBuilder.AppendFormat(" | ");
				}
			}
			effectsTxt.text = stringBuilder.ToString();
		}

		private void SetOnHitAndKillModifierIcons(WeaponBehaviour weaponBehaviour)
		{
			StringBuilder stringBuilder = new StringBuilder();
			string translation = LocalizationMediator.GetTranslation(onHitStringKey);
			string translation2 = LocalizationMediator.GetTranslation(onKillStringKey);
			translation += ": {0} {1} {2 }%";
			translation2 += ": {0} {1} {2} %";
			RuntimeEquipmentModifiers equipmentModifiers = weaponBehaviour.EquipmentModifiers;
			int num = 0;
			for (int i = 0; i < equipmentModifiers.OnHitModifiers.Count; i++)
			{
				num++;
				OnHitModifier onHitModifier = equipmentModifiers.OnHitModifiers[i];
				string translation3 = LocalizationMediator.GetTranslation("STT_" + ModifiersStringHelpers.GetEquipmentModifierNameLocKey(onHitModifier.ID));
				float num2 = onHitModifier.GetRollChance() * 100f;
				stringBuilder.AppendFormat(translation, ModifiersStringHelpers.GetEquipmentModifierStringIcon(onHitModifier.ID), translation3, num2);
				if (num == 3)
				{
					stringBuilder.AppendFormat("\n");
					num = 0;
				}
				else if (i + 1 < equipmentModifiers.OnHitModifiers.Count)
				{
					stringBuilder.AppendFormat(" | ");
				}
				else if (i == equipmentModifiers.OnHitModifiers.Count - 1 && equipmentModifiers.OnKillModifiers.Count != 0)
				{
					stringBuilder.AppendFormat(" | ");
				}
			}
			for (int j = 0; j < equipmentModifiers.OnKillModifiers.Count; j++)
			{
				num++;
				OnKillModifier onKillModifier = equipmentModifiers.OnKillModifiers[j];
				string translation4 = LocalizationMediator.GetTranslation("STT_" + ModifiersStringHelpers.GetEquipmentModifierNameLocKey(onKillModifier.ID));
				float num3 = onKillModifier.GetRollChance() * 100f;
				stringBuilder.AppendFormat(translation2, ModifiersStringHelpers.GetEquipmentModifierStringIcon(onKillModifier.ID), translation4, num3);
				if (num == 3)
				{
					stringBuilder.AppendFormat("\n");
					num = 0;
				}
				else if (j + 1 < equipmentModifiers.OnKillModifiers.Count)
				{
					stringBuilder.AppendFormat(" | ");
				}
			}
			effectsTxt.text = stringBuilder.ToString();
		}

		private async UniTaskVoid RefreshPanel()
		{
			await UniTask.NextFrame();
			if ((bool)statsContainer)
			{
				LayoutRebuilder.ForceRebuildLayoutImmediate(statsContainer);
			}
		}
	}
}
