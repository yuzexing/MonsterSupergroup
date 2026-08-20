using TMPro;
using UnityEngine;

namespace AstralShift.QTI.Interactions.Demos.Showcase
{
	public class QTI_Showcase_UnityEventInteractionCounter : MonoBehaviour
	{
		[SerializeField]
		private TextMeshProUGUI[] _text;

		private int _counter;

		private void Awake()
		{
			TextMeshProUGUI[] text = _text;
			for (int i = 0; i < text.Length; i++)
			{
				text[i].SetText(_counter.ToString());
			}
		}

		public void IncreaseCounter()
		{
			_counter++;
			TextMeshProUGUI[] text = _text;
			for (int i = 0; i < text.Length; i++)
			{
				text[i].SetText(_counter.ToString());
			}
		}
	}
}
