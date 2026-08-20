using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.UI
{
	public class SliderStepper : MonoBehaviour
	{
		public float step;

		private Slider slider;

		private void Awake()
		{
			slider = GetComponent<Slider>();
		}

		public void Increment()
		{
			Debug.Log("Incrementing slider by " + step);
			if (slider.value != slider.maxValue)
			{
				slider.value += step;
			}
		}

		public void Decrement()
		{
			Debug.Log("Decrementing slider by " + step);
			if (slider.value != slider.minValue)
			{
				slider.value -= step;
			}
		}
	}
}
