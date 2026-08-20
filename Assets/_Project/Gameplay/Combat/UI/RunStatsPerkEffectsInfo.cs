using System.Text;
using AstralShift.HellMaiden;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Perks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RunStatsPerkEffectsInfo : MonoBehaviour
{
	private static PerkTemplateLUT _perksTemplateLUT;

	[SerializeField]
	private Image perkIcon;

	[SerializeField]
	private TextMeshProUGUI perkInfoText;

	private string SttPrefix = "STP_";

	public void SetPerkEffectInfo(RuntimePerk runtimePerk, int modifierIndex)
	{
		if (_perksTemplateLUT == null)
		{
			_perksTemplateLUT = GameDirector.Instance.runtimeDB.PerkDB.VisualsTemplateLUT;
		}
		StringBuilder stringBuilder = new StringBuilder();
		PerkModifierID modifierID = runtimePerk.RuntimeData.Data.GetRarity(runtimePerk.CurrentRarity).Modifiers[modifierIndex].ModifierID;
		float atIndexModifierParameterValue = runtimePerk.GetAtIndexModifierParameterValue(modifierIndex);
		string term = SttPrefix + ModifiersStringHelpers.GetPerkModifierNameLocKey(modifierID);
		LocalizationMediator.GetTranslation(ref term);
		stringBuilder.AppendFormat("{0}:{1}", term, $"{atIndexModifierParameterValue * 100f:0.##}");
		perkIcon.sprite = _perksTemplateLUT.GetModifierIconSprite(modifierID);
		perkInfoText.text = stringBuilder.ToString();
	}
}
