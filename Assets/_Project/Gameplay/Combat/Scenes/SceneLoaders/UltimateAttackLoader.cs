using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.Initialization;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Scenes.SceneLoaders
{
	public class UltimateAttackLoader : SceneLoader
	{
		[SerializeField]
		private bool isUltimateAvailable = true;

		public bool restartsUltimateCharge = true;

		[SerializeField]
		private bool attackZoom = true;

		public override UniTask LoadAsync()
		{
			if (isUltimateAvailable)
			{
				UltimateData ultimateData;
				if (PlayerHand.Instance.TryGetEquippedSignatureWeapon(out var data) && data.Data.UltimateData != null)
				{
					ultimateData = data.Data.UltimateData;
				}
				else
				{
					ultimateData = GameDirector.Instance.runtimeDB.GetWeaponData(1u).UltimateData;
					Debug.LogWarning("Default Ultimate Attack Loaded, no signature weapon found");
				}
				UltimateAttackManager ultimateAttackManager = GameDirector.Instance.Player.ultimateAttackManager;
				ultimateAttackManager.ultimateData = ultimateData;
				ultimateAttackManager.canApplyZoom = attackZoom;
				ultimateAttackManager.Init();
			}
			if (restartsUltimateCharge)
			{
				GameDirector.Instance.Player.ResetUltimateCharge();
			}
			else if (GameDirector.Instance.Player.HasUltimateCharge)
			{
				GameEvents.Instance.UltimateGained();
			}
			return UniTask.CompletedTask;
		}
	}
}
