using AstralShift.Managers;
using UnityEngine;
using UnityEngine.Playables;

namespace AstralShift.HellMaiden.Timeline
{
	public class TimelineSlowMotion : MonoBehaviour, IPausable
	{
		[SerializeField]
		private PlayableDirector playableDirector;

		public void OnPausePausables()
		{
			playableDirector.timeUpdateMode = DirectorUpdateMode.GameTime;
		}

		public void OnGamePause()
		{
			playableDirector.timeUpdateMode = DirectorUpdateMode.GameTime;
		}

		public void OnResumePausables()
		{
			playableDirector.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
		}

		public void OnGameResume()
		{
			playableDirector.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
		}

		private void Start()
		{
			SubscribeSceneEvents();
		}

		protected void OnDestroy()
		{
			UnSubscribeSceneEvents();
		}

		private void SubscribeSceneEvents()
		{
			((IPausable)this).Subscribe();
		}

		private void UnSubscribeSceneEvents()
		{
			((IPausable)this).UnSubscribe();
		}
	}
}
