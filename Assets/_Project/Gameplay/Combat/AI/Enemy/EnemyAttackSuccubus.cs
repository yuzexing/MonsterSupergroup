using System;
using System.Collections;
using System.Linq;
using Animancer;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Player;
using AstralShift.Pooling;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.AI.Enemy
{
	public class EnemyAttackSuccubus : EnemyAttack
	{
		public string behingMapSortingLayer;

		[SerializeField]
		private ParticleSystem appearingParticles;

		public SpriteRenderer shadowSprite;

		private string baseSortingLayer;

		private string baseShadowSortingLayer;

		public Transform bulletPosition;

		public EnemyController enemyController;

		private BulletProjectile _currentBullet;

		public BulletProjectile bulletPrefab;

		protected GenericPooler<BulletProjectile> bulletPooler;

		private Collider2D collider;

		[SerializeField]
		private BoxCollider2D hurtbox;

		public float timeFadeIn = 0.8f;

		private bool isSuccubusBehindMap;

		private PlayerMovement player;

		public bool MultipleProjectiles;

		[Header("MultiProjectiles")]
		public int numberOfBarrages;

		public float bulletInterval;

		public int bulletsPerArc = 4;

		public float currentAngleOffset;

		public float spawnRadius = 2f;

		public float rotationSpeed = 90f;

		public float attackAngle = 180f;

		[Header("AudioEvents")]
		[SerializeField]
		private EventReference spawnSound;

		private float startingWarningTime;

		private float startingAttackingTime;

		private float startingRecoveryTime;

		[SerializeField]
		[Range(0f, 10f)]
		protected float warningTimeVariance;

		[SerializeField]
		[Range(0f, 10f)]
		protected float attackTimeVariance;

		[SerializeField]
		[Range(0f, 10f)]
		protected float recoveryTimeVariance;

		private float warningElapsed;

		private void Start()
		{
			player = GameDirector.Instance.Player;
			baseSortingLayer = base.controller.spriteRenderer.sortingLayerName;
			baseShadowSortingLayer = shadowSprite.sortingLayerName;
			collider = base.controller.collider;
			if ((bool)collider)
			{
				collider.enabled = true;
				hurtbox.enabled = true;
			}
			startingWarningTime = warningTime;
			startingAttackingTime = attackTime;
			startingRecoveryTime = recoveryTime;
			base.controller.SetImmunity(state: false);
		}

		public override void AttackWarningEnter()
		{
			if (isSuccubusBehindMap)
			{
				RuntimeManager.PlayOneShot(spawnSound);
			}
			base.controller.SetImmunity(state: false);
			base.controller.spriteRenderer.sortingLayerName = baseSortingLayer;
			base.controller.spriteRenderer.sortingOrder = 0;
			shadowSprite.sortingLayerName = baseShadowSortingLayer;
			collider.enabled = true;
			hurtbox.enabled = true;
			isSuccubusBehindMap = false;
			warningElapsed = 0f;
			appearingParticles.Play();
			Color color = base.controller.spriteRenderer.color;
			color.a = 0f;
			base.controller.spriteRenderer.color = color;
			warningTime = UnityEngine.Random.Range(startingWarningTime, warningTimeVariance);
			attackTime = UnityEngine.Random.Range(startingAttackingTime, attackTimeVariance);
			recoveryTime = UnityEngine.Random.Range(startingRecoveryTime, recoveryTimeVariance);
			base.AttackWarningEnter();
			if (!base.controller.isElite)
			{
				float num = Mathf.Abs(bulletPosition.transform.localPosition.x);
				bulletPosition.transform.localPosition = new Vector3((enemyController.FacingDirection.x < 0f) ? (0f - num) : num, bulletPosition.transform.localPosition.y, bulletPosition.transform.localPosition.z);
				bulletPooler = PoolManager.Instance.GetOrCreatePooler(bulletPrefab);
				BulletProjectile bulletProjectile = bulletPooler.GetOrCreate(bulletPosition);
				bulletProjectile.OnReturn = delegate
				{
					ReturnAttack(bulletProjectile);
				};
				bulletProjectile.transform.localPosition = Vector3.zero;
				bulletProjectile.fired = false;
				bulletProjectile.SetStats(base.controller.stats);
				_currentBullet = bulletProjectile;
				base.controller.enemyAnimator.AttackWarningLeftDown.Events.Clear();
				base.controller.enemyAnimator.AttackWarningLeftUp.Events.Clear();
				base.controller.enemyAnimator.AttackWarningRightDown.Events.Clear();
				base.controller.enemyAnimator.AttackWarningRightUp.Events.Clear();
				base.controller.enemyAnimator.AttackWarningLeftDown.Events.Add(new AnimancerEvent(0.5f, ActivateCurrentBullet));
				base.controller.enemyAnimator.AttackWarningLeftUp.Events.Add(new AnimancerEvent(0.5f, ActivateCurrentBullet));
				base.controller.enemyAnimator.AttackWarningRightDown.Events.Add(new AnimancerEvent(0.5f, ActivateCurrentBullet));
				base.controller.enemyAnimator.AttackWarningRightUp.Events.Add(new AnimancerEvent(0.5f, ActivateCurrentBullet));
			}
		}

		public override void AttackWarningTick()
		{
			Color color = base.controller.spriteRenderer.color;
			warningElapsed += Time.deltaTime;
			color.a = Mathf.Clamp01(warningElapsed / base.WarningTime);
			base.controller.spriteRenderer.color = color;
			base.AttackWarningTick();
		}

		public override void AttackWarningExit()
		{
			Color color = base.controller.spriteRenderer.color;
			color.a = 1f;
			base.controller.spriteRenderer.color = color;
			base.AttackWarningExit();
		}

		public void ActivateCurrentBullet()
		{
			if (!(_currentBullet == null))
			{
				_currentBullet.gameObject.SetActive(value: true);
			}
		}

		public override void AttackEnter()
		{
			base.AttackEnter();
			if (base.controller.isElite)
			{
				StartCoroutine(ShootingWRotation());
			}
			else if (!(_currentBullet == null))
			{
				_currentBullet.transform.parent = null;
				_currentBullet.Fire(player.EnemyAttackTarget.position - bulletPosition.position);
			}
		}

		private void ReturnAttack(BulletProjectile bullet)
		{
			if (!(bullet == null))
			{
				bulletPooler.Return(bullet);
				if (_currentBullet != null && bullet.GetInstanceID() == _currentBullet.GetInstanceID())
				{
					_currentBullet = null;
				}
			}
		}

		public override void CancelAttack()
		{
			if (!(_currentBullet == null) && !_currentBullet.fired)
			{
				base.controller.lastAttackTime = Time.time;
				ReturnAttack(_currentBullet);
				_currentBullet = null;
			}
		}

		public override void RecoveryEnter()
		{
			base.RecoveryEnter();
			base.controller.SetImmunity(state: true);
		}

		public override void RecoveryExit()
		{
			collider.enabled = false;
			hurtbox.enabled = false;
			base.controller.spriteRenderer.sortingLayerName = behingMapSortingLayer;
			base.controller.spriteRenderer.sortingOrder = -100;
			shadowSprite.sortingLayerName = behingMapSortingLayer;
			isSuccubusBehindMap = true;
			base.RecoveryExit();
		}

		private IEnumerator ShootingWRotation()
		{
			Vector2 direction = GameDirector.Instance.Player.EnemyAttackTarget.position - bulletPosition.position;
			WaitForSeconds waitForSeconds = new WaitForSeconds(bulletInterval);
			currentAngleOffset = (0f - attackAngle) / 2f;
			for (int i = 0; i < numberOfBarrages; i++)
			{
				for (int j = 0; j < bulletsPerArc; j++)
				{
					float num = Vector2.SignedAngle(base.transform.right, direction);
					float f = (attackAngle / (float)bulletsPerArc * (float)j + currentAngleOffset + num) * (MathF.PI / 180f);
					Vector3 vector = base.transform.position + new Vector3(Mathf.Cos(f), Mathf.Sin(f), 0f) * spawnRadius;
					bulletPooler = PoolManager.Instance.GetOrCreatePooler(bulletPrefab);
					BulletProjectile bulletProjectile = bulletPooler.GetOrCreate(bulletPosition);
					bulletProjectile.SetStats(base.controller.stats);
					bulletProjectile.OnReturn = delegate
					{
						ReturnAttack(bulletProjectile);
					};
					bulletProjectile.transform.position = vector;
					bulletProjectile.fired = false;
					bulletProjectile.gameObject.SetActive(value: true);
					bulletProjectile.transform.parent = null;
					Vector3 normalized = (vector - base.transform.position).normalized;
					bulletProjectile.Fire(normalized);
				}
				currentAngleOffset += rotationSpeed * Time.deltaTime;
				currentAngleOffset %= attackAngle;
				yield return waitForSeconds;
			}
		}

		private void OnDisable()
		{
			collider.enabled = false;
			hurtbox.enabled = false;
			base.controller.spriteRenderer.sortingLayerName = behingMapSortingLayer;
			base.controller.spriteRenderer.sortingOrder = -100;
			shadowSprite.sortingLayerName = behingMapSortingLayer;
			isSuccubusBehindMap = true;
		}

		private static string[] GetSortingLayers()
		{
			return SortingLayer.layers.Select((SortingLayer x) => x.name).ToArray();
		}
	}
}
