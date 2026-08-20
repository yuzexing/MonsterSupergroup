using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public abstract class EnemyAttackWarning : MonoBehaviour
	{
		public abstract void Show();

		public abstract void Hide();

		public abstract UniTask AwaitableHide();

		public abstract void SetWarningTime(float warningTime, float attackTime);
	}
}
