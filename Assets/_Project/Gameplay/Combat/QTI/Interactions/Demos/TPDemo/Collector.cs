using UnityEngine;

namespace AstralShift.QTI.Interactions.Demos.TPDemo
{
	public class Collector : MonoBehaviour
	{
		private int collectedItems;

		public int CollectedItems => collectedItems;

		public void Collect()
		{
			collectedItems++;
		}

		private void OnGUI()
		{
			float value = Screen.width * Screen.height / 2073600;
			value = Mathf.Clamp(value, 1f, 1.75f);
			GUI.skin.label.fontSize = (int)(32f * value);
			GUI.Label(new Rect((float)Screen.width - 170f * value, 180f * value, 250f * value, 45f * value), "Gems: " + collectedItems);
		}
	}
}
