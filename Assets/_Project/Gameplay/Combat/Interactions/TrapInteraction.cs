using System;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Spawners;
using AstralShift.HellMaiden.Combat.Traps;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.HellMaiden.Interactions
{
	public class TrapInteraction : Interaction
	{
		public Trap trapPrefab;

		private Trap _trapInstance;

		[SerializeField]
		private Transform trapTarget;

		public EnemySpawner enemySpawner;

		public bool stopsProgression;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			if (enemySpawner != null)
			{
				enemySpawner.Init();
			}
			if (stopsProgression)
			{
				ProgressionManager.Instance.MainProgressionTimeline.PauseAllMilestones();
			}
			if (trapPrefab != null)
			{
				SpawnTrap();
				if ((bool)enemySpawner)
				{
					enemySpawner.enemiesKilled = delegate
					{
						_trapInstance.Stop();
					};
				}
			}
			else
			{
				if (!enemySpawner)
				{
					return;
				}
				enemySpawner.enemiesKilled = delegate
				{
					if (stopsProgression)
					{
						ProgressionManager.Instance.MainProgressionTimeline.ResumeAllMilestones();
					}
					OnEnd();
				};
			}
		}

		protected void SpawnTrap()
		{
			_trapInstance = UnityEngine.Object.Instantiate(trapPrefab, trapTarget ? trapTarget : base.transform.parent, worldPositionStays: false) as BarrierTrap;
			if (trapTarget != null && _trapInstance is BarrierTrap barrierTrap)
			{
				barrierTrap.target = trapTarget;
			}
			_trapInstance.Init();
			ProgressionManager.Instance.RegisterTrapCount(_trapInstance);
			Trap trapInstance = _trapInstance;
			trapInstance.onTrapEnd = (Action)Delegate.Combine(trapInstance.onTrapEnd, (Action)delegate
			{
				ProgressionManager.Instance.UnRegisterTrapCount(_trapInstance);
				_trapInstance = null;
				if (stopsProgression)
				{
					ProgressionManager.Instance.MainProgressionTimeline.ResumeAllMilestones();
				}
				OnEnd();
			});
		}
	}
}
