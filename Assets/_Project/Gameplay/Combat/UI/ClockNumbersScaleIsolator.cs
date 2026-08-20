using UnityEngine;

namespace AstralShift.HellMaiden.UI
{
	public class ClockNumbersScaleIsolator : MonoBehaviour
	{
		private Vector3 originalScale;

		private Transform parentTransform;

		private void Start()
		{
			parentTransform = base.transform.parent;
			originalScale = base.transform.localScale;
		}

		private void LateUpdate()
		{
			if (parentTransform != null)
			{
				Vector3 localScale = parentTransform.localScale;
				base.transform.localScale = new Vector3(originalScale.x / localScale.x, originalScale.y / localScale.y, originalScale.z / localScale.z);
			}
		}
	}
}
