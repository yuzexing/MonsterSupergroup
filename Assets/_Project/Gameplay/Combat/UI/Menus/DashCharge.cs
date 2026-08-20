using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class DashCharge : MonoBehaviour
{
	[SerializeField]
	private Image dashCharge;

	public void SetDashChargeState(bool state)
	{
		if (state)
		{
			dashCharge.DOFade(1f, 0.2f).SetUpdate(isIndependentUpdate: true);
		}
		else
		{
			dashCharge.DOFade(0f, 0.2f).SetUpdate(isIndependentUpdate: true);
		}
	}
}
