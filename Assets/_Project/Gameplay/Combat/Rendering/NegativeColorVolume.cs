using System;
using AstralShift.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[Serializable]
[VolumeComponentMenu("Astral Shift/Negative Color")]
[VolumeRequiresRendererFeatures(new Type[] { typeof(ASRendererFeature) })]
[DisallowMultipleComponent]
public class NegativeColorVolume : VolumeComponent, IPostProcessComponent
{
	public BoolParameter enabled = new BoolParameter(value: true, overrideState: true);

	public BoolParameter roundMask = new BoolParameter(value: false, overrideState: true);

	public ClampedFloatParameter progress = new ClampedFloatParameter(0.5f, 0f, 1f, overrideState: true);

	public bool IsActive()
	{
		if (active)
		{
			return enabled.value;
		}
		return false;
	}

	public bool IsRoundMaskEnabled()
	{
		return roundMask.value;
	}

	public float GetRoundMaskProgress()
	{
		return progress.value * 2f;
	}

	public bool IsTileCompatible()
	{
		return false;
	}
}
