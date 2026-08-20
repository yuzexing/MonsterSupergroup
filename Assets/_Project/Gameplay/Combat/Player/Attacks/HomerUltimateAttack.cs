using System;
using System.Collections.Generic;
using System.Threading;
using Animancer;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Items;
using AstralShift.HellMaiden.Timeline;
using AstralShift.Helpers;
using AstralShift.Managers;
using AstralShift.Pooling;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class HomerUltimateAttack : UltimateAttackWeaponBehaviour
	{
		public AnimancerComponent animancer;

		public ClipTransition circleSpinAnim;

		public GameObject pushEnemyCircle;

		[SerializeField]
		private GameObject circleGameObject;

		[SerializeField]
		private float pushCircleTimer = 5f;

		[Header("Attack Settings")]
		public ProjectileAttack projectilePrefab;

		[SerializeField]
		protected float baseSpeed = 3f;

		[SerializeField]
		protected float spawnRadius = 0.5f;

		[SerializeField]
		protected int hitCount = 1;

		[SerializeField]
		protected int projectileCount = 1;

		[SerializeField]
		protected bool rotateToMovement = true;

		[Header("Zoom Settings")]
		public float zoomOutSize = 25f;

		public float zoomInDuration = 1f;

		public CustomAnimationCurve zoomInCurve;

		public float zoomOutDuration = 2f;

		public CustomAnimationCurve zoomOutCurve;

		public TimelineEffects timelineEffects;

		private GenericPooler<ProjectileAttack> _pooler;

		private List<ProjectileAttack> _attacks = new List<ProjectileAttack>();

		private CancellationTokenSource _cts;

		[SerializeField]
		protected Vector3 positionOffset = Vector3.zero;

		private async UniTask PauseAwareDelay(float duration, Action onComplete, CancellationToken cancellationToken)
		{
			float elapsed = 0f;
			while (elapsed < duration && !cancellationToken.IsCancellationRequested)
			{
				if (!_isPaused)
				{
					elapsed += Time.unscaledDeltaTime;
				}
				await UniTask.Yield(cancellationToken);
			}
			onComplete?.Invoke();
		}

		private void CompleteAttack()
		{
			if (base.CanZoom)
			{
				ProCamera2DHelpers.ResetZoom(zoomOutDuration, zoomOutCurve);
			}
			timelineEffects.ShakeCamera(2);
			StopPushEnemies();
			_cts?.Dispose();
			_cts = null;
		}

		private void StopPushEnemies()
		{
			pushEnemyCircle.SetActive(value: false);
		}

		private ProjectileAttack GetOrCreateAttack()
		{
			if (_pooler == null)
			{
				_pooler = PoolManager.Instance.GetOrCreatePooler(projectilePrefab);
			}
			ProjectileAttack attack = _pooler.GetOrCreate(null);
			if (!_attacks.Contains(attack))
			{
				_attacks.Add(attack);
			}
			Action onEnd = delegate
			{
				attack.GetComponent<HomerTornadoPullBehavior>().ReleaseAllEnemies();
				_attacks.Remove(attack);
				_pooler.Return(attack);
			};
			attack.Init(this, null, onEnd);
			return attack;
		}

		protected override void Dispose()
		{
		}

		public override void Init()
		{
			base.Init();
			((IPausable)this).Subscribe();
		}

		public override async void Attack()
		{
			try
			{
				PlayAttackSound();
				_cts?.Cancel();
				_cts?.Dispose();
				_cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
				CancellationToken ct = _cts.Token;
				pushEnemyCircle.SetActive(value: true);
				circleGameObject.SetActive(value: true);
				AnimancerState state = animancer.Layers[0].Play(circleSpinAnim, 0f, FadeMode.FromStart);
				state.Events(this).OnEnd = delegate
				{
					circleGameObject.SetActive(value: false);
					state.Events(this).OnEnd = null;
				};
				if (base.CanZoom)
				{
					ProCamera2DHelpers.Zoom(zoomOutSize, zoomInDuration, zoomInCurve);
				}
				slowMoRequestId = PauseManager.Instance.StartSlowMo(immediate: true);
				slowMoTask = PauseAwareDelay(slowMoSafetyDelay, delegate
				{
					if (!ct.IsCancellationRequested)
					{
						PauseManager.Instance.StopSlowMo(immediate: true, slowMoRequestId);
						LootManager.Instance.ResumeItemsPull();
					}
				}, ct);
				GameDirector.Instance.Player.SetInvulnerable(state: true);
				LootManager.Instance.StopAllItemsPull();
				invulTask = PauseAwareDelay(invulnerabilitySafetyDelay, delegate
				{
					if (!ct.IsCancellationRequested)
					{
						GameDirector.Instance.Player.SetInvulnerable(state: false);
					}
				}, ct);
				await UniTask.WhenAll(slowMoTask, invulTask);
				if (!ct.IsCancellationRequested)
				{
					CompleteAttack();
				}
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
				if (PauseManager.Instance != null)
				{
					PauseManager.Instance.StopSlowMo(immediate: true, slowMoRequestId);
				}
				if (GameDirector.Instance?.Player != null)
				{
					GameDirector.Instance.Player.SetInvulnerable(state: false);
				}
				if (LootManager.Instance != null)
				{
					LootManager.Instance.ResumeItemsPull();
				}
				if (circleGameObject != null)
				{
					circleGameObject.SetActive(value: false);
				}
			}
		}

		public void SpawnTornados()
		{
			if (_cts != null && !_cts.IsCancellationRequested)
			{
				for (int i = 0; i < projectileCount; i++)
				{
					ProjectileAttack orCreateAttack = GetOrCreateAttack();
					orCreateAttack.gameObject.SetActive(value: true);
					Vector3 vector = Quaternion.AngleAxis(Vector2.SignedAngle(GameDirector.Instance.Player.attackDirection, Vector2.right) + 360f / (float)projectileCount * (float)i, -Vector3.forward) * Vector3.right;
					orCreateAttack.transform.position = base.transform.position + positionOffset + vector.normalized * spawnRadius;
					orCreateAttack.Attack(vector.normalized, baseSpeed, hitCount, rotateToMovement);
				}
				Invoke("StopPushEnemies", pushCircleTimer);
			}
		}

		public void StopSlowMo()
		{
		}

		public void EndUltimateEffects()
		{
		}

		public override void OnPausePausables()
		{
			if (_attacks != null && _attacks.Count > 0)
			{
				foreach (ProjectileAttack attack in _attacks)
				{
					attack.PlayEndAnimation();
				}
			}
			CompleteAttack();
			base.OnPausePausables();
		}

		private void OnDisable()
		{
			_cts?.Cancel();
			_cts?.Dispose();
			((IPausable)this).UnSubscribe();
		}

		private void OnDestroy()
		{
			_cts?.Cancel();
			_cts?.Dispose();
			((IPausable)this).UnSubscribe();
		}
	}
}
