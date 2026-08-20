using System;

namespace AstralShift.Managers
{
	public interface IPausable
	{
		void Subscribe()
		{
			PauseManager instance = PauseManager.Instance;
			instance.OnPausePausables = (Action)Delegate.Combine(instance.OnPausePausables, new Action(OnPausePausables));
			PauseManager instance2 = PauseManager.Instance;
			instance2.OnResumePausables = (Action)Delegate.Combine(instance2.OnResumePausables, new Action(OnResumePausables));
			PauseManager instance3 = PauseManager.Instance;
			instance3.OnGamePause = (Action)Delegate.Combine(instance3.OnGamePause, new Action(OnGamePause));
			PauseManager instance4 = PauseManager.Instance;
			instance4.OnGameResume = (Action)Delegate.Combine(instance4.OnGameResume, new Action(OnGameResume));
		}

		void UnSubscribe()
		{
			PauseManager instance = PauseManager.Instance;
			instance.OnPausePausables = (Action)Delegate.Remove(instance.OnPausePausables, new Action(OnPausePausables));
			PauseManager instance2 = PauseManager.Instance;
			instance2.OnResumePausables = (Action)Delegate.Remove(instance2.OnResumePausables, new Action(OnResumePausables));
			PauseManager instance3 = PauseManager.Instance;
			instance3.OnGamePause = (Action)Delegate.Remove(instance3.OnGamePause, new Action(OnGamePause));
			PauseManager instance4 = PauseManager.Instance;
			instance4.OnGameResume = (Action)Delegate.Remove(instance4.OnGameResume, new Action(OnGameResume));
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
