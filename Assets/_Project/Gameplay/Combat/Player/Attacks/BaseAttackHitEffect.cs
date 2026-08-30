using System;
using MonsterSupergroup.GAS;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public abstract class BaseAttackHitEffect : MonoBehaviour
	{
		public AttackProgressionScaler progressionScaler;

		public BaseAttackHitBox hitbox;

		public abstract void Init(WeaponBehaviour behaviour);

		public virtual void Init(WeaponBehaviour behaviour, AttackSnapshot attack)
		{
			Init(behaviour);
		}

		public abstract void PlayOnEnable(Action onEnd);

		public abstract void PlayOnEnable(Action<IDamageable> onHit, Action onEnd);

		public abstract void Play(Action onEnd);

		public abstract void Play(Action<IDamageable> onHit, Action onEnd);

		public abstract void Stop();
	}
}
