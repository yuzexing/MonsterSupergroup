using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.UI.Menus;
using AstralShift.HellMaiden.UI.Menus.Hand;
using Com.LuisPedroFonseca.ProCamera2D;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Cards
{
	public class UIEquipmentCardViewHandler : UICardViewHandler, IEquipmentCardVisual, ICardVisual
	{
		private RuntimeEquipmentData _runtimeEquipmentData;

		public RuntimeEquipmentData RuntimeEquipmentData => _runtimeEquipmentData;

		public EquipmentSlotView SlotView => base.CardSlot as EquipmentSlotView;

		public override void Initialize(RuntimeCardData runtimeCardData)
		{
			base.Initialize(runtimeCardData);
			_runtimeEquipmentData = base.RuntimeCardData as RuntimeEquipmentData;
		}

		public override void Equip(CardSlotView cardSlot)
		{
			base.Equip(cardSlot);
			base.CardSlot.HandSlotView.AddEquipment(this);
		}

		public void UnEquip(bool sort = true)
		{
			if ((bool)base.CardSlot)
			{
				base.CardSlot.HandSlotView.RemoveEquipment(this);
				base.CardSlot.HandSlotView.HideAllCompatVFX -= HideCompatVFX;
				base.CardSlot.ClearCardSlot();
				if (sort)
				{
					base.CardSlot.HandSlotView?.SortEquipmentSlots();
				}
				base.CardSlot = null;
			}
		}

		protected override void InitializeStateTransitions()
		{
			_stateMachine.AddTransition(_idleState, _droppedState);
			base.InitializeStateTransitions();
		}

		public void ShowMergeCompatVFX()
		{
			base.CardView.ShowMergeCompatVFX(state: true);
		}

		public override void HideCompatVFX()
		{
			base.CardView?.HideCompatVFX();
		}

		public override void Select()
		{
			if (RuntimeEquipmentData != null)
			{
				RuntimeManager.PlayOneShot(onSelectSound, ProCamera2D.Instance.GameCamera.transform.position);
				UICardPickMenuView.Instance?.HandView.RunCompatibilityCheck(this);
			}
		}

		public override void UnSelect()
		{
			UICardPickMenuView.Instance?.HandView.HideCompatibilityVFX();
		}

		public void SetRarity(uint levelIndex)
		{
			base.CardView.SetEquipmentRarity(levelIndex);
		}

		public override void SetLevelIcon(Sprite sprite)
		{
			base.CardView.Card3DProxy.Card.SetLevelIcon(sprite);
		}

		public override void SetEffectIcon(Sprite sprite)
		{
			base.CardView.Card3DProxy.Card.SetEffectIcon(sprite);
		}
	}
}
