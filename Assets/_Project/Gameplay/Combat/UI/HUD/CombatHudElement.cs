using Animancer;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.HUD
{
	public class CombatHudElement : MonoBehaviour
	{
		[SerializeField]
		private AnimancerComponent animancerComponent;

		[SerializeField]
		private ClipTransition hideAnimation;

		[SerializeField]
		private ClipTransition showAnimation;

		public void HideElement()
		{
			animancerComponent.Layers[4].Play(hideAnimation);
		}

		public void ShowElement()
		{
			animancerComponent.Layers[4].Play(showAnimation);
		}
	}
}
