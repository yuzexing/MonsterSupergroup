using System.Globalization;
using System.Text;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.GameStats;
using TMPro;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Menus
{
	public class RunStatsPlayerPanel : MonoBehaviour
	{
		[HideInInspector]
		public PlayerStatsEntry PlayerStatsEntry;

		[SerializeField]
		private TextMeshProUGUI enemiesKilledText;

		[SerializeField]
		private TextMeshProUGUI elitesKilledText;

		[SerializeField]
		private TextMeshProUGUI levelReachedTxt;

		[SerializeField]
		private TextMeshProUGUI timeSurvivedTxt;

		[SerializeField]
		private TextMeshProUGUI totalDamageDealtTxt;

		[SerializeField]
		private TextMeshProUGUI damageTakenTxt;

		[SerializeField]
		private TextMeshProUGUI damageHealedTxt;

		[SerializeField]
		private TextMeshProUGUI shrinesActivatedTxt;

		[SerializeField]
		private TextMeshProUGUI questsCompletedTxt;

		[SerializeField]
		private TextMeshProUGUI scoreTxt;

		[SerializeField]
		private int maxScoreCharacters = 6;

		[SerializeField]
		private TMP_SpriteAsset scoreNumbers;

		[SerializeField]
		private TMP_Text currencyText;

		[SerializeField]
		private TMP_Text totalCurrencyCollectedText;

		[SerializeField]
		private float playerLevelMultiplier = 200f;

		[SerializeField]
		private float enemiesKilledScoreMultiplier = 6f;

		[SerializeField]
		private float totalDamageDealtScoreMultiplier = 0.03f;

		[SerializeField]
		private float damageTakenScoreMultiplier = -2f;

		[SerializeField]
		private float damageHealedScoreMultiplier = 2f;

		[SerializeField]
		private float elitesKilledScoreMultiplier = 2500f;

		[SerializeField]
		private float shrineScoreMultiplier = 1000f;

		[SerializeField]
		private float questScoreMultiplier = 10000f;

		[SerializeField]
		private float runTimeMultiplier = 15f;

		public void RefreshValues()
		{
			if (timeSurvivedTxt != null)
			{
				timeSurvivedTxt.text = $"Time Survived: {PlayerStatsEntry.TotalTimeSurvived:0:00}";
			}
			if (totalDamageDealtTxt != null)
			{
				totalDamageDealtTxt.text = PlayerStatsEntry.totalDamageDealt.ToString(CultureInfo.InvariantCulture);
			}
			if (damageTakenTxt != null)
			{
				damageTakenTxt.text = PlayerStatsEntry.damageTaken.ToString();
			}
			if (damageHealedTxt != null)
			{
				damageHealedTxt.text = PlayerStatsEntry.healthRecovered.ToString();
			}
			if (levelReachedTxt != null)
			{
				levelReachedTxt.text = PlayerStatsEntry.maxLevelReached.ToString(CultureInfo.InvariantCulture);
			}
			if (enemiesKilledText != null)
			{
				enemiesKilledText.text = PlayerStatsEntry.totalEnemiesDefeated.ToString();
			}
			if (elitesKilledText != null)
			{
				elitesKilledText.text = PlayerStatsEntry.eliteEnemiesDefeated.ToString();
			}
			if (shrinesActivatedTxt != null)
			{
				shrinesActivatedTxt.text = PlayerStatsEntry.shrinesActivated.ToString();
			}
			if (questsCompletedTxt != null)
			{
				questsCompletedTxt.text = PlayerStatsEntry.questsCompleted.ToString(CultureInfo.InvariantCulture);
			}
			int score = CalculateScore();
			CalculateCurrency(score);
		}

		private int CalculateScore()
		{
			int num = 0;
			num += (int)((PlayerStatsEntry.maxLevelReached - 1f) * playerLevelMultiplier);
			num += (int)((float)PlayerStatsEntry.totalEnemiesDefeated * enemiesKilledScoreMultiplier);
			num += (int)((float)PlayerStatsEntry.eliteEnemiesDefeated * elitesKilledScoreMultiplier);
			num += (int)(PlayerStatsEntry.totalDamageDealt * totalDamageDealtScoreMultiplier);
			num += (int)((float)PlayerStatsEntry.damageTaken * damageTakenScoreMultiplier);
			num += (int)((float)PlayerStatsEntry.healthRecovered * damageHealedScoreMultiplier);
			num += (int)((float)PlayerStatsEntry.shrinesActivated * shrineScoreMultiplier);
			num += (int)(PlayerStatsEntry.questsCompleted * questScoreMultiplier);
			num += (int)(PlayerStatsEntry.TotalTimeSurvived * runTimeMultiplier - Mathf.Clamp(PlayerStatsEntry.TotalTimeSurvived - 720f, 0f, 10000f) * runTimeMultiplier);
			if (PlayerStatsEntry.TotalTimeSurvived > 720f)
			{
				num += 15000;
			}
			if (RunStatsTracker.Instance.RunSucessfull)
			{
				num += 35000;
			}
			int num2 = Mathf.Clamp(num, 0, 999999);
			if (num2 == 0)
			{
				scoreTxt.SetText(ModifiersStringHelpers.GetSpriteAssetFormatedString(scoreNumbers, 0));
			}
			else
			{
				StringBuilder stringBuilder = new StringBuilder();
				int num3 = 0;
				while (num2 > 0)
				{
					num3++;
					int index = num2 % 10;
					stringBuilder.Insert(0, ModifiersStringHelpers.GetSpriteAssetFormatedString(scoreNumbers, index));
					num2 /= 10;
				}
				scoreTxt.SetText(stringBuilder.ToString());
			}
			RunStatsTracker.Instance.PlayerStatsEntry.RegisterScore(RunStatsTracker.Instance.Circle, num);
			return num;
		}

		private void CalculateCurrency(int score)
		{
			float num = (float)score / 1000f;
			float currencyMultiplier = GameDirector.Instance.Player.PlayerStats.StatMultipliers.currencyMultiplier;
			float num2 = num;
			if (currencyMultiplier > 0f)
			{
				num2 = num * (currencyMultiplier + 1f);
			}
			float num3 = 0.4f;
			float num4 = 1f + (float)(RunStatsTracker.Instance.Circle - 1) * num3;
			num2 *= num4;
			num2 = Mathf.Clamp(Mathf.CeilToInt(num2), 0, 9999);
			currencyText.text = num2.ToString() ?? "";
			GameDataManager.IncreaseCurrency((int)num2);
			if (totalCurrencyCollectedText != null)
			{
				totalCurrencyCollectedText.text = GameDataManager.GetCurrency().ToString();
			}
		}
	}
}
