using AstralShift.HellMaiden.UI.Cards;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Cards
{
	public abstract class CardTemplateVisualData : ScriptableObject
	{
		[SerializeField]
		protected UICardViewHandler uiCardViewTemplate;

		[SerializeField]
		protected UICard3DView uiCard3DViewTemplate;

		public UICardViewHandler UICardViewTemplate => uiCardViewTemplate;

		public UICard3DView UICard3DViewTemplate => uiCard3DViewTemplate;
	}
}
