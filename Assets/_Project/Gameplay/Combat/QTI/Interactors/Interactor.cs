using UnityEngine;

namespace AstralShift.QTI.Interactors
{
	[AddComponentMenu("QTI/Interactor")]
	public class Interactor : MonoBehaviour, IInteractor
	{
		public Transform GetTransform()
		{
			return base.transform;
		}
	}
}
