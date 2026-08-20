using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using AstralShift.QTI.Triggers.Physics2D;
using DG.Tweening;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat
{
	public class UnFillCircleInteraction : Interaction
	{
		[SerializeField]
		private GameObject shrineCircleFill;

		public float duration;

		private Tweener unFillCircleTweener;

		private GameObject fillUpObject;

		private ShrineCircleInteraction shrineCircleInteraction;

		[SerializeField]
		private ShrineSFX shrineSFX;

		private void Start()
		{
			fillUpObject = shrineCircleFill.transform.GetChild(0).gameObject;
			shrineCircleInteraction = base.gameObject.GetComponent<ShrineCircleInteraction>();
			unFillCircleTweener = shrineCircleInteraction.fillUp;
		}

		public override void Interact(IInteractor interactor)
		{
			fillUpObject.transform.DOKill();
			shrineCircleInteraction.isFilling = false;
			shrineSFX.DechargingShrineSound();
			unFillCircleTweener = fillUpObject.transform.DOScale(Vector3.zero, duration).OnComplete(delegate
			{
				shrineSFX.StopSoundInstance();
				OnEnd();
			});
			base.gameObject.GetComponent<StepOn2DTrigger>().enabled = true;
			base.Interact(interactor);
		}

		public void KillCircle()
		{
			base.gameObject.GetComponent<StepOff2DTrigger>().enabled = false;
			fillUpObject.transform.DOKill();
			unFillCircleTweener = fillUpObject.transform.DOScale(Vector3.zero, duration).OnComplete(delegate
			{
				OnEnd();
			});
		}
	}
}
