using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions
{
	public abstract class Condition : MonoBehaviour
	{
		public abstract bool Verify(IInteractor interactor);
	}
}
