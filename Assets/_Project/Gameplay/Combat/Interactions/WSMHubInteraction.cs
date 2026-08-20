using System.Collections.Generic;
using AstralShift.HellMaiden.Characters.Effects;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.UI.Menus;
using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
// using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace AstralShift.HellMaiden.Interactions
{
	public class WSMHubInteraction : Interaction
	{
		[SerializeField]
		protected Transform worldViewsParent;

		[SerializeField]
		private WeaponSelectionLayouts weaponSelectionLayouts;

		// [VariablePopup(false)]
		// public string WeaponsUpdatedTrigger;

		public CharacterBalloonController balloonController;

		private GameObject _currentWorldView;

		private Dictionary<uint, GameObject> _worldViews;

		protected void Start()
		{
			WeaponSelectionMenuView.Instance.OnClose += GetOrCreateWeaponWorldView;
			GetOrCreateWeaponWorldView();
			// if (GameDataManager.GetGameTriggerState(WeaponsUpdatedTrigger))
			// {
			// 	balloonController.DisplayBalloon(show: true, CharacterBalloonController.BalloonType.ExclamationMark);
			// }
		}

		protected void OnDestroy()
		{
			if ((bool)WeaponSelectionMenuView.Instance)
			{
				WeaponSelectionMenuView.Instance.OnClose -= GetOrCreateWeaponWorldView;
			}
		}

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			// if (GameDataManager.GetGameTriggerState(WeaponsUpdatedTrigger))
			// {
			// 	GameDataManager.RegisterGameTrigger(WeaponsUpdatedTrigger, state: false);
			// 	balloonController.DisplayBalloon(show: false);
			// }
			WeaponSelectionMenuView.Instance.Open();
			OnEnd();
		}

		private void GetOrCreateWeaponWorldView()
		{
			if (_worldViews == null)
			{
				_worldViews = new Dictionary<uint, GameObject>();
			}
			_currentWorldView?.gameObject.SetActive(value: false);
			if (!PlayerHand.Instance.TryGetEnqueuedSignatureWeapon(out var data))
			{
				return;
			}
			if (!weaponSelectionLayouts.TryGetEntry(data, out var entry))
			{
				Debug.LogWarning("No WeaponSelectionLayoutEntry found for weapon: " + data.name);
				_currentWorldView = null;
				return;
			}
			if (!_worldViews.TryGetValue(data.ID, out var value))
			{
				if (entry.WeaponHubView == null)
				{
					_currentWorldView = null;
					return;
				}
				value = Object.Instantiate(entry.WeaponHubView, worldViewsParent.position, Quaternion.identity, worldViewsParent);
				_worldViews.Add(data.ID, value);
			}
			_currentWorldView = value;
			_currentWorldView?.gameObject.SetActive(value: true);
		}
	}
}
