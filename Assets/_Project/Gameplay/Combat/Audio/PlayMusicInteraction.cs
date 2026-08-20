using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;
using FMODUnity;
using UnityEngine;

namespace AstralShift.HellMaiden.Audio
{
	public class PlayMusicInteraction : Interaction
	{
		[SerializeField]
		private EventReference musicToPlay;

		[SerializeField]
		private bool overrideMusic = true;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			if (overrideMusic)
			{
				MusicPlayer.Instance.PlayOverridenMusic(musicToPlay.Guid);
			}
			else
			{
				MusicPlayer.Instance.QueueMusic(musicToPlay.Guid);
				MusicPlayer.Instance.PlayNextMusic();
			}
			OnEnd();
		}
	}
}
