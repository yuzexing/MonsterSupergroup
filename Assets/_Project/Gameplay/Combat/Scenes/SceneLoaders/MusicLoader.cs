using AstralShift.HellMaiden.Audio;
using AstralShift.Initialization;
using Cysharp.Threading.Tasks;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Scenes.SceneLoaders
{
	public class MusicLoader : SceneLoader
	{
		[SerializeField]
		private EventReference sceneMusic;

		[SerializeField]
		private bool stopOverridennMusic;

		[SerializeField]
		private bool stopOverridenImeadiatly;

		[SerializeField]
		private bool stopAllPreviousMusic;

		public override UniTask LoadAsync()
		{
			MusicPlayer.Instance.QueueMusic(sceneMusic.Guid);
			if (stopAllPreviousMusic)
			{
				SceneMaster.Instance.OnSceneInit += delegate
				{
					MusicPlayer.Instance.StopAllMusic();
				};
			}
			if (stopOverridennMusic)
			{
				SceneMaster.Instance.OnSceneInit += delegate
				{
					MusicPlayer.Instance.StopCurrentOverridenMusic(stopOverridenImeadiatly);
				};
			}
			return UniTask.CompletedTask;
		}
	}
}
