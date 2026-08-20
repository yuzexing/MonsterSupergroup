using System;
using System.Collections;
using System.Collections.Generic;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class Leveler : MonoBehaviour
	{
		public static Leveler Instance;

		[SerializeField]
		private AnimationCurve xpCurve;

		[SerializeField]
		private int MaxLevelUpsInARow = 5;

		[SerializeField]
		private Animator levelUpPrefab;

		[SerializeField]
		private LevelUpAnimation levelUpAnimation;

		[SerializeField]
		private float invulnerabilityDisableDelay = 1.5f;

		private CardPool _cardPool;

		private PerkPool _perkPool;

		private float _currentXP;

		private float _XPTarget;

		private const int PerkFrequency = 2;

		private const int XPPerLevelCap = 1000;

		private Coroutine _disableInvulnerabilityCoroutine;

		private bool _levelUpAnimationRunning;

		private WaitForSeconds _invulnerabilityDisableDelayYield;

		private Queue<Action> _levelUpQueue = new Queue<Action>();

		public int Level { get; private set; }

		public CardPool CardPool => _cardPool;

		public PerkPool PerkPool => _perkPool;

		public float XPPercentage => _currentXP / _XPTarget;

		public bool LevelUpAnimationRunning => _levelUpAnimationRunning;

		public int LevelUpQueueCount => _levelUpQueue.Count;

		public void Awake()
		{
			if (Instance == null)
			{
				Instance = this;
			}
			levelUpAnimation.gameObject.SetActive(value: false);
		}

		public void Init()
		{
			Level = 1;
			_levelUpQueue.Clear();
			_cardPool = new CardPool();
			_cardPool.Init();
			_perkPool = new PerkPool();
			_perkPool.Init();
			levelUpAnimation.gameObject.SetActive(value: false);
			ResetLevel();
		}

		public void IncreaseXP(float increment)
		{
			_currentXP += increment;
			while (_currentXP >= _XPTarget)
			{
				_currentXP -= _XPTarget;
				if (LevelUpQueueCount >= MaxLevelUpsInARow)
				{
					return;
				}
				IncreaseLevel();
			}
			GameEvents.Instance.OnIncreaseXP?.Invoke(_currentXP / _XPTarget);
		}

		public void ResetLevel()
		{
			if (_cardPool == null || _perkPool == null)
			{
				Init();
			}
			_currentXP = 0f;
			Level = 1;
			_XPTarget = GetLevelThreshold(Level);
			GameEvents.Instance.OnLevelIncrease?.Invoke(Level);
			GameEvents.Instance.OnLevelUp?.Invoke();
			UpdatePools();
		}

		private void UpdatePools()
		{
			_cardPool?.UpdateWeights(Level);
			_perkPool?.UpdateWeights(Level);
		}

		public void IncreaseLevel()
		{
			EnqueueLevelUp();
			int num = Level + LevelUpQueueCount;
			_XPTarget = GetLevelThreshold(num);
			GameEvents.Instance.OnIncreaseXP?.Invoke(0f);
			GameEvents.Instance.OnLevelIncrease?.Invoke(num);
			if (!_levelUpAnimationRunning)
			{
				TryRunLevelUpAnimation();
			}
		}

		private void TryRunLevelUpAnimation()
		{
			levelUpAnimation.gameObject.SetActive(value: true);
			if (!_levelUpAnimationRunning)
			{
				levelUpAnimation.StartAnimation();
			}
			_levelUpAnimationRunning = true;
			TryEnableInvulnerability();
		}

		private void EnqueueLevelUp()
		{
			_levelUpQueue.Enqueue(ProcessLevelUp);
		}

		private void ProcessLevelUp()
		{
			if (_cardPool == null || _perkPool == null)
			{
				Init();
			}
			Level++;
			GameEvents.Instance.OnLevelUp?.Invoke();
			UpdatePools();
			if (Level == 1)
			{
				Debug.LogError("LEVELER: Leveled to level 1, it should be automatic through progressionLoader so something is wrong (AR)");
			}
			else if (Level < 7 || Level % 2 == 0)
			{
				GameEvents.Instance.ShowOfferingsScreen?.Invoke();
			}
			else
			{
				GameEvents.Instance.ShowPerksScreen?.Invoke();
			}
			_levelUpAnimationRunning = false;
			_XPTarget = GetLevelThreshold(Level);
			DisableInvulnerability();
		}

		private void TryEnableInvulnerability()
		{
			if (_disableInvulnerabilityCoroutine == null)
			{
				GameDirector.Instance.Player.SetInvulnerable(state: true);
			}
		}

		private void DisableInvulnerability()
		{
			if (_disableInvulnerabilityCoroutine != null)
			{
				StopCoroutine(_disableInvulnerabilityCoroutine);
				_disableInvulnerabilityCoroutine = null;
			}
			if (_invulnerabilityDisableDelayYield == null)
			{
				_invulnerabilityDisableDelayYield = new WaitForSeconds(invulnerabilityDisableDelay);
			}
			_disableInvulnerabilityCoroutine = StartCoroutine(DisableInvulnerabilityAfterDelay());
		}

		private IEnumerator DisableInvulnerabilityAfterDelay()
		{
			yield return _invulnerabilityDisableDelayYield;
			if (_disableInvulnerabilityCoroutine != null)
			{
				GameDirector.Instance.Player.SetInvulnerable(state: false);
				_disableInvulnerabilityCoroutine = null;
			}
		}

		public void EvalLevelUp()
		{
			if (_levelUpQueue.Count > 0)
			{
				_levelUpQueue.Dequeue()();
			}
		}

		private int GetLevelThreshold(int lvl)
		{
			if (lvl > 99)
			{
				return 1000;
			}
			return (int)(xpCurve.Evaluate((float)lvl / 100f) * 1000f);
		}

		private void OnDestroy()
		{
			if (_disableInvulnerabilityCoroutine != null)
			{
				StopCoroutine(_disableInvulnerabilityCoroutine);
				_disableInvulnerabilityCoroutine = null;
			}
		}
	}
}
