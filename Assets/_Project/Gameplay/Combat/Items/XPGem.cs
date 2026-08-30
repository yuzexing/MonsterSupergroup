using System;
using System.Threading;
using AstralShift.HellMaiden.Combat;
using Cysharp.Threading.Tasks;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Items
{
	public class XPGem : WorldItem
	{
		[SerializeField]
		private GameObject particleParent;

		private SpriteRenderer _shadow;

		private SpriteRenderer _orb;

		private ParticleSystem[] _pSystems = new ParticleSystem[0];

		private CancellationTokenSource _disableCts;

		public float xp { get; set; }

		private void Start()
		{
			_orb = particleParent.GetComponentInChildren<SpriteRenderer>();
			_shadow = base.gameObject.GetComponentInChildren<SpriteRenderer>();
			_pSystems = particleParent.GetComponentsInChildren<ParticleSystem>();
		}

		protected override void OnEnable()
		{
			_disableCts = new CancellationTokenSource();
			ResetAllParticles();
			PlayParticles();
			RuntimeManager.PlayOneShotAttached(soundEventSpawn, base.gameObject);
			if (GameEvents.Instance.IsMagnetOn)
			{
				InstantPull();
			}
			GameEvents instance = GameEvents.Instance;
			instance.OnInstantMagnetStart = (Action<float>)Delegate.Combine(instance.OnInstantMagnetStart, new Action<float>(InstantPull));
		}

		protected override void OnDisable()
		{
			_disableCts?.Cancel();
			_disableCts?.Dispose();
			GameEvents instance = GameEvents.Instance;
			instance.OnInstantMagnetStart = (Action<float>)Delegate.Remove(instance.OnInstantMagnetStart, new Action<float>(InstantPull));
		}

		public override void Consume()
		{
			GameEvents instance = GameEvents.Instance;
			instance.OnInstantMagnetStart = (Action<float>)Delegate.Remove(instance.OnInstantMagnetStart, new Action<float>(InstantPull));
			PullCollector?.CombatantBinding?.PlayerMovement?.IncreaseXP(xp);
			Dispose();
		}

		public override void Dispose()
		{
			StopPlayerPull();
			LootManager.Instance.UnRegisterSpawnedItem(this);
			CancellationToken token = CancellationTokenSource.CreateLinkedTokenSource(_disableCts.Token, this.GetCancellationTokenOnDestroy()).Token;
			WaitForEndAndReturnToPool(token).Forget();
		}

		public void InstantPull(float duration = -1f)
		{
			LootManager.Instance.TryStartConsumePull(this);
		}

		private async UniTaskVoid WaitForEndAndReturnToPool(CancellationToken token)
		{
			try
			{
				await UniTask.WaitUntil(() => !_pSystems[0].IsAlive(withChildren: true), PlayerLoopTiming.Update, token);
				_shadow.enabled = true;
				_orb.enabled = true;
				ResetAllParticles();
				PoolManager.Instance?.xpPool?.Return(this);
			}
			catch (OperationCanceledException)
			{
			}
		}

		private void ResetAllParticles()
		{
			ParticleSystem[] pSystems = _pSystems;
			for (int i = 0; i < pSystems.Length; i++)
			{
				ParticleSystem.MainModule main = pSystems[i].main;
				main.loop = true;
			}
		}

		private void PlayParticles()
		{
			ParticleSystem[] pSystems = _pSystems;
			for (int i = 0; i < pSystems.Length; i++)
			{
				pSystems[i].Play(withChildren: true);
			}
		}

		protected override void TurnOffParticles()
		{
			_shadow.enabled = false;
			_orb.enabled = false;
			ParticleSystem[] pSystems = _pSystems;
			foreach (ParticleSystem obj in pSystems)
			{
				ParticleSystem.MainModule main = obj.main;
				main.loop = false;
				obj.Play();
			}
		}
	}
}
