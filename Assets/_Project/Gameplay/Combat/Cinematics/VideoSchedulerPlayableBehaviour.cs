using System;
using System.Collections.Generic;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.Cinematics
{
	public sealed class VideoSchedulerPlayableBehaviour : PlayableBehaviour
	{
		private IEnumerable<TimelineClip> m_Clips;

		private PlayableDirector m_Director;

		internal PlayableDirector director
		{
			get
			{
				return m_Director;
			}
			set
			{
				m_Director = value;
			}
		}

		internal IEnumerable<TimelineClip> clips
		{
			get
			{
				return m_Clips;
			}
			set
			{
				m_Clips = value;
			}
		}

		public override void ProcessFrame(Playable playable, FrameData info, object playerData)
		{
			if (m_Clips == null)
			{
				return;
			}
			int num = 0;
			foreach (TimelineClip clip in m_Clips)
			{
				VideoPlayableBehaviour behaviour = ((ScriptPlayable<VideoPlayableBehaviour>)playable.GetInput(num)).GetBehaviour();
				if (behaviour != null)
				{
					double num2 = Math.Max(0.0, behaviour.preloadTime);
					if (m_Director.time >= clip.start + clip.duration || m_Director.time <= clip.start - num2)
					{
						behaviour.StopVideo();
					}
					else if (m_Director.time > clip.start - num2)
					{
						behaviour.PrepareVideo();
					}
				}
				num++;
			}
		}
	}
}
