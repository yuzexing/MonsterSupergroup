using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Cards
{
	public interface ICardVisual
	{
		void SetIllustrationMainLayer(Sprite sprite, Material material = null);

		UniTask SetIllustrationAdditionalLayer(int index, Sprite sprite, Material material = null);

		void ClearIllustrationAdditionalLayers();

		UniTask SetForegroundLayer(Sprite sprite, Material material = null);

		void SetFrameLayer(Sprite bg, Sprite frame, Material bgMaterial = null, Material frameMaterial = null);

		void SetTitleText(string text, Color color);

		void SetTextBoxBackground(Sprite sprite);

		void SetDescriptionText(string text, Color color);

		void SetQuoteText(string text, Color color, Color separatorColor);

		void SetLevelIcon(Sprite sprite);

		void SetEffectIcon(Sprite sprite);
	}
}
