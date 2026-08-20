using System;
using AstralShift.HellMaiden.MapGeneration;
using AstralShift.HellMaiden.UI.HUD;
using AstralShift.QTI.Helpers.Attributes;
using DG.Tweening;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Items
{
	public abstract class WorldItem : MonoBehaviour, IConsumable
	{
		[Header("Minimap Icon")]
		[SerializeField]
		private Sprite minimapIconSprite;

		[SerializeField]
		private float minimapIconSize = 1f;

		[SerializeField]
		private MinimapIcon.PingMode pingMode;

		[Header("Animation MoveBack Options")]
		[SerializeField]
		public AnimationCurve baseBackCurve;

		[SerializeField]
		public float baseMoveBackAmount = 1f;

		[SerializeField]
		public float baseMoveBackTime = 0.3f;

		[Header("Animation Jump Options")]
		[SerializeField]
		public AnimationCurve baseJumpCurve;

		[SerializeField]
		public float baseJumpTime = 0.5f;

		[SerializeField]
		public float baseJumpForce = 1f;

		[SerializeField]
		public int baseJumps = 1;

		[Header("Sounds")]
		[SerializeField]
		protected EventReference soundEventSpawn;

		[SerializeField]
		protected EventReference soundEventPull;

		[SerializeField]
		protected EventReference soundEventConsume;

		[Header("Despawn Conditions")]
		[SerializeField]
		protected bool despawnsWithDistance;

		[ConditionalHide("despawnsWithDistance", true)]
		[SerializeField]
		protected float despawnDistance = 6f;

		protected MinimapIcon _minimapIcon;

		protected WorldItemsPool.WorldItemPool _worldItemPool;

		private Tween jumpBack;

		private Tween jumpToPlayer;

		private Sequence _pullTween;

		protected bool _isPaused;

		[SerializeField]
		private MinimapUIManager.MinimapIconType iconType;

		public event Action OnStartPlayerPull;

		protected virtual void OnEnable()
		{
			if (despawnsWithDistance)
			{
				MapGenerator.OnTilesMoved += CheckDespawn;
			}
			CreateMinimapIcon();
		}

		protected virtual void OnDisable()
		{
			if (despawnsWithDistance)
			{
				MapGenerator.OnTilesMoved -= CheckDespawn;
			}
			ReleaseMinimapIcon();
		}

		public virtual void Consume()
		{
			if (!soundEventConsume.IsNull)
			{
				RuntimeManager.PlayOneShotAttached(soundEventConsume, base.gameObject);
			}
			Dispose();
		}

		public void Show()
		{
			base.gameObject.SetActive(value: true);
		}

		public virtual bool StartPlayerPull()
		{
			return StartPlayerPull(baseMoveBackAmount, baseMoveBackTime, baseBackCurve, baseJumpForce, baseJumpTime, baseJumpCurve, delegate
			{
				LootManager.Instance.EnqueueConsume(this);
			}, TurnOffParticles, baseJumps);
		}

		protected bool StartPlayerPull(float moveBackAmmount, float moveBackDuration, AnimationCurve moveBackCurve, float jumpForce, float jumpDuration, AnimationCurve jumpCurve, Action onEnd, Action turnOffParticles, int numbOfJumps = 1)
		{
			if (!base.gameObject.activeSelf)
			{
				return false;
			}
			if (!soundEventPull.IsNull)
			{
				RuntimeManager.PlayOneShotAttached(soundEventPull, base.gameObject);
			}
			this.OnStartPlayerPull?.Invoke();
			StopPlayerPull();
			PlayerPull(moveBackAmmount, moveBackDuration, moveBackCurve, jumpDuration, jumpForce, jumpCurve, onEnd, turnOffParticles);
			return true;
		}

		public void StopPlayerPull()
		{
			if (_pullTween != null)
			{
				_pullTween.Kill();
				_pullTween = null;
			}
		}

		public void ResumePlayerPull()
		{
			if (_isPaused && _pullTween != null)
			{
				_pullTween.Play();
				_isPaused = false;
			}
		}

		public virtual void PausePlayerPull()
		{
			if (!_isPaused && _pullTween != null)
			{
				_pullTween.Pause();
				_isPaused = true;
			}
		}

		private void PlayerPull(float moveBackAmmount, float moveBackDuration, AnimationCurve moveBackCurve, float jumpDuration, float jumpForce, AnimationCurve jumpCurve, Action onEnd, Action turnOffParticles)
		{
			Transform playerTransform = GameDirector.Instance.Player.transform;
			Vector3 endValue = -(Vector2)(playerTransform.position - base.transform.position).normalized * moveBackAmmount;
			endValue += base.transform.position;
			float progress = 0f;
			Vector3 jumpStartPosition = Vector3.zero;
			_pullTween = DOTween.Sequence(this);
			_pullTween.Append(base.transform.DOMove(endValue, moveBackDuration).SetEase(moveBackCurve));
			_pullTween.AppendCallback(delegate
			{
				turnOffParticles?.Invoke();
				jumpStartPosition = base.transform.position;
			});
			_pullTween.Append(DOTween.To(() => progress, delegate(float x)
			{
				progress = x;
			}, 1f, jumpDuration).SetEase(jumpCurve).OnUpdate(delegate
			{
				float num = Mathf.Sin(progress * MathF.PI * 1f) * jumpForce;
				base.transform.position = Vector3.Lerp(jumpStartPosition, playerTransform.position, progress) + Vector3.up * num;
			}));
			_pullTween.OnComplete(delegate
			{
				onEnd?.Invoke();
				_pullTween = null;
			});
			_pullTween.Restart();
		}

		protected void CreateMinimapIcon()
		{
			MinimapUIManager.Instance.RequestMinimapIcon(base.transform, minimapIconSprite, minimapIconSize, pingMode, iconType, HandleIconCreated);
			void HandleIconCreated(MinimapIcon icon)
			{
				_minimapIcon = icon;
			}
		}

		private void ReleaseMinimapIcon()
		{
			if ((bool)_minimapIcon)
			{
				_minimapIcon.Release();
			}
		}

		protected virtual void TurnOffParticles()
		{
		}

		public virtual void Dispose()
		{
			_worldItemPool.itemAmount--;
			StopPlayerPull();
			LootManager.Instance.UnRegisterSpawnedItem(this);
			_worldItemPool.Return(this);
		}

		private void CheckDespawn(TileGenerator[] tileGenerators, MapGenerator mapGenerator)
		{
			if (MapGenerator.GetDistanceToPlayerInTiles(base.transform.position) >= despawnDistance)
			{
				Dispose();
			}
		}
	}
}
