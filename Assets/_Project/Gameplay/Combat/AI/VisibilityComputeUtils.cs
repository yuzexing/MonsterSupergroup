using UnityEngine;

public class VisibilityComputeUtils
{
	private enum KernelMode
	{
		K32 = 0,
		K64 = 1,
		K128 = 2,
		K256 = 3
	}

	private static int ChooseThreadCount()
	{
		int computeSubGroupSize = SystemInfo.computeSubGroupSize;
		if (computeSubGroupSize >= 64)
		{
			return 256;
		}
		if (computeSubGroupSize >= 32)
		{
			return 128;
		}
		if (computeSubGroupSize >= 16)
		{
			return 64;
		}
		return 32;
	}

	public static void GetMostOptimalKernelAndGroups(int count, out int kernelIndex, out int groupsX)
	{
		int num = ChooseThreadCount();
		kernelIndex = num switch
		{
			256 => 3, 
			128 => 2, 
			64 => 1, 
			_ => 0, 
		};
		groupsX = Mathf.CeilToInt((float)count / (float)num);
	}

	public static void GetCompatibilityKernelAndGroups(int count, out int kernelIndex, out int groupsX)
	{
		kernelIndex = 2;
		groupsX = Mathf.CeilToInt((float)count / 128f);
		GetMostOptimalKernelAndGroups(count, out kernelIndex, out groupsX);
	}
}
