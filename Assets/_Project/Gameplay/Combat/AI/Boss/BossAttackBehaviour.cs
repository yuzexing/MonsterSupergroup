using System;
using System.Collections;
using System.Collections.Generic;
using Animancer;
using AstralShift.HellMaiden.AI.Enemy.Boss;
using AstralShift.HellMaiden.Data;
// using AstralShift.HellMaiden.Dialogue;
using AstralShift.Helpers.Attributes;
using AstralShift.Helpers.DialogueHelpers;
using AstralShift.QTI.Helpers.Attributes;
// using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Boss
{
	public abstract class BossAttackBehaviour : MonoBehaviour
	{
		public new string name;

		public BossController bossController;

		[SerializeField]
		protected float moveSpeed;

		[Header("Weight Settings")]
		[SerializeField]
		[Range(0f, 1f)]
		protected float baseWeight = 1f;

		[ReadOnly]
		[SerializeField]
		protected float currentWeight = 1f;

		[SerializeField]
		protected bool pityChance;

		[SerializeField]
		[ConditionalHide("pityChance", true)]
		[DisplayName("Loss Factor")]
		protected float weightLossFactor = 0.3f;

		[SerializeField]
		[ConditionalHide("pityChance", true)]
		[DisplayName("Recovery Factor")]
		protected float weightRecoveryFactor = 0.1f;

		[SerializeField]
		[ConditionalHide("pityChance", true)]
		[DisplayName("Minimum Threshold")]
		protected float minWeightThreshold = 0.1f;

		[ConditionalHide("pityChance", true)]
		public float recoveryInterval = 5f;

		protected Coroutine _restoreWeightCoroutine;

		[SerializeField]
		protected DialogueLUTEntry converstation;

		[SerializeField]
		protected List<int> entryId;

		[SerializeField]
		[Range(0f, 100f)]
		protected float barkChance = 100f;

		[Header("Character Animations")]
		public bool hasWarningAnimation = true;

		public bool hasAttackingAnimation = true;

		[Header("Warning State")]
		public ClipTransition warningLeftUp;

		public ClipTransition warningLeftDown;

		public ClipTransition warningRightUp;

		public ClipTransition warningRightDown;

		private Action onWarningAnimationEnd;

		[Header("Attacking State")]
		public ClipTransition attackLeftUp;

		public ClipTransition attackLeftDown;

		public ClipTransition attackRightUp;

		public ClipTransition attackRightDown;

		private Action onAttackAnimationEnd;

		public Action onPositioningEnd;

		public Action onWarningEnd;

		public Action onAttackEnd;

		public bool shootBullets;

		public Shooter shooter;

		public float BaseWeight => baseWeight;

		public float CurrentWeight => currentWeight;

		public bool PityChance => pityChance;

		public float WeightLossFactor => weightLossFactor;

		public float WeightRecoveryFactor => weightRecoveryFactor;

		public float MinWeightThreshold => minWeightThreshold;

		public virtual void Init(BossController controller)
		{
			bossController = controller;
			RunWeightTracker();
		}

		public virtual void WarningBossAnimation(Action onEnd)
		{
			if (hasWarningAnimation)
			{
				bossController.Animator.AttackWarning(0f, 0f, onEnd);
			}
			else
			{
				onEnd?.Invoke();
			}
		}

		public virtual void AttackBossAnimation(Action onEnd)
		{
			if (hasAttackingAnimation)
			{
				bossController.Animator.Attack(0f, 0f, onEnd);
			}
			else
			{
				onEnd?.Invoke();
			}
		}

		public abstract void Positioning();

		public abstract void Warning();

		public abstract void Attack();

		public virtual void BarkWarning()
		{
			if (bossController.BarksOn && (float)UnityEngine.Random.Range(0, 100) <= barkChance && entryId.Count > 0)
			{
				int index = UnityEngine.Random.Range(0, entryId.Count);
				// Subtitle barkSubtitle = DialogueHelpers.GetBarkSubtitle(converstation.conversation, entryId[index], bossController.Actor.transform, bossController.Actor.transform);
				// AstralDialogueManager.Instance.LaunchBark(barkSubtitle);
			}
		}

		public virtual void Dispose()
		{
			base.gameObject.SetActive(value: false);
		}

		public virtual void Stop()
		{
		}

		public void ResetWeight()
		{
			SetWeight(BaseWeight);
		}

		public void SetWeight(float value)
		{
			currentWeight = value;
		}

		public void IncreaseWeight(float value)
		{
			currentWeight += value;
		}

		public void DecreaseWeight(float value)
		{
			currentWeight -= value;
		}

		public void ApplyReductionFactor()
		{
			currentWeight *= weightLossFactor;
		}

		public void ApplyPityChance()
		{
			if (PityChance)
			{
				ApplyReductionFactor();
				if (CurrentWeight < MinWeightThreshold)
				{
					SetWeight(MinWeightThreshold);
				}
			}
		}

		private void RunWeightTracker()
		{
			if (_restoreWeightCoroutine == null)
			{
				_restoreWeightCoroutine = StartCoroutine(RestoreWeightsOverTime());
			}
		}

		private IEnumerator RestoreWeightsOverTime()
		{
			if (!PityChance)
			{
				yield break;
			}
			WaitForSeconds waitInterval = new WaitForSeconds(recoveryInterval);
			while (true)
			{
				if (CurrentWeight < BaseWeight)
				{
					IncreaseWeight(WeightRecoveryFactor);
					if (CurrentWeight > BaseWeight)
					{
						ResetWeight();
					}
				}
				yield return waitInterval;
			}
		}
	}
}
