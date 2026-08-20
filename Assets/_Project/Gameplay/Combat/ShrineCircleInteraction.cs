using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using AstralShift.QTI.Triggers.Physics2D;
using DG.Tweening;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat
{
	public class ShrineCircleInteraction : Interaction
	{
		[SerializeField]
		private GameObject shrineCircleFill;

		public float duration;

		public Tweener fillUp;

		private Tweener fadeOut;

		public bool isFilling;

		private GameObject fillUpObject;

		private SpriteRenderer circleSpriteRenderer;

		[SerializeField]
		private ShrineSFX shrineSFX;

		private void Start()
		{
			fillUpObject = shrineCircleFill.transform.GetChild(0).gameObject;
			circleSpriteRenderer = shrineCircleFill.GetComponent<SpriteRenderer>();
		}

		public override void Interact(IInteractor interactor)
		{
			isFilling = true;
			shrineSFX.ChargingShrineSound();
			fillUpObject.transform.DOKill();
			fillUp = fillUpObject.transform.DOScale(Vector3.one, duration).OnComplete(delegate
			{
				if (isFilling)
				{
					fadeOut = circleSpriteRenderer.DOFade(0f, duration * 0.5f);
					OnEnd();
				}
			});
			base.gameObject.GetComponent<StepOff2DTrigger>().enabled = true;
			base.Interact(interactor);
		}
	}
}
