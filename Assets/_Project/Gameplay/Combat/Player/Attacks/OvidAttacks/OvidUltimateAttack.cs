using System;
using System.Collections.Generic;
using Animancer;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Data.Shrines;
using AstralShift.HellMaiden.Items;
using AstralShift.HellMaiden.Timeline;
using AstralShift.Helpers;
using AstralShift.Managers;
using AstralShift.Pooling;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks.OvidAttacks
{
	public class OvidUltimateAttack : UltimateAttackWeaponBehaviour
	{
		[Header("Heal Settings")]
		[SerializeField]
		private int healTimes;

		[Header("Animation")]
		public AnimancerComponent animancer;

		public ClipTransition start;

		public ClipTransition loop;

		public ClipTransition end;

		public StringAsset ultimateEventName;

		public TimelineEffects timelineEffects;

		[Header("Zoom Settings")]
		public float zoomOutSize = 25f;

		public float zoomInDuration = 1f;

		public CustomAnimationCurve zoomInCurve;

		public float zoomOutDuration = 2f;

		public CustomAnimationCurve zoomOutCurve;

		[Header("Attack Settings")]
		public ProjectileAttack projectilePrefab;

		[SerializeField]
		protected float baseSpeed = 3f;

		[SerializeField]
		protected float spawnRadius = 0.5f;

		[SerializeField]
		protected int hitCount = 1;

		[SerializeField]
		protected bool rotateToMovement = true;

		[SerializeField]
		private int projectileCount = 8;

		[SerializeField]
		private Vector3 positionOffset = Vector3.zero;

		private float healthIncreaseInterval;

		private float currentTime;

		private int calledTimes;

		private Vector2 initialPosition;

		private GenericPooler<ProjectileAttack> _pooler;

		private List<ProjectileAttack> _attacks = new List<ProjectileAttack>();

		private bool _timerActive;

		private NoMovementPlayerController _controller;

		private bool _interrupted;

		[Header("Shoot Sound")]
		[SerializeField]
		protected EventReference shootSound;

		[Header("After Ultimate Buff")]
		public ShrineData shrineData;

		private void Update()
		{
			if (!_timerActive || calledTimes >= healTimes)
			{
				return;
			}
			currentTime += Time.deltaTime;
			if (currentTime >= healthIncreaseInterval * (float)(calledTimes + 1))
			{
				Shoot();
				if (calledTimes == healTimes - 1)
				{
					EndUltimate();
				}
				calledTimes++;
				player.IncreaseHealth(player.PlayerStats.MaxHP / healTimes);
			}
		}

		private void EndUltimate()
		{
			NoMovementPlayerController controller = _controller;
			controller.onDeactivate = (Action)Delegate.Remove(controller.onDeactivate, new Action(Interrupt));
			_timerActive = false;
			calledTimes = 0;
			currentTime = 0f;
			AnimancerState currentState = animancer.Layers[0].Play(end, 0f, FadeMode.FromStart);
			end.Events.SetCallback(ultimateEventName, delegate
			{
				if (!_interrupted)
				{
					ControllerManager.Instance.YieldGameController();
				}
				player.body.constraints = RigidbodyConstraints2D.FreezeRotation;
				player.spriteRenderer.sortingLayerName = "Props";
				player.spriteRenderer.sortingOrder = 0;
				PlayerHand.Instance.ActivateWeapons();
				PlayerHand.Instance.ApplyShrine(shrineData);
			});
			currentState.Events(this).OnEnd = delegate
			{
				player.SetInvulnerable(state: false);
				LootManager.Instance.ResumeItemsPull();
				currentState.Events(this).OnEnd = null;
				if (base.CanZoom)
				{
					ProCamera2DHelpers.ResetZoom(zoomOutDuration, zoomOutCurve);
				}
				base.transform.SetParent(player.transform, worldPositionStays: false);
				base.transform.localPosition = initialPosition;
			};
		}

		private void Shoot()
		{
			if (!shootSound.IsNull)
			{
				RuntimeManager.PlayOneShot(shootSound, base.transform.position);
			}
			for (int i = 0; i < projectileCount; i++)
			{
				ProjectileAttack orCreateAttack = GetOrCreateAttack();
				orCreateAttack.gameObject.SetActive(value: true);
				Vector3 vector = Quaternion.AngleAxis(Vector2.SignedAngle(player.attackDirection, Vector2.right) + 360f / (float)projectileCount * (float)i, -Vector3.forward) * Vector3.right;
				orCreateAttack.transform.position = base.transform.position + positionOffset + vector.normalized * spawnRadius;
				orCreateAttack.Attack(vector.normalized, baseSpeed, hitCount, rotateToMovement);
			}
		}

		protected ProjectileAttack GetOrCreateAttack()
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
				_attacks.Remove(attack);
				_pooler.Return(attack);
			};
			attack.Init(this, null, onEnd);
			return attack;
		}

		private void CleanupPlayerState()
		{
			if (player == null)
			{
				return;
			}
			player.SetInvulnerable(state: false);
			player.body.constraints = RigidbodyConstraints2D.FreezeRotation;
			player.spriteRenderer.sortingLayerName = "Props";
			player.spriteRenderer.sortingOrder = 0;
			PlayerHand.Instance?.ActivateWeapons();
			LootManager.Instance?.ResumeItemsPull();
			if (base.CanZoom)
			{
				ProCamera2DHelpers.ResetZoom(zoomOutDuration, zoomOutCurve);
			}
			if (base.transform.parent != player.transform)
			{
				base.transform.SetParent(player.transform, worldPositionStays: false);
				base.transform.localPosition = initialPosition;
			}
			if (_controller != null)
			{
				NoMovementPlayerController controller = _controller;
				controller.onDeactivate = (Action)Delegate.Remove(controller.onDeactivate, new Action(Interrupt));
				ControllerManager instance = ControllerManager.Instance;
				if ((object)instance != null && instance.Stack.Contains(_controller))
				{
					ControllerManager.Instance.YieldGameController();
				}
			}
			_timerActive = false;
			calledTimes = 0;
			currentTime = 0f;
			ProjectileAttack[] array = _attacks.ToArray();
			foreach (ProjectileAttack projectileAttack in array)
			{
				if (projectileAttack != null && projectileAttack.gameObject != null)
				{
					_pooler?.Return(projectileAttack);
				}
			}
			_attacks.Clear();
		}

		protected override void Dispose()
		{
		}

		public override void Init()
		{
			base.Init();
			initialPosition = base.transform.localPosition;
			((IPausable)this).Subscribe();
		}

		public override void Attack()
		{
			BeginCombatAttack();
			PlayAttackSound();
			base.transform.SetParent(null);
			player.SetInvulnerable(state: true);
			LootManager.Instance.StopAllItemsPull();
			PlayerHand.Instance.DeactivateWeapons();
			player.body.constraints = RigidbodyConstraints2D.FreezeAll;
			player.spriteRenderer.sortingLayerName = "Default";
			player.spriteRenderer.sortingOrder = -1000;
			_controller = ControllerManager.Instance.OverrideGameController<NoMovementPlayerController>();
			timelineEffects.ShakeCamera(2);
			healthIncreaseInterval = ultimateData.BaseStats.duration / (float)healTimes;
			if (base.CanZoom)
			{
				ProCamera2DHelpers.Zoom(zoomOutSize, zoomInDuration, zoomInCurve);
			}
			AnimancerState currentState = animancer.Layers[0].Play(start, 0f, FadeMode.FromStart);
			currentState.Events(this).OnEnd = delegate
			{
				_timerActive = true;
				animancer.Layers[0].Play(loop, 0f, FadeMode.FromStart);
				currentState.Events(this).OnEnd = null;
			};
			NoMovementPlayerController controller = _controller;
			controller.onDeactivate = (Action)Delegate.Combine(controller.onDeactivate, new Action(Interrupt));
			_interrupted = false;
		}

		public override void OnPausePausables()
		{
			animancer.UpdateMode = AnimatorUpdateMode.Normal;
			base.OnPausePausables();
		}

		public override void OnResumePausables()
		{
			animancer.UpdateMode = AnimatorUpdateMode.UnscaledTime;
			base.OnResumePausables();
		}

		public override void OnGamePause()
		{
			animancer.UpdateMode = AnimatorUpdateMode.Normal;
			base.OnGamePause();
		}

		public override void OnGameResume()
		{
			animancer.UpdateMode = AnimatorUpdateMode.UnscaledTime;
			base.OnGameResume();
		}

		public override void Interrupt()
		{
			if (!ControllerManager.Instance.Stack.Contains(_controller) && !_interrupted)
			{
				_interrupted = true;
				EndUltimate();
			}
		}

		private void OnDisable()
		{
			if (_timerActive || player != null)
			{
				CleanupPlayerState();
			}
		}

		private void OnDestroy()
		{
			((IPausable)this).UnSubscribe();
			CleanupPlayerState();
		}
	}
}
