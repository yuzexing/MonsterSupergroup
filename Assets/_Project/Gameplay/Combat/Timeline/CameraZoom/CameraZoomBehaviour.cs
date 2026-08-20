using System;
using Com.LuisPedroFonseca.ProCamera2D;
using UnityEngine;
using UnityEngine.Playables;

namespace AstralShift.HellMaiden.Timeline.CameraZoom
{
	[Serializable]
	public class CameraZoomBehaviour : PlayableBehaviour
	{
		public float ortographicSize;

		public EaseType easeType;

		private bool firstFrameHappened;

		public override void ProcessFrame(Playable playable, FrameData info, object playerData)
		{
			if (Application.isPlaying && !firstFrameHappened)
			{
				float zoomAmount = ortographicSize - ProCamera2D.Instance.GetComponent<Camera>().orthographicSize;
				ProCamera2D.Instance.Zoom(zoomAmount, (float)playable.GetDuration(), easeType);
				firstFrameHappened = true;
			}
		}
	}
}
