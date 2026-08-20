using System.Collections;
using AstralShift.Control.Controllers;
using AstralShift.HellMaiden;
using AstralShift.HellMaiden.Player;
using AstralShift.Managers;
using UnityEngine;
using UnityEngine.Playables;

public class TransitionInMap : MonoBehaviour
{
	[SerializeField]
	private PlayableDirector _director;

	[SerializeField]
	private float directorTimeout = 1.5f;

	public void TransitionToPosition(Vector3 position)
	{
		StartCoroutine(TransitionCheckRoutine(position));
	}

	private IEnumerator TransitionCheckRoutine(Vector3 position)
	{
		bool isInControllerBasedUltimateAttackController = PlayerState.IsInControllerBasedUltimateAttackController();
		while (PlayerState.IsBusy() && !isInControllerBasedUltimateAttackController)
		{
			yield return null;
		}
		if (isInControllerBasedUltimateAttackController)
		{
			ControllerManager.Instance.YieldGameController();
		}
		GameDirector.Instance.Player.SetInvulnerable(state: true);
		ControllerManager.Instance.OverrideGameController<NoInputGameController>();
		_director.transform.position = GameDirector.Instance.Player.transform.position;
		_director.Play();
		PauseManager.Instance.PausePausables();
		yield return new WaitForSeconds((float)_director.duration);
		ControllerManager.Instance.YieldGameController();
		GameDirector.Instance.Player.transform.position = position;
		PauseManager.Instance.ResumePausables();
		GameDirector.Instance.Player.SetInvulnerable(state: false);
		yield return new WaitForSeconds(directorTimeout);
		Object.Destroy(_director.gameObject);
		yield return null;
	}
}
