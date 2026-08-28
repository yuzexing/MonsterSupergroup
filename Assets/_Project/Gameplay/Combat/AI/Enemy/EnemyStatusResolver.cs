using System.Collections.Generic;
using AstralShift.HellMaiden.Combat;
using AstralShift.Pooling;
using MonsterSupergroup.GAS;
using UnityEngine;
using GasEnemyStatusID = MonsterSupergroup.GAS.EnemyStatusID;

namespace AstralShift.HellMaiden.AI.Enemy
{
	/// <summary>
	/// Legacy status VFX presenter. Gameplay state and timing are owned exclusively by
	/// each enemy's StatusController.
	/// </summary>
	public class EnemyStatusResolver : MonoBehaviour
	{
		public enum onHitTransform
		{
			Center = 0,
			Center1 = 1,
			Center2 = 2,
			Center3 = 3,
			Top = 4,
			Bottom = 5
		}

		private sealed class StatusVisualHandler
		{
			private static readonly int EffectAnimHash = Animator.StringToHash("EffectAnim");

			private readonly GenericPooler<GameObject> _pooler;

			private readonly onHitTransform _hitTransform;

			private readonly Dictionary<BaseEnemyController, GameObject> _visuals =
				new Dictionary<BaseEnemyController, GameObject>();

			public StatusVisualHandler(GameObject prefab, onHitTransform hitTransform)
			{
				_pooler = PoolManager.Instance.GetOrCreatePooler(prefab);
				_hitTransform = hitTransform;
			}

			public void Show(BaseEnemyController enemy, float duration)
			{
				if (enemy == null)
				{
					return;
				}

				if (!_visuals.TryGetValue(enemy, out GameObject effect) || effect == null)
				{
					effect = _pooler.GetOrCreate(activate: true);
					_visuals[enemy] = effect;
					SetEffectPosition(effect, enemy, _hitTransform);
					if (effect.TryGetComponent(out Animator animator))
					{
						animator.Play(EffectAnimHash, -1, 0f);
					}
				}

				UpdateAnimationDuration(effect, duration);
			}

			public void Hide(BaseEnemyController enemy)
			{
				if (enemy == null || !_visuals.TryGetValue(enemy, out GameObject effect))
				{
					return;
				}

				if (effect != null)
				{
					_pooler.Return(effect);
				}
				_visuals.Remove(enemy);
			}

			private static void UpdateAnimationDuration(GameObject effect, float duration)
			{
				if (effect == null || !effect.TryGetComponent(out Animator animator) ||
					animator.runtimeAnimatorController == null ||
					animator.runtimeAnimatorController.animationClips.Length == 0)
				{
					return;
				}

				float safeDuration = duration > 0f ? duration : 1f;
				animator.speed =
					animator.runtimeAnimatorController.animationClips[0].length / safeDuration;
			}
		}

		[Header("Prefabs")]
		public GameObject SlowEffectPrefab;

		public GameObject BurnEffectPrefab;

		public GameObject PoisonEffectPrefab;

		public GameObject BleedEffectPrefab;

		public GameObject WeakEffectPrefab;

		[Header("Transforms")]
		public onHitTransform slowTransform;

		public onHitTransform burnTransform;

		public onHitTransform poisonTransform;

		public onHitTransform bleedTransform;

		public onHitTransform weakTransform;

		private StatusVisualHandler _slowHandler;

		private StatusVisualHandler _burnHandler;

		private StatusVisualHandler _poisonHandler;

		private StatusVisualHandler _bleedHandler;

		private StatusVisualHandler _weakHandler;

		private bool _isInitialized;

		public static EnemyStatusResolver Instance { get; private set; }

		public void Init()
		{
			if (!Instance)
			{
				Instance = this;
			}
			InitializeHandlers();
			_isInitialized = true;
		}

		public void ShowStatus(
			BaseEnemyController enemy,
			GasEnemyStatusID id,
			float duration)
		{
			if (!_isInitialized)
			{
				return;
			}

			GetHandler(id)?.Show(enemy, duration);
		}

		public void HideStatus(BaseEnemyController enemy, GasEnemyStatusID id)
		{
			if (!_isInitialized)
			{
				return;
			}

			GetHandler(id)?.Hide(enemy);
		}

		public void HideAll(BaseEnemyController enemy)
		{
			if (!_isInitialized)
			{
				return;
			}

			_slowHandler.Hide(enemy);
			_burnHandler.Hide(enemy);
			_poisonHandler.Hide(enemy);
			_bleedHandler.Hide(enemy);
			_weakHandler.Hide(enemy);
		}

		private void InitializeHandlers()
		{
			_slowHandler = new StatusVisualHandler(SlowEffectPrefab, slowTransform);
			_burnHandler = new StatusVisualHandler(BurnEffectPrefab, burnTransform);
			_poisonHandler = new StatusVisualHandler(PoisonEffectPrefab, poisonTransform);
			_bleedHandler = new StatusVisualHandler(BleedEffectPrefab, bleedTransform);
			_weakHandler = new StatusVisualHandler(WeakEffectPrefab, weakTransform);
		}

		private StatusVisualHandler GetHandler(GasEnemyStatusID id)
		{
			switch (id)
			{
			case GasEnemyStatusID.Slow:
				return _slowHandler;
			case GasEnemyStatusID.Burn:
				return _burnHandler;
			case GasEnemyStatusID.Poison:
				return _poisonHandler;
			case GasEnemyStatusID.Bleed:
				return _bleedHandler;
			case GasEnemyStatusID.Weaken:
				return _weakHandler;
			default:
				return null;
			}
		}

		private static void SetEffectPosition(
			GameObject effect,
			BaseEnemyController enemy,
			onHitTransform hitTransform)
		{
			Transform target = enemy.Transform ? enemy.Transform : enemy.transform;
			switch (hitTransform)
			{
			case onHitTransform.Center:
				target = enemy.OnHitEffectCenterPivot ? enemy.OnHitEffectCenterPivot : target;
				break;
			case onHitTransform.Center1:
				target = enemy.OnHitEffectCenterPivot1 ? enemy.OnHitEffectCenterPivot1 : target;
				break;
			case onHitTransform.Center2:
				target = enemy.OnHitEffectCenterPivot2 ? enemy.OnHitEffectCenterPivot2 : target;
				break;
			case onHitTransform.Center3:
				target = enemy.OnHitEffectCenterPivot3 ? enemy.OnHitEffectCenterPivot3 : target;
				break;
			case onHitTransform.Top:
				target = enemy.OnHitEffectTopPivot ? enemy.OnHitEffectTopPivot : target;
				break;
			case onHitTransform.Bottom:
				target = enemy.OnHitEffectBottomPivot ? enemy.OnHitEffectBottomPivot : target;
				break;
			}

			effect.transform.position = target.position;
			effect.transform.SetParent(target);
		}

		private void OnDestroy()
		{
			if (Instance == this)
			{
				Instance = null;
			}
		}
	}
}
