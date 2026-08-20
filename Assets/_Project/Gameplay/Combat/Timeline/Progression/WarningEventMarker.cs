using System.ComponentModel;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Events;
using UnityEngine;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline.Progression
{
	[CustomStyle("WarningEventMarkerStyle")]
	[DisplayName("AstralShift/Progression/Warning Event")]
	public class WarningEventMarker : ProgressionEventMarker
	{
		[SerializeField]
		private float ttl = 5f;

		public WarningEventMarker(PropertyName id)
			: base(id)
		{
		}

		public override void ProcessEvent(ProgressionTimeline timeline)
		{
			WarningProgressionEvent warningProgressionEvent = new WarningProgressionEvent();
			warningProgressionEvent.ttl = ttl;
			timeline.CreateMilestone(warningProgressionEvent, this);
		}
	}
}
