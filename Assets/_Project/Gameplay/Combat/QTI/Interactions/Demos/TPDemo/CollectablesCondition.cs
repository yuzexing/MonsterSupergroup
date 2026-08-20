using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions.Demos.TPDemo
{
	public class CollectablesCondition : Condition
	{
		[SerializeField]
		private int amount = 5;

		public override bool Verify(IInteractor interactor)
		{
			if (interactor == null)
			{
				return false;
			}
			if (!(interactor is PlayerControllerDemo playerControllerDemo))
			{
				return false;
			}
			if (!playerControllerDemo.TryGetComponent<Collector>(out var component))
			{
				return false;
			}
			return component.CollectedItems >= amount;
		}
	}
}
