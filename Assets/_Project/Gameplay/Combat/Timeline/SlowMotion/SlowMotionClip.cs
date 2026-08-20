using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.SlowMotion
{
	[Serializable]
	public class SlowMotionClip : PlayableAsset, ITimelineClipAsset
	{
		public float slowMotionSpeed;

		private SlowMotionBehaviour template = new SlowMotionBehaviour();

		[SerializeField]
		private Ease easeType = Ease.Linear;

		public ClipCaps clipCaps => ClipCaps.None;

		public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
		{
			ScriptPlayable<SlowMotionBehaviour> scriptPlayable = ScriptPlayable<SlowMotionBehaviour>.Create(graph, template);
			SlowMotionBehaviour behaviour = scriptPlayable.GetBehaviour();
			behaviour.slowMotionSpeed = slowMotionSpeed;
			behaviour.easeType = easeType;
			return scriptPlayable;
		}
	}
}
