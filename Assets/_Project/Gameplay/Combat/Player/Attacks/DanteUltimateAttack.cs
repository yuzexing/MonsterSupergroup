using System;
using System.Threading;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Items;
using AstralShift.Managers;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class DanteUltimateAttack : UltimateAttackWeaponBehaviour
	{
		[Header("Burn Settings")]
		public float burnStrength = 0.1f;

		public float burnDuration = 4f;

		public float burnRate = 0.5f;

		[Header("Wave Settings")]
		public GameObject DanteUltimateWavePrefab;

		private GameObject[] waves = new GameObject[2];

		private bool isAttacking;

		private CancellationTokenSource attackCts;

		private void OnDestroy()
		{
			((IPausable)this).UnSubscribe();
			if (attackCts != null)
			{
				attackCts.Cancel();
				attackCts.Dispose();
			}
			GameObject[] array = waves;
			foreach (GameObject gameObject in array)
			{
				if (gameObject != null)
				{
					UnityEngine.Object.Destroy(gameObject);
				}
			}
		}

		private void InitializeWaves()
		{
			for (int i = 0; i < waves.Length; i++)
			{
				int index = i;
				waves[i] = UnityEngine.Object.Instantiate(DanteUltimateWavePrefab, base.transform);
				waves[i].GetComponent<UltimateDamageAttack>().Init(this, null, delegate
				{
					waves[index].SetActive(value: false);
				});
				waves[i].SetActive(value: false);
			}
		}

		private async UniTask PauseAwareDelay(float duration, Action onComplete, CancellationToken token)
		{
			float elapsed = 0f;
			while (elapsed < duration && !token.IsCancellationRequested)
			{
				if (!_isPaused)
				{
					elapsed += Time.unscaledDeltaTime;
				}
				await UniTask.Yield(token);
			}
			if (!token.IsCancellationRequested)
			{
				onComplete?.Invoke();
			}
		}

		private void CompleteAttack()
		{
			isAttacking = false;
		}

		protected override void Dispose()
		{
		}

		public override void Init()
		{
			((IPausable)this).Subscribe();
			base.Init();
			OnHitBurnModifier item = new OnHitBurnModifier
			{
				parameters = new OnHitBurnModifier.Params
				{
					chance = 1f,
					damageMultiplier = burnStrength,
					numberOfHits = Mathf.RoundToInt(burnDuration / burnRate),
					hitIntervalDuration = burnRate
				}
			};
			_equipmentModifiers.OnHitModifiers.Add(item);
			UpdateModifiers(_equipmentModifiers);
			InitializeWaves();
		}

		public void SpawnWave(int waveIndex)
		{
			int num = waveIndex - 1;
			if (num >= 0 && num < waves.Length)
			{
				waves[num].SetActive(value: true);
			}
		}

		public override async void Attack()
		{
			try
			{
				if (isAttacking)
				{
					return;
				}
				isAttacking = true;
				attackCts?.Cancel();
				attackCts?.Dispose();
				attackCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
				PlayAttackSound();
				slowMoRequestId = PauseManager.Instance.StartSlowMo(immediate: true);
				slowMoTask = PauseAwareDelay(slowMoSafetyDelay, delegate
				{
					PauseManager.Instance.StopSlowMo(immediate: true, slowMoRequestId);
					LootManager.Instance.ResumeItemsPull();
				}, attackCts.Token);
				GameDirector.Instance.Player.SetInvulnerable(state: true);
				LootManager.Instance.StopAllItemsPull();
				invulTask = PauseAwareDelay(invulnerabilitySafetyDelay, delegate
				{
					GameDirector.Instance.Player.SetInvulnerable(state: false);
				}, attackCts.Token);
				animator.SetTrigger("Attack");
				KnockbackEnemies();
				try
				{
					await UniTask.WhenAll(slowMoTask, invulTask);
				}
				catch (OperationCanceledException ex)
				{
					Debug.LogError("[DanteUltimateAttack] Error during ultimate execution: " + ex.Message);
				}
			}
			catch (Exception ex2)
			{
				Debug.LogError("[DanteUltimateAttack] Error during ultimate execution: " + ex2.Message);
			}
			finally
			{
				CompleteAttack();
			}
		}
	}
}
