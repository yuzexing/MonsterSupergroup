using System;
using Rewired;
using RewiredConsts;

[Serializable]
public struct KeyRebindInfo
{
	[ActionIdProperty(typeof(RewiredConsts.Action))]
	public int actionToRebind;

	public AxisRange actionAxisRange;
}
