using System;
using AstralShift.Helpers;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace AstralShift.HellMaiden.Data
{
	[Serializable]
	public class NamedAssetReference : AssetReference
	{
		public string Name
		{
			get
			{
				if (AddressableHelpers.TryGetAddressablePathFromRuntimeKey(RuntimeKey, out var path))
				{
					int num = path.IndexOf("CUT", StringComparison.Ordinal);
					if (num >= 0)
					{
						return path.Substring(num);
					}
					int num2 = path.LastIndexOf('/');
					if (num2 < 0)
					{
						return path;
					}
					return path.Substring(num2 + 1);
				}
				Debug.LogError($"Could not get name for asset reference {RuntimeKey}. Please review the asset reference and make sure it has a valid path.");
				return string.Empty;
			}
		}

		public NamedAssetReference(string guid)
			: base(guid)
		{
		}
	}
}
