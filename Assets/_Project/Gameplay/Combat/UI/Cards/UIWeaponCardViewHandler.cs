using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.UI.Menus.Hand;
using Com.LuisPedroFonseca.ProCamera2D;
using FMODUnity;

namespace AstralShift.HellMaiden.UI.Cards
{
	public class UIWeaponCardViewHandler : UICardViewHandler
	{
		private RuntimeWeaponData _runtimeWeaponData;

		public RuntimeWeaponData RuntimeWeaponData => _runtimeWeaponData;

		public WeaponSlotView SlotView => base.CardSlot as WeaponSlotView;

		public override void Initialize(RuntimeCardData runtimeCardData)
		{
			base.Initialize(runtimeCardData);
			_runtimeWeaponData = base.RuntimeCardData as RuntimeWeaponData;
			base.gameObject.name = RuntimeWeaponData.Data.name + "_CardViewHandler";
		}

		public override void Equip(CardSlotView cardSlot)
		{
			base.Equip(cardSlot);
			base.CardSlot.HandSlotView.AddWeapon(this);
		}

		protected override void InitializeStateTransitions()
		{
			_stateMachine.AddTransition(_idleState, _draggingState);
			_stateMachine.AddTransition(_draggingState, _idleState);
			_stateMachine.AddTransition(_draggingState, _droppedState);
			_stateMachine.SetInitialState(_idleState);
		}

		protected override void OnEnterDragging()
		{
			if (!base.HasBeenDropped)
			{
				base.OnEnterDragging();
			}
		}

		protected override void OnExitDragging()
		{
			if (!base.HasBeenDropped)
			{
				base.OnExitDragging();
			}
		}

		public void ShowCompatVFX()
		{
			base.CardView.ShowWeaponCompatVFX(state: true);
		}

		public override void HideCompatVFX()
		{
			base.CardView?.HideCompatVFX();
			base.CardView?.HideUnCompatVFX();
		}

		public void ShowUnCompatVFX()
		{
			base.CardView.ShowUnCompatVFX(state: true);
		}

		public override void Select()
		{
			RuntimeManager.PlayOneShot(onSelectSound, ProCamera2D.Instance.GameCamera.transform.position);
		}

		public override void UnSelect()
		{
		}

		public void SetRarity(WeaponRarity rarity)
		{
			base.CardView.SetWeaponRarity(rarity);
		}
	}
}
