using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.Managers;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public abstract class UltimateAttackWeaponBehaviour : WeaponBehaviour, IPausable
	{
		[Header("Animation Settings")]
		public Animator animator;

		[Header("Knockback Settings")]
		[SerializeField]
		private float knockbackRadius = 5f;

		[SerializeField]
		private LayerMask enemyLayers;

		[SerializeField]
		private KnockbackSettings knockbackSettings;

		[Header("Ultimate Delay Settings")]
		[Tooltip("Delay before ending slow motion from attack start")]
		[SerializeField]
		protected float slowMoSafetyDelay = 1f;

		[Tooltip("Delay before disabling invulnerability from attack start")]
		[SerializeField]
		protected float invulnerabilitySafetyDelay = 1f;

		protected bool _isPaused;

		protected UniTask slowMoTask;

		protected UniTask invulTask;

		protected uint slowMoRequestId;

		protected Coroutine slowMoCoroutine;

		protected Coroutine invulCoroutine;

		public UltimateData ultimateData;

		public bool CanZoom { get; set; }

		protected override void EvaluateDynamicOnDamageStatModifiers(BaseEnemyController enemy)
		{
		}

		public virtual void Init()
		{
			base.Init(uint.MaxValue, ultimateData.BaseStats);
			_equipmentModifiers = new RuntimeEquipmentModifiers();
		}

		public virtual void KnockbackEnemies()
		{
			RaycastHit2D[] array = Physics2D.CircleCastAll(base.transform.position, knockbackRadius, Vector2.up, 0f, enemyLayers);
			if (array.Length == 0)
			{
				return;
			}
			RaycastHit2D[] array2 = array;
			foreach (RaycastHit2D raycastHit2D in array2)
			{
				if (raycastHit2D.transform.TryGetComponent<BaseEnemyController>(out var component))
				{
					component.BruteforceKnockBack(base.transform.position, knockbackSettings);
				}
			}
		}

		public virtual void Interrupt()
		{
		}

		public virtual void OnPausePausables()
		{
			animator.updateMode = AnimatorUpdateMode.Normal;
			_isPaused = true;
		}

		public virtual void OnResumePausables()
		{
			animator.updateMode = AnimatorUpdateMode.UnscaledTime;
			_isPaused = false;
		}

		public virtual void OnGamePause()
		{
			animator.updateMode = AnimatorUpdateMode.Normal;
			_isPaused = true;
		}

		public virtual void OnGameResume()
		{
			animator.updateMode = AnimatorUpdateMode.UnscaledTime;
			_isPaused = false;
		}
	}
}
