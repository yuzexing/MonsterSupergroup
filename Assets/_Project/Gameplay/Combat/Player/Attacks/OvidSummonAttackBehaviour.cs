using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class OvidSummonAttackBehaviour : SummonAttackBehaviour
	{
		[SerializeField]
		private float cacoonStateTime = 10f;

		private float _cacoonTimer;

		private bool _isCacoonState = true;

		private OvidSummonIdleModule _idleModule;

		protected override void ConfigureSummon(SummonAIBehaviour summon, bool isInitialSpawn)
		{
			_idleModule = summon.IdleModule as OvidSummonIdleModule;
			_idleModule.IsCacoon = _isCacoonState;
			if (isInitialSpawn)
			{
				SetInitialPosition(summon);
				SetLastAttackTime();
			}
		}

		private void SetInitialPosition(SummonAIBehaviour summon)
		{
			Vector3 currentPosition = GameDirector.Instance.Player.CurrentPosition;
			summon.Transform.position = new Vector3(currentPosition.x, currentPosition.y, summon.Transform.position.z);
			summon.Transform.rotation = Quaternion.identity;
		}

		public override float GetCooldown()
		{
			float cooldown = base.GetCooldown();
			if (!_isCacoonState)
			{
				return cooldown;
			}
			return cacoonStateTime;
		}

		public override void Update()
		{
			base.Update();
			if (_isCacoonState)
			{
				_cacoonTimer += Time.deltaTime;
				if (_cacoonTimer >= GetCooldown())
				{
					_isCacoonState = false;
					_idleModule.Exit();
				}
			}
		}
	}
}
