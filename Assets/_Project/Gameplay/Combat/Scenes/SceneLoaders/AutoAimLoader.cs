using AstralShift.Initialization;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Scenes.SceneLoaders
{
	public class AutoAimLoader : SceneLoader
	{
		public override UniTask LoadAsync()
		{
			GameDirector.Instance.Player.SubscribeAutoAim();
			SceneMaster.Instance.OnSceneUnloadPersist += delegate
			{
				GameDirector.Instance.Player.UnSubscribeAutoAim();
			};
			Debug.Log("Ultimate Attack Loaded");
			return UniTask.CompletedTask;
		}
	}
}
