using System;
using Com.LuisPedroFonseca.ProCamera2D;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.CameraZoom
{
	[Serializable]
	public class CameraZoomClip : PlayableAsset, ITimelineClipAsset
	{
		private CameraZoomBehaviour template = new CameraZoomBehaviour();

		[SerializeField]
		private float ortographicSize;

		[SerializeField]
		private EaseType easeType = EaseType.Linear;

		public ClipCaps clipCaps => ClipCaps.None;

		public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
		{
			ScriptPlayable<CameraZoomBehaviour> scriptPlayable = ScriptPlayable<CameraZoomBehaviour>.Create(graph, template);
			CameraZoomBehaviour behaviour = scriptPlayable.GetBehaviour();
			behaviour.ortographicSize = ortographicSize;
			behaviour.easeType = easeType;
			return scriptPlayable;
		}
	}
}
