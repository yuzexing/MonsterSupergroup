using System;
using System.Collections.Generic;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Helpers;
using UnityEngine;

namespace AstralShift.HellMaiden.Player
{
	public class AutoAim : MonoBehaviour
	{
		[Tooltip("Lower is faster")]
		public int updateRate = 7;

		[Tooltip("Snap immediately to any enemy inside this distance")]
		public float minAttackDistance = 1.2f;

		public Action OnTargetUpdate;

		private int _updateCount;

		private readonly List<BaseEnemyController> _targets = new List<BaseEnemyController>(32);

		private BaseEnemyController _target;

		[Tooltip("Required distance gain (m) for a candidate to replace the current target")]
		[SerializeField]
		private float switchDistanceAdvantage = 0.25f;

		[Tooltip("Minimum time between target switches")]
		[SerializeField]
		private float minSwitchCooldown = 0.05f;

		private float _lastSwitchTime = -999f;

		[Tooltip("Circle trigger used by this AutoAim (same object)")]
		[SerializeField]
		private CircleCollider2D trigger;

		[Tooltip("Extra margin added to trigger radius when validating entries via OnTriggerEnter")]
		[SerializeField]
		private float enterDistanceTolerance = 0.05f;

		[Tooltip("Extra margin allowed before removing a target as 'out of range'")]
		[SerializeField]
		private float exitDistanceTolerance = 0.1f;

		private void OnEnable()
		{
			_updateCount = updateRate;
			_targets.Clear();
			_target = null;
		}

		private void OnTriggerEnter2D(Collider2D collision)
		{
			BaseEnemyController componentInParent = collision.GetComponentInParent<BaseEnemyController>();
			if (!(componentInParent == null) && IsCandidateValid(componentInParent, checkRange: true, useEnterTolerance: true) && !_targets.Contains(componentInParent))
			{
				_targets.Add(componentInParent);
			}
		}

		private void OnTriggerExit2D(Collider2D collision)
		{
			BaseEnemyController componentInParent = collision.GetComponentInParent<BaseEnemyController>();
			if (!(componentInParent == null))
			{
				_targets.Remove(componentInParent);
				if (_target == componentInParent)
				{
					_target = null;
				}
			}
		}

		private void FixedUpdate()
		{
			if (_updateCount != updateRate)
			{
				_updateCount++;
				return;
			}
			_updateCount = 0;
			RemoveTargetList();
			if (_targets.Count == 0)
			{
				BaseEnemyController target = null;
				float num = float.PositiveInfinity;
				BaseEnemyController[] allEnemiesOnScreen = AIHelpers.GetAllEnemiesOnScreen();
				foreach (BaseEnemyController baseEnemyController in allEnemiesOnScreen)
				{
					if (IsCandidateValid(baseEnemyController, checkRange: false))
					{
						float distanceToTarget = baseEnemyController.DistanceToTarget;
						if (distanceToTarget < num)
						{
							num = distanceToTarget;
							target = baseEnemyController;
						}
					}
				}
				_target = target;
				OnTargetUpdate?.Invoke();
			}
			else
			{
				SelectNearestWithHysteresis(_targets);
				OnTargetUpdate?.Invoke();
			}
		}

		private void RemoveTargetList()
		{
			for (int num = _targets.Count - 1; num >= 0; num--)
			{
				BaseEnemyController enemy = _targets[num];
				if (!IsCandidateValid(enemy, checkRange: true, useEnterTolerance: false, useExitTolerance: true))
				{
					_targets.RemoveAt(num);
				}
			}
			if (_target != null && !_targets.Contains(_target))
			{
				_target = null;
			}
		}

		private void SelectNearestWithHysteresis(List<BaseEnemyController> candidates)
		{
			BaseEnemyController baseEnemyController = null;
			float num = float.PositiveInfinity;
			BaseEnemyController baseEnemyController2 = null;
			float num2 = float.PositiveInfinity;
			for (int i = 0; i < candidates.Count; i++)
			{
				BaseEnemyController baseEnemyController3 = candidates[i];
				if (IsCandidateValid(baseEnemyController3, checkRange: false))
				{
					float distanceToTarget = baseEnemyController3.DistanceToTarget;
					if (distanceToTarget < num)
					{
						num = distanceToTarget;
						baseEnemyController = baseEnemyController3;
					}
					if (distanceToTarget < minAttackDistance && distanceToTarget < num2)
					{
						num2 = distanceToTarget;
						baseEnemyController2 = baseEnemyController3;
					}
				}
			}
			if (baseEnemyController == null)
			{
				_target = null;
			}
			else if (baseEnemyController2 != null)
			{
				if (_target != baseEnemyController2)
				{
					ForceSwitch(baseEnemyController2);
				}
			}
			else if (_target == null)
			{
				_target = baseEnemyController;
				_lastSwitchTime = Time.time;
			}
			else if (Time.time >= _lastSwitchTime + minSwitchCooldown)
			{
				float distanceToTarget2 = _target.DistanceToTarget;
				if (num + switchDistanceAdvantage < distanceToTarget2)
				{
					ForceSwitch(baseEnemyController);
				}
			}
		}

		private void ForceSwitch(BaseEnemyController newTarget)
		{
			if (!(newTarget == _target))
			{
				_target = newTarget;
				_lastSwitchTime = Time.time;
			}
		}

		public BaseEnemyController GetTarget()
		{
			if (_target != null && !_target.gameObject.activeSelf)
			{
				_target = null;
			}
			return _target;
		}

		private bool IsCandidateValid(BaseEnemyController enemy, bool checkRange, bool useEnterTolerance = false, bool useExitTolerance = false)
		{
			if (enemy == null)
			{
				return false;
			}
			if (!enemy.gameObject.activeInHierarchy)
			{
				return false;
			}
			Collider2D componentInChildren = enemy.GetComponentInChildren<Collider2D>();
			if (componentInChildren != null && !componentInChildren.enabled)
			{
				return false;
			}
			if (checkRange && trigger != null)
			{
				float triggerWorldRadius = GetTriggerWorldRadius();
				float b = (useEnterTolerance ? enterDistanceTolerance : (useExitTolerance ? exitDistanceTolerance : 0f));
				float num = triggerWorldRadius + Mathf.Max(0f, b);
				if (enemy.DistanceToTarget > num)
				{
					return false;
				}
			}
			return true;
		}

		private float GetTriggerWorldRadius()
		{
			if (trigger == null)
			{
				return 0f;
			}
			float num = Mathf.Max(trigger.transform.lossyScale.x, trigger.transform.lossyScale.y);
			return trigger.radius * num;
		}

		internal void Enable()
		{
			base.gameObject.SetActive(value: true);
		}

		internal void Disable()
		{
			base.gameObject.SetActive(value: false);
		}
	}
}
