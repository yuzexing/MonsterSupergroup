using System;
using UnityEngine;

public class ParticleStoppedNotifier : MonoBehaviour
{
	public Action<ParticleStoppedNotifier> OnStopped;

	private ParticleSystem _ps;

	private void Awake()
	{
		_ps = GetComponent<ParticleSystem>();
		ParticleSystem.MainModule main = _ps.main;
		main.stopAction = ParticleSystemStopAction.Callback;
	}

	private void OnParticleSystemStopped()
	{
		OnStopped?.Invoke(this);
	}

	public void ClearCallback()
	{
		OnStopped = null;
	}
}
