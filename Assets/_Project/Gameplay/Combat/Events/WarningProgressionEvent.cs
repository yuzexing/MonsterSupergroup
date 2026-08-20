using AstralShift.HellMaiden.CameraFX;

namespace AstralShift.HellMaiden.Combat.Events
{
	public class WarningProgressionEvent : ProgressionEvent
	{
		private float expiredTime;

		public override float startTime { get; set; }

		public override float endTime { get; set; }

		public override bool progressionPaused { get; set; }

		public override bool hasEnded { get; set; }

		public float ttl { get; set; }

		public override void Init()
		{
			CameraEffects.Instance.warningEffect.Enable();
		}

		public override void ProgressUpdate()
		{
			expiredTime = ProgressionManager.Instance.CurrentTime - startTime;
			if (expiredTime >= ttl)
			{
				hasEnded = true;
			}
		}

		public override void End()
		{
			CameraEffects.Instance.warningEffect.Disable();
		}
	}
}
