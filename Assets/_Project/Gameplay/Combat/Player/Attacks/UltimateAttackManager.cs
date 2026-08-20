using Assets.Scripts.AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Scenes;
using UnityEngine;

namespace AstralShift.HellMaiden.Player.Attacks
{
	public class UltimateAttackManager : MonoBehaviour
	{
		private UltimateAttackWeaponBehaviour _ultimateAttackWeaponBehaviour;

		private UltimateAttackEvents _ultimateAttackEvents;

		public UltimateData ultimateData { get; set; }

		public bool canApplyZoom { get; set; }

		public UltimateAttackWeaponBehaviour UltimateAttackWeaponBehaviour => _ultimateAttackWeaponBehaviour;

		public UltimateAttackEvents UltimateAttackEvents => _ultimateAttackEvents;

		public void Init()
		{
			_ultimateAttackWeaponBehaviour = Object.Instantiate(ultimateData.ultimateAttackWeaponBehaviour, base.transform);
			_ultimateAttackWeaponBehaviour.ultimateData = ultimateData;
			_ultimateAttackWeaponBehaviour.CanZoom = canApplyZoom;
			_ultimateAttackWeaponBehaviour.Init();
			_ultimateAttackEvents = Object.Instantiate(ultimateData.ultimateAttackEvents, base.transform);
			Object.FindFirstObjectByType<UltimateAttackController>().Init(this);
			SceneMaster.Instance.OnSceneUnload += Destroy;
		}

		private void Destroy()
		{
			Object.Destroy(_ultimateAttackWeaponBehaviour.gameObject);
			Object.Destroy(_ultimateAttackEvents.gameObject);
		}
	}
}
