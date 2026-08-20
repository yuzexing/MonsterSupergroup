using UnityEngine;

namespace AstralShift.Helpers
{
	public struct ActionBuffer
	{
		private float _lastInputTime;

		private readonly float _duration;

		public bool IsValid => Time.time - _lastInputTime <= _duration;

		public ActionBuffer(float duration)
		{
			_lastInputTime = float.NegativeInfinity;
			_duration = duration;
		}

		public void Record()
		{
			_lastInputTime = Time.time;
		}

		public void Consume()
		{
			_lastInputTime = float.NegativeInfinity;
		}
	}
}
