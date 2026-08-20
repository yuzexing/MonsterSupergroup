using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.Timeline
{
	public class FadeEffectTrackMixer : PlayableBehaviour
	{
		private Color m_DefaultColor = Color.black;

		private Image m_TrackBinding;

		private bool m_FirstFrameHappened;

		private PlayableDirector director;

		public override void OnPlayableCreate(Playable playable)
		{
			director = playable.GetGraph().GetResolver() as PlayableDirector;
		}

		public override void ProcessFrame(Playable playable, FrameData info, object playerData)
		{
		}

		public override void OnPlayableDestroy(Playable playable)
		{
		}
	}
}
