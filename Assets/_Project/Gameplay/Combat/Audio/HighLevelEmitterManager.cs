using System.Collections.Generic;
using FMODUnity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AstralShift.HellMaiden.Audio
{
	public class HighLevelEmitterManager : MonoBehaviour
	{
		private List<StudioEventEmitter> sounds = new List<StudioEventEmitter>();

		private void Awake()
		{
			SceneManager.sceneLoaded += delegate
			{
				ClearAllSounds();
			};
		}

		public void RegisterSound(StudioEventEmitter sound)
		{
			sounds.Add(sound);
		}

		public void StopAllSounds()
		{
			foreach (StudioEventEmitter sound in sounds)
			{
				sound.Stop();
			}
		}

		public void RestartAllSounds()
		{
			foreach (StudioEventEmitter sound in sounds)
			{
				sound.Play();
			}
		}

		public void ClearAllSounds()
		{
			sounds.Clear();
		}
	}
}
