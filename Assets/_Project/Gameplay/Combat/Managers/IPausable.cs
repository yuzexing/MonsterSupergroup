using System;

namespace AstralShift.Managers
{
	public interface IPausable
	{
		void Subscribe()
		{
			PauseManager instance = PauseManager.Instance;
			if (instance == null)
			{
				return;
			}
			instance.OnPausePausables = (Action)Delegate.Combine(instance.OnPausePausables, new Action(OnPausePausables));
			instance.OnResumePausables = (Action)Delegate.Combine(instance.OnResumePausables, new Action(OnResumePausables));
			instance.OnGamePause = (Action)Delegate.Combine(instance.OnGamePause, new Action(OnGamePause));
			instance.OnGameResume = (Action)Delegate.Combine(instance.OnGameResume, new Action(OnGameResume));
		}

		void UnSubscribe()
		{
			PauseManager instance = PauseManager.Instance;
			if (instance == null)
			{
				return;
			}
			instance.OnPausePausables = (Action)Delegate.Remove(instance.OnPausePausables, new Action(OnPausePausables));
			instance.OnResumePausables = (Action)Delegate.Remove(instance.OnResumePausables, new Action(OnResumePausables));
			instance.OnGamePause = (Action)Delegate.Remove(instance.OnGamePause, new Action(OnGamePause));
			instance.OnGameResume = (Action)Delegate.Remove(instance.OnGameResume, new Action(OnGameResume));
		}

		void OnPausePausables();

		void OnResumePausables();

		void OnGamePause()
		{
		}

		void OnGameResume()
		{
		}
	}
}
