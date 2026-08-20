using TMPro;
using UnityEngine;

public class LoadingScreenBackground : MonoBehaviour
{
	[SerializeField]
	private TextMeshProUGUI loreText;

	public void SetLoreText(string text)
	{
		if (loreText != null)
		{
			loreText.text = text;
		}
	}
}
