using UnityEngine;
using UnityEngine.Playables;

namespace AstralShift.Cinematics.Timeline
{
	public class TimelineSubtitleBehaviour : PlayableBehaviour
	{
		public string Text { get; private set; }

		public bool HasPositionOverride => Position != Vector2.zero;

		public Vector2 Position { get; private set; } = Vector2.zero;

		public void SetTranslatedText(string text)
		{
			LocalizationMediator.GetTranslation(ref text);
			SetText(text);
		}

		public void SetText(string text)
		{
			Text = text;
		}

		public void SetPosition(Vector2 position)
		{
			Position = position;
		}
	}
}
