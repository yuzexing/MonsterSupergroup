using System.Collections.Generic;
using AstralShift.HellMaiden;
using UnityEngine;

public class DashCounter : MonoBehaviour
{
	private List<DashCharge> dashChargeSlots = new List<DashCharge>();

	[SerializeField]
	private DashCharge dashChargeSlotsPrefab;

	[SerializeField]
	private Transform dashChargesSlotParent;

	public void ResetChargeSlots()
	{
		int maxDashCharges = GameDirector.Instance.Player.PlayerStats.currentStats.maxDashCharges;
		if (dashChargeSlots.Count == maxDashCharges)
		{
			return;
		}
		if (dashChargeSlots.Count < maxDashCharges)
		{
			for (int i = dashChargeSlots.Count; i < maxDashCharges; i++)
			{
				DashCharge item = Object.Instantiate(dashChargeSlotsPrefab, dashChargesSlotParent);
				dashChargeSlots.Add(item);
			}
			return;
		}
		for (int num = maxDashCharges; num > dashChargeSlots.Count; num--)
		{
			Object.Destroy(dashChargeSlots[num]);
			dashChargeSlots.RemoveAt(num);
		}
	}

	public void LooseDashCharge(int order)
	{
		List<DashCharge> list = dashChargeSlots;
		int num = order + 1;
		list[list.Count - num].SetDashChargeState(state: false);
	}

	public void GainDashCharge(int order)
	{
		List<DashCharge> list = dashChargeSlots;
		list[list.Count - order].SetDashChargeState(state: true);
	}
}
