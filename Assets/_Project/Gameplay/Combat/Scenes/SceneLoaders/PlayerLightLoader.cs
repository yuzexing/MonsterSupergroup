using AstralShift.HellMaiden.Player;
using AstralShift.Initialization;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Scenes.SceneLoaders
{
	public class PlayerLightLoader : SceneLoader
	{
		[SerializeField]
		private GameObject[] playerLights;

		public override UniTask LoadAsync()
		{
			CharacterInvisibility characterInvisibility = GameDirector.Instance.Player.gameObject.GetComponent<CharacterInvisibility>();
			for (int i = 0; i < playerLights.Length; i++)
			{
				GameObject light = Object.Instantiate(playerLights[i], characterInvisibility.lightsParent);
				characterInvisibility.visibleObjects.Add(light);
				SceneMaster.Instance.OnSceneUnload += delegate
				{
					characterInvisibility.visibleObjects.Remove(light);
					Object.Destroy(light);
				};
			}
			Debug.Log("Player Lights loaded");
			return UniTask.CompletedTask;
		}
	}
}
