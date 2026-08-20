using System.Collections.Generic;
using UnityEngine;

namespace AstralShift.FadeEffect
{
	[CreateAssetMenu(fileName = "FadeEffectData", menuName = "ScriptableObjects/FadeEffectData")]
	public class FadeEffectData : ScriptableObject
	{
		public List<FadeEffectEnum> enumFields;

		public List<BaseFadeEffect> effects;
	}
}
