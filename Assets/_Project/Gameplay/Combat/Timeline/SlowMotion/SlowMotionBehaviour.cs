using AstralShift.Managers;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Playables;

namespace AstralShift.HellMaiden.Timeline.SlowMotion
{
	public class SlowMotionBehaviour : PlayableBehaviour
	{
		public float slowMotionSpeed;

		public Ease easeType = Ease.Linear;

		private bool firstFrameHappened;

		public override void ProcessFrame(Playable playable, FrameData info, object playerData)
		{
			if (Application.isPlaying && !firstFrameHappened)
			{
				PauseManager.Instance.StartSlowMo(slowMotionSpeed, (float)playable.GetDuration(), easeType);
				firstFrameHappened = true;
			}
		}
	}
}
