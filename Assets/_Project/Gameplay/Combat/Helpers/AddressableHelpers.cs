using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AstralShift.Helpers
{
	public class AddressableHelpers
	{
		public static bool TryGetAddressablePath(AssetReference assetReference, out string path)
		{
			path = string.Empty;
			if (assetReference == null || !assetReference.RuntimeKeyIsValid())
			{
				Debug.LogWarning("Asset Reference is null or invalid!");
				return false;
			}
			foreach (IResourceLocator resourceLocator in Addressables.ResourceLocators)
			{
				if (resourceLocator.Locate(assetReference.RuntimeKey, typeof(object), out var locations) && locations != null && locations.Count > 0)
				{
					path = locations[0].PrimaryKey;
					return true;
				}
			}
			return false;
		}

		public static bool TryGetAddressablePathFromRuntimeKey(object runtimeKey, out string path)
		{
			path = string.Empty;
			if (runtimeKey == null)
			{
				Debug.LogWarning("RuntimeKey is null!");
				return false;
			}
			foreach (IResourceLocator resourceLocator in Addressables.ResourceLocators)
			{
				if (resourceLocator.Locate(runtimeKey, typeof(object), out var locations) && locations != null && locations.Count > 0)
				{
					path = locations[0].PrimaryKey;
					return true;
				}
			}
			Debug.LogError("RuntimeKey is not valid in runtime!");
			return false;
		}

		public static Task<T> LoadAssetAsync<T>(AssetReference assetReference) where T : UnityEngine.Object
		{
			if (assetReference == null)
			{
				Debug.LogError("AssetReference is null.");
				return null;
			}
			AsyncOperationHandle<T> asyncOperationHandle = assetReference.LoadAssetAsync<T>();
			try
			{
				return asyncOperationHandle.Task;
			}
			catch
			{
				Debug.LogError($"Failed to load asset of type {typeof(T)}.");
				return null;
			}
		}

		public static bool TryLoadAssetAsyncWithHandle<T>(AssetReference assetReference, out AsyncOperationHandle<T> handle) where T : UnityEngine.Object
		{
			handle = default(AsyncOperationHandle<T>);
			if (assetReference == null || !assetReference.RuntimeKeyIsValid())
			{
				Debug.LogWarning("Asset Reference is null!");
				return false;
			}
			handle = assetReference.LoadAssetAsync<T>();
			return true;
		}

		public static AsyncOperationHandle<T> LoadAssetAsyncWithHandle<T>(AssetReference assetReference) where T : UnityEngine.Object
		{
			AsyncOperationHandle<T> result = default(AsyncOperationHandle<T>);
			if (assetReference == null || !assetReference.RuntimeKeyIsValid())
			{
				Debug.LogWarning("Asset Reference is null!");
				return result;
			}
			return assetReference.LoadAssetAsync<T>();
		}

		public static AsyncOperationHandle<T> LoadAssetAsyncWithHandle<T>(string key) where T : UnityEngine.Object
		{
			if (key == null || key == "")
			{
				throw new Exception("Asset Reference is null or empty!");
			}
			return Addressables.LoadAssetAsync<T>(key);
		}
	}
}
