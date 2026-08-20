using System;
using AstralShift.HellMaiden.Combat.Traps;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Spawners
{
	public class BarrierTrapSpawner : TrapSpawner
	{
		private BarrierTrap _currentTrap;

		public override void Init()
		{
			startingChance = 100f / (base.Duration - trap.GetShrinkDuration());
			pittyChance = startingChance;
			currentChance = startingChance;
			TrySpawn();
		}

		public override void ProgressUpdate()
		{
			if (!(_currentTrap != null))
			{
				base.ProgressUpdate();
			}
		}

		protected override void SpawnTrap()
		{
			_currentTrap = UnityEngine.Object.Instantiate(trap, base.transform, worldPositionStays: false) as BarrierTrap;
			_currentTrap.Init();
			BarrierTrap currentTrap = _currentTrap;
			currentTrap.onTrapEnd = (Action)Delegate.Combine(currentTrap.onTrapEnd, (Action)delegate
			{
				ProgressionManager.Instance.UnRegisterTrapCount(_currentTrap);
				DisposeTrap();
				base.hasEnded = true;
			});
			ProgressionManager.Instance.RegisterTrapCount(_currentTrap);
		}

		protected virtual void DisposeTrap()
		{
			UnityEngine.Object.Destroy(_currentTrap.gameObject);
			_currentTrap = null;
		}

		public override void End()
		{
			currentChance = startingChance;
			if ((bool)_currentTrap)
			{
				_currentTrap.Stop();
			}
		}

		public override void PauseProgressable()
		{
			base.progressionPaused = true;
			if ((bool)_currentTrap)
			{
				_currentTrap.Stop();
			}
		}
	}
}
