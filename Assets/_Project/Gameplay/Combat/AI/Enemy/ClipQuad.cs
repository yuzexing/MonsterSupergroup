using System;
using Animancer;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	[Serializable]
	public class ClipQuad
	{
		[Header("Attack Sequence")]
		[Header("Warning Animation")]
		public ClipTransition attackWarningLeftUp;

		public ClipTransition attackWarningLeftDown;

		public ClipTransition attackWarningRightUp;

		public ClipTransition attackWarningRightDown;

		[Header("Attack Animation")]
		public ClipTransition attackLeftUp;

		public ClipTransition attackLeftDown;

		public ClipTransition attackRightUp;

		public ClipTransition attackRightDown;
	}
}
