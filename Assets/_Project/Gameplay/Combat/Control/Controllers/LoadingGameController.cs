using AstralShift.HellMaiden;
using AstralShift.HellMaiden.UI;
using AstralShift.Managers;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace AstralShift.Control.Controllers
{
	public class LoadingGameController : GameController
	{
		[SerializeField]
		private EventReference loadingSnapshot;

		private EventInstance _snapshotInstance;

		public bool Paused { get; set; }

		public override void Activate()
		{
			CombatUIManager.Instance?.CloseHUD();
			PointerManager.Instance.HideMouseCursor();
			GameDirector.Instance.Player.StopMovement();
			StartSnapshot();
		}

		public void PauseDuringLoading()
		{
			PauseManager.Instance.PauseGame();
			Paused = true;
		}

		public override void Deactivate()
		{
			if (Paused)
			{
				PauseManager.Instance.ResumeGame();
			}
			Paused = false;
			StopSnapshot();
		}

		private void StartSnapshot()
		{
			if (!_snapshotInstance.isValid())
			{
				_snapshotInstance = RuntimeManager.CreateInstance(loadingSnapshot);
				_snapshotInstance.start();
			}
		}

		private void StopSnapshot()
		{
			if (_snapshotInstance.isValid())
			{
				_snapshotInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
				_snapshotInstance.release();
				_snapshotInstance.clearHandle();
			}
		}

		protected override void OnDisable()
		{
			base.OnDisable();
			StopSnapshot();
		}
	}
}
