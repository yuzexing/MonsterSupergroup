using System.Collections;
using System.Text;
using I2.Loc;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(TMP_Text))]
public class TMPStringInterpolator : MonoBehaviour
{
	[SerializeField]
	private string firstTextLocalizationKey;

	[SerializeField]
	private string secondTextLocalizationKey;

	private string _startText;

	private string _endText;

	private string _charSet;

	private TMP_Text _tmpText;

	private StringBuilder _charSetBuilder;

	private StringBuilder _textBuilder;

	public TMP_Text TMPText
	{
		get
		{
			if (_tmpText == null)
			{
				_tmpText = GetComponent<TMP_Text>();
			}
			return _tmpText;
		}
	}

	private void UpdateCharSet()
	{
		_charSetBuilder = new StringBuilder();
		for (int i = 0; i < TMPText.font.characterTable.Count; i++)
		{
			_charSetBuilder.Append((char)TMPText.font.characterTable[i].unicode);
		}
		_charSet = _charSetBuilder.ToString();
	}

	private void OnEnable()
	{
		LocalizationManager.OnLocalizeEvent += UpdateLocalization;
		UpdateLocalization();
		ResetQuote();
		UpdateCharSet();
	}

	private void OnDisable()
	{
		LocalizationManager.OnLocalizeEvent -= UpdateLocalization;
	}

	private void UpdateLocalization()
	{
		_startText = LocalizationMediator.GetTranslation(firstTextLocalizationKey);
		_endText = LocalizationMediator.GetTranslation(secondTextLocalizationKey);
		NormalizeStrings();
	}

	private void NormalizeStrings()
	{
		if (!string.IsNullOrEmpty(_startText))
		{
			int totalLength = Mathf.Max(_startText.Length, _endText.Length);
			_startText = PadBoth(_startText, totalLength);
			_endText = PadBoth(_endText, totalLength);
		}
	}

	public void ResetQuote()
	{
		TMPText.text = _startText;
	}

	public void Interpolate(float duration, float delay = 0f)
	{
		StopAllCoroutines();
		StartCoroutine(RunUpRoutine(duration, delay));
	}

	private string PadBoth(string text, int totalLength)
	{
		int num = totalLength - text.Length;
		int num2 = num / 2;
		int count = num - num2;
		return new string(' ', num2) + text + new string(' ', count);
	}

	private IEnumerator RunUpRoutine(float duration, float delay)
	{
		int maxLength = Mathf.Max(_startText.Length, _endText.Length);
		string startText = _startText;
		string quoteB = _endText;
		int[] startIndices = new int[maxLength];
		int[] endIndices = new int[maxLength];
		for (int i = 0; i < maxLength; i++)
		{
			startIndices[i] = _charSet.IndexOf(startText[i]);
			endIndices[i] = _charSet.IndexOf(quoteB[i]);
		}
		_textBuilder = new StringBuilder(startText);
		yield return new WaitForSecondsRealtime(delay);
		float elapsedTime = 0f;
		while (elapsedTime < duration)
		{
			elapsedTime += Time.unscaledDeltaTime;
			float value = Mathf.Clamp01(elapsedTime / duration);
			for (int j = 0; j < maxLength; j++)
			{
				int num = startIndices[j];
				int num2 = endIndices[j];
				if (num == num2)
				{
					_textBuilder[j] = _charSet[num];
					continue;
				}
				float t = Mathf.Clamp01(value);
				int index = Mathf.RoundToInt(Mathf.Lerp(num, num2, t));
				_textBuilder[j] = _charSet[index];
			}
			TMPText.text = _textBuilder.ToString();
			yield return null;
		}
		Debug.Log("String Interpolator: " + elapsedTime);
		TMPText.text = quoteB;
	}
}
