using System.Collections.Generic;
using AstralShift.HellMaiden.Combat.Traps;
using AstralShift.HellMaiden.Quests;
using AstralShift.Helpers.Attributes;
using AstralShift.Managers;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat
{
	public class ProgressionManager : MonoBehaviour, IPausable
	{
		public int maxEnemies = 500;

		public AnimationCurve xpMultiplierCurve;

		public float maxXPMultiplier = 2f;

		private double maxTime = 600.0;

		[SerializeField]
		[ReadOnly]
		private int enemyCount;

		[SerializeField]
		[ReadOnly]
		private int trapCount;

		private const int MaxTrapCount = 1;

		[SerializeField]
		[ReadOnly]
		private bool _trapsDisabled;

		private bool _isPaused;

		private bool timelineIsPaused;

		public EnemyDatabase enemyDatabase;

		[Header("Debug")]
		[SerializeField]
		private float startTime;

		[SerializeField]
		private float timeScaling = 1f;

		[SerializeField]
		private ProgressionTimeline mainProgressionTimeline;

		public static ProgressionManager Instance { get; private set; }

		public static ProgressionStack ProgressionStack { get; private set; }

		public float CurrentTime => ProgressionStack.Peek().CurrentTime;

		public float StageTime => mainProgressionTimeline.CurrentTime;

		public float TimeOutTime => EndStageTime - 61f;

		public float EndStageTime => mainProgressionTimeline.EndTime;

		public float ProgressionPercent => StageTime / EndStageTime;

		public bool ReachedMaxEnemiesCount => enemyCount == maxEnemies;

		public bool ReachedMaxTrapCount
		{
			get
			{
				if (trapCount != 1)
				{
					return _trapsDisabled;
				}
				return true;
			}
		}

		public int TrapCount => trapCount;

		public List<DivinaQuestGoal> Quests => mainProgressionTimeline.Quests;

		public ProgressionTimeline MainProgressionTimeline => mainProgressionTimeline;

		private void Awake()
		{
			if (Instance == null)
			{
				Instance = this;
			}
			ProgressionStack = new ProgressionStack();
		}

		public void Init()
		{
			mainProgressionTimeline.Init();
			maxTime = ProgressionStack.Peek().GetDuration();
		}

		public void StartProgression()
		{
			((IPausable)this).Subscribe();
			ProgressionStack.Peek().StartProgression(startTime, timeScaling);
		}

		public void RegisterEnemiesCount()
		{
			enemyCount++;
			enemyCount = Mathf.Clamp(enemyCount, 0, int.MaxValue);
		}

		public void UnRegisterEnemiesCount()
		{
			enemyCount--;
			enemyCount = Mathf.Clamp(enemyCount, 0, int.MaxValue);
		}

		public void RegisterTrapCount(Trap trap)
		{
			trapCount++;
			trapCount = Mathf.Clamp(trapCount, 0, int.MaxValue);
		}

		public void UnRegisterTrapCount(Trap trap)
		{
			trapCount--;
			trapCount = Mathf.Clamp(trapCount, 0, int.MaxValue);
		}

		public void EnableTraps()
		{
			_trapsDisabled = false;
		}

		public void DisableTraps()
		{
			_trapsDisabled = true;
		}

		public void EndTraps()
		{
		}

		public float GetXPModifier()
		{
			return 1f + xpMultiplierCurve.Evaluate(StageTime / (float)maxTime) * maxXPMultiplier;
		}

		private void OnDestroy()
		{
			((IPausable)this).UnSubscribe();
			Instance = null;
		}

		public void OnPausePausables()
		{
			ProgressionStack.Peek().Pause();
		}

		public void OnResumePausables()
		{
			ProgressionStack.Peek().Resume();
		}

		public void InitQuests()
		{
			ProgressionStack.Peek().InitQuests();
		}
	}
}
