using System.Globalization;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Data.Shrines;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Menus
{
	public class PlayerStatsInformationPanel : MonoBehaviour
	{
		[SerializeField]
		private WeaponSelectionLayouts weaponSelectionLayouts;

		[Header("Weapon Info")]
		[SerializeField]
		private TextMeshProUGUI weaponNameTxt;

		[SerializeField]
		private TextMeshProUGUI ultimateNameTxt;

		[SerializeField]
		private RectTransform ultimateLayoutGroupTransform;

		[SerializeField]
		private UICardOrPerkStaticElement signatureWeaponVisuals;

		[SerializeField]
		private RectTransform weapon3DViewContainer;

		[Space]
		[Header("Max HP")]
		[SerializeField]
		private RectTransform maxHPContainer;

		[SerializeField]
		private TextMeshProUGUI maxHpText;

		[SerializeField]
		private TextMeshProUGUI maxHPShrineText;

		[SerializeField]
		private CanvasGroup maxHPShrineCanvasGroup;

		[SerializeField]
		private PerkModifierID maxHpShrineID;

		[Space]
		[Header("XP Mod")]
		[SerializeField]
		private RectTransform xpModContainer;

		[SerializeField]
		private TextMeshProUGUI xpModText;

		[SerializeField]
		private TextMeshProUGUI xpModShrineText;

		[SerializeField]
		private CanvasGroup xpModShrineCanvasGroup;

		[SerializeField]
		private PerkModifierID xpModShrineID;

		[Space]
		[Header("Mag Area")]
		[SerializeField]
		private RectTransform magAreaContainer;

		[SerializeField]
		private TextMeshProUGUI magAreaTxt;

		[SerializeField]
		private TextMeshProUGUI magAreaShrineText;

		[SerializeField]
		private CanvasGroup magAreaShrineCanvasGroup;

		[SerializeField]
		private PerkModifierID magAreaShrineID;

		[Space]
		[Header("Mov Speed")]
		[SerializeField]
		private RectTransform movSpeedContainer;

		[SerializeField]
		private TextMeshProUGUI movSpeedTxt;

		[SerializeField]
		private TextMeshProUGUI movSpeedShrineText;

		[SerializeField]
		private CanvasGroup movSpeedShrineCanvasGroup;

		[SerializeField]
		private PerkModifierID movSpeedShrineID;

		[Space]
		[Header("Localization Keys")]
		[SerializeField]
		private string maxHpKey = "STT_MaxHp";

		[SerializeField]
		private string xpModKey = "STT_XpMod";

		[SerializeField]
		private string magAreaKey = "STT_MagArea";

		[SerializeField]
		private string moveSpeedKey = "STT_MaxHp";

		private WSMWeapon3DView _weapon3DView;

		public void Show()
		{
			if (PlayerHand.Instance.TryGetEquippedSignatureWeapon(out var data))
			{
				weaponNameTxt.text = data.Data.GetTitle();
				if (data.Data.IsSignature)
				{
					ultimateNameTxt.text = data.Data.UltimateData.GetTitle();
				}
				signatureWeaponVisuals.SetCardVisuals(data);
				RefreshLayout(ultimateLayoutGroupTransform).Forget();
			}
			SetMaxHPText();
			SetXPModText();
			SetMagAreaText();
			SetMovSpeedText();
		}

		private async UniTaskVoid RefreshLayout(RectTransform rectTransform)
		{
			await UniTask.NextFrame();
			LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
		}

		private void SetMaxHPText()
		{
			RuntimeShrine resultShrine;
			bool flag = PlayerHand.Instance.TryGetShrineByModifierID(maxHpShrineID, out resultShrine) && resultShrine.ModifiersCount != 0;
			if (flag)
			{
				string text = Mathf.Ceil(resultShrine.GetAtIndexModifierParameterValue(0) * (float)GameDirector.Instance.Player.PlayerStats.baseStats.maxHP).ToString(CultureInfo.InvariantCulture);
				maxHPShrineText.text = text ?? "";
			}
			maxHPShrineCanvasGroup.alpha = (flag ? 1 : 0);
			string text2 = GameDirector.Instance.Player.PlayerStats.currentStats.maxHP.ToString(CultureInfo.InvariantCulture);
			maxHpText.text = LocalizationMediator.GetTranslation(maxHpKey) + " " + text2 + " ";
			RefreshLayout(maxHPContainer).Forget();
		}

		private void SetXPModText()
		{
			RuntimeShrine resultShrine;
			bool flag = PlayerHand.Instance.TryGetShrineByModifierID(xpModShrineID, out resultShrine) && resultShrine.ModifiersCount != 0;
			if (flag)
			{
				string text = DataModifierUtils.FormatMultiplierToPercentage(resultShrine.GetAtIndexModifierParameterValue(0));
				xpModShrineText.text = text + "%";
			}
			xpModShrineCanvasGroup.alpha = (flag ? 1 : 0);
			string text2 = DataModifierUtils.FormatMultiplierToPercentage(GameDirector.Instance.Player.PlayerStats.currentStats.xpModifier);
			xpModText.text = LocalizationMediator.GetTranslation(xpModKey) + " " + text2 + "%";
			RefreshLayout(xpModContainer).Forget();
		}

		private void SetMagAreaText()
		{
			RuntimeShrine resultShrine;
			bool flag = PlayerHand.Instance.TryGetShrineByModifierID(magAreaShrineID, out resultShrine) && resultShrine.ModifiersCount != 0;
			if (flag)
			{
				string text = DataModifierUtils.FormatMultiplierToPercentage(resultShrine.GetAtIndexModifierParameterValue(0));
				magAreaShrineText.text = text + "%";
			}
			magAreaShrineCanvasGroup.alpha = (flag ? 1 : 0);
			string text2 = DataModifierUtils.FormatAbsoluteValue(GameDirector.Instance.Player.PlayerStats.currentStats.pullArea);
			magAreaTxt.text = LocalizationMediator.GetTranslation(magAreaKey) + " " + text2;
			RefreshLayout(magAreaContainer).Forget();
		}

		private void SetMovSpeedText()
		{
			RuntimeShrine resultShrine;
			bool flag = PlayerHand.Instance.TryGetShrineByModifierID(movSpeedShrineID, out resultShrine) && resultShrine.ModifiersCount != 0;
			if (flag)
			{
				string text = DataModifierUtils.FormatMultiplierToPercentage(resultShrine.GetAtIndexModifierParameterValue(0));
				movSpeedShrineText.text = text + "%";
			}
			movSpeedShrineCanvasGroup.alpha = (flag ? 1 : 0);
			string text2 = DataModifierUtils.FormatAbsoluteValue(GameDirector.Instance.Player.PlayerStats.currentStats.moveSpeed);
			movSpeedTxt.text = LocalizationMediator.GetTranslation(moveSpeedKey) + " " + text2;
			RefreshLayout(movSpeedContainer).Forget();
		}

		public async UniTask CreateWeapon3D(WeaponData data)
		{
			if (!weaponSelectionLayouts.TryGetEntry(data, out var entry))
			{
				Debug.LogWarning("No WeaponSelectionLayoutEntry found for weapon: " + data.name);
			}
			else if ((bool)entry.Weapon3DViewPrefab)
			{
				AsyncInstantiateOperation<WSMWeapon3DView> instantiateOp = Object.InstantiateAsync(entry.Weapon3DViewPrefab, weapon3DViewContainer);
				await instantiateOp;
				_weapon3DView = instantiateOp.Result[0];
				_weapon3DView.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
				_weapon3DView.Hide();
				_weapon3DView.Initialize();
				_weapon3DView.Show();
			}
		}
	}
}
