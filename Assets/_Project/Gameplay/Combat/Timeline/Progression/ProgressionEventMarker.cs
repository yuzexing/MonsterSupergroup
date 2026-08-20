using AstralShift.HellMaiden.Combat;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression
{
	public abstract class ProgressionEventMarker : Marker, INotification
	{
		public PropertyName id { get; }

		protected ProgressionEventMarker(PropertyName id)
		{
			this.id = id;
		}

		public abstract void ProcessEvent(ProgressionTimeline timeline);
	}
}
