using AstralShift.QTI.Interactions;
using AstralShift.QTI.Interactors;

namespace AstralShift.HellMaiden.Audio
{
	public class StopMusicInteraction : Interaction
	{
		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			MusicPlayer.Instance.StopCurrentOverridenMusic();
			OnEnd();
		}
	}
}
