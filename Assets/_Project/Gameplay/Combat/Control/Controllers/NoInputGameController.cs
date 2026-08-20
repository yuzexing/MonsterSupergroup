using AstralShift.HellMaiden;

namespace AstralShift.Control.Controllers
{
	public class NoInputGameController : GameController
	{
		public override void Activate()
		{
			GameDirector.Instance.Player.StopMovement();
		}

		public override void Deactivate()
		{
		}
	}
}
