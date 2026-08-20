using AstralShift.HellMaiden.Audio;
using AstralShift.Rendering;

namespace AstralShift.Control.Controllers
{
	public abstract class UIController : GameController
	{
		public override void Activate()
		{
			MusicPlayer.Instance.SetSnapShot(MusicPlayer.SnapshotID.Menu);
			InputHandler.EnableMenuInputs();
			ASRendererFeature.Instance.EnableFullscreenBlurRenderPass(enable: true);
		}

		public override void Deactivate()
		{
			ASRendererFeature.Instance.EnableFullscreenBlurRenderPass(enable: false);
		}
	}
}
