using System;
using AstralShift.Initialization;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.Helpers.Initialization
{
	public class SceneLoadingDelegator : MonoBehaviour
	{
		public SceneLoader[] loaders;

		private bool hasLoaded;

		public async UniTask LoadAsync()
		{
			for (int i = 0; i < loaders.Length; i++)
			{
				Debug.Log("Loading... " + loaders[i].name + " " + Time.time);
				try
				{
					await loaders[i].LoadAsync();
				}
				catch (Exception message)
				{
					Debug.LogError(message);
				}
			}
		}
	}
}
