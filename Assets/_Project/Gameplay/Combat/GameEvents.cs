using System;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.Helpers;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat
{
	public class GameEvents : MonoBehaviour
	{
		public static GameEvents Instance;

		public Action<int> OnHealthDecrease;

		public Action<int> OnHealthIncrease;

		public Action<int> OnHealthUpdate;

		public Action OnBeforePlayerDeath;

		public Action OnAfterPlayerDeath;

		public Action OnBossKilled;

		public Action<int> OnMaxHealthUpdate;

		public Action<float> OnIncreaseXP;

		public Action<int> OnLevelIncrease;

		public Action OnLevelUpAnimationLevelUpTrigger;

		public Action OnLevelUp;

		public Action<int> DashUsed;

		public Action<int> DashRestored;

		public Action OnMaxDashesUpdate;

		public Action ShowOfferingsScreen;

		public Action ShowPerksScreen;

		public Action<WeaponBehaviour> OnWeaponAdded;

		public Action UltimateUsed;

		public Action UltimateGained;

		public Action UpdateDashChargesAmount;

		public Action OnEliteKilled;

		public Action<float> OnInstantMagnetStart;

		public Action<float> OnVisualClockChange;

		public Action<float> OnTimeTick;

		public Action OnAreaNamePopupClosed;

		public Action<int> OnCurrencyChanged;

		public Action<float> OnCountDownStarted;

		public bool IsMagnetOn { get; private set; }

		private void Awake()
		{
			if (Instance == null)
			{
				Instance = this;
			}
			else
			{
				UnityEngine.Object.Destroy(this);
			}
		}

		public void StartMagnet(float duration)
		{
			IsMagnetOn = true;
			OnInstantMagnetStart?.Invoke(duration);
			StartCoroutine(Wait.SetTimeout(duration, StopMagnet));
		}

		public void StopMagnet()
		{
			IsMagnetOn = false;
		}
	}
}
