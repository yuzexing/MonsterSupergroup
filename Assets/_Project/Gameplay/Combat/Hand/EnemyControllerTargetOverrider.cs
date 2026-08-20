using System;
using AstralShift.HellMaiden.AI.Enemy;
using UnityEngine;

public class EnemyControllerTargetOverrider : MonoBehaviour
{
	public delegate BaseEnemyController TargetEnemyGetterDelegate(BaseEnemyController currentTarget, Vector3 position);

	public delegate Transform TargetTransformGetterDelegate(Transform currentTransform, Vector3 position);

	public BaseEnemyController targetEnemy;

	public Transform targetTransform;

	private EnemyController _enemy;

	private TargetTransformGetterDelegate _targetTransformGetter;

	private TargetEnemyGetterDelegate _targetEnemyGetter;

	private Action _targetGetter;

	public void Init(EnemyController enemy, TargetEnemyGetterDelegate targetEnemyGetter = null)
	{
		_enemy = enemy;
		_targetEnemyGetter = targetEnemyGetter;
		_targetGetter = SearchEnemyTarget;
	}

	public void Init(EnemyController enemy, TargetTransformGetterDelegate targetTransformGetter = null)
	{
		_enemy = enemy;
		_targetTransformGetter = targetTransformGetter;
		_targetGetter = SearchTransformTarget;
	}

	private void Update()
	{
		_targetGetter?.Invoke();
	}

	private void SearchEnemyTarget()
	{
		targetEnemy = _targetEnemyGetter?.Invoke(targetEnemy, _enemy.Transform.position);
		_enemy.Target = targetEnemy?.Transform;
	}

	private void SearchTransformTarget()
	{
		targetTransform = _targetTransformGetter?.Invoke(targetTransform, _enemy.Transform.position);
		_enemy.Target = targetTransform;
	}

	public void Dispose()
	{
		_targetGetter = null;
		_targetEnemyGetter = null;
		_targetTransformGetter = null;
		_enemy.Target = null;
	}
}
