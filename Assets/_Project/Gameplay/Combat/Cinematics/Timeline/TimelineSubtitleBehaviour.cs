using MonsterSupergroup.Gameplay.Options;
using UnityEngine;
using UnityEngine.Playables;

namespace AstralShift.Cinematics.Timeline
{
	public class TimelineSubtitleBehaviour : PlayableBehaviour
	{
        private string literalText, localizedKey;
        public string Text => localizedKey == null ? literalText : GameLocalization.Menu(localizedKey);

		public bool HasPositionOverride => Position != Vector2.zero;

		public Vector2 Position { get; private set; } = Vector2.zero;

		public void SetTranslatedText(string text)
		{
            localizedKey = text;
		}

		public void SetText(string text)
		{
            localizedKey = null; literalText = text;
		}

		public void SetPosition(Vector2 position)
		{
			Position = position;
		}
	}
}
