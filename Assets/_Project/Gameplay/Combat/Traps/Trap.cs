using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Traps
{
	public abstract class Trap : MonoBehaviour
	{
		public Action onTrapEnd;

		public abstract void Init();

		public abstract void Stop();

		public abstract float GetShrinkDuration();
	}
}
