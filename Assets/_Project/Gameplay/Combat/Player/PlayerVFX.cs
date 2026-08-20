using System;
using AstralShift.HellMaiden.Combat;
using UnityEngine;

namespace AstralShift.HellMaiden.Player
{
	public class PlayerVFX : MonoBehaviour
	{
		public PlayerMovement player;

		[Header("Dash Settings")]
		public ParticleSystem dash;

		public ParticleSystem dashWind;

		[Header("Health Effects")]
		public ParticleSystem healthIncrease;

		[Header("Magnet Effects")]
		public ParticleSystem[] magnet;

		[Header("Teleport Effects")]
		public ParticleSystem teleport;

		private void Start()
		{
			player = GameDirector.Instance.Player;
			player.OnDashStart += TriggerDash;
			player.OnDashEnd += StopDash;
			GameEvents instance = GameEvents.Instance;
			instance.OnHealthIncrease = (Action<int>)Delegate.Combine(instance.OnHealthIncrease, new Action<int>(TriggerHealthIncrease));
			GameEvents instance2 = GameEvents.Instance;
			instance2.OnInstantMagnetStart = (Action<float>)Delegate.Combine(instance2.OnInstantMagnetStart, new Action<float>(TriggerMagnet));
		}

		private void OnDestroy()
		{
			player.OnDashStart -= TriggerDash;
			player.OnDashEnd -= StopDash;
			GameEvents instance = GameEvents.Instance;
			instance.OnHealthIncrease = (Action<int>)Delegate.Remove(instance.OnHealthIncrease, new Action<int>(TriggerHealthIncrease));
			GameEvents instance2 = GameEvents.Instance;
			instance2.OnInstantMagnetStart = (Action<float>)Delegate.Remove(instance2.OnInstantMagnetStart, new Action<float>(TriggerMagnet));
		}

		public void TriggerDash()
		{
			TriggerDashWind();
			ParticleSystem.TextureSheetAnimationModule textureSheetAnimation = dash.textureSheetAnimation;
			if (!textureSheetAnimation.enabled)
			{
				Debug.LogError("No texture sheet animation enabled");
				return;
			}
			if (player.FacingDirection.y > 0f)
			{
				textureSheetAnimation.rowIndex = 1;
				dash.transform.localScale = new Vector3(-Math.Sign(player.FacingDirection.x), 1f, 1f);
			}
			else
			{
				textureSheetAnimation.rowIndex = 0;
				dash.transform.localScale = new Vector3(Math.Sign(player.FacingDirection.x), 1f, 1f);
			}
			dash.Play();
		}

		public void TriggerDashWind()
		{
			if (!dashWind.textureSheetAnimation.enabled)
			{
				Debug.LogError("No texture sheet animation enabled");
				return;
			}
			Vector2 vector = ((player.CurrentInputDirection != Vector2.zero) ? player.CurrentInputDirection : player.FacingDirection);
			float num = Mathf.Atan2(vector.y, vector.x) * 57.29578f;
			dashWind.transform.localRotation = Quaternion.Euler(0f, 0f, num + 180f);
			dashWind.Play();
		}

		public void StopDash()
		{
			dash.Stop();
		}

		public void TriggerHealthIncrease(int value)
		{
			healthIncrease.Play();
		}

		public void TriggerMagnet(float duration)
		{
			ParticleSystem[] array = magnet;
			foreach (ParticleSystem particleSystem in array)
			{
				if (!particleSystem.isEmitting)
				{
					ParticleSystem.MainModule main = particleSystem.main;
					main.duration = duration;
				}
				particleSystem.Play();
			}
		}

		public void TriggerTeleportVFX()
		{
			teleport.Play();
		}
	}
}
