using System;
using Animancer;
using AstralShift.Control;
using AstralShift.HellMaiden.Combat;
using AstralShift.Helpers;
using Cysharp.Threading.Tasks;
using Rewired;
using UnityEngine;

public class ControlsTutorial : MonoBehaviour
{
	[SerializeField]
	private float tutorialDuration = 10f;

	[SerializeField]
	private AnimancerComponent animancerComponent;

	[SerializeField]
	private ClipTransition showTutorial;

	[SerializeField]
	private ClipTransition hideTutorial;

	[SerializeField]
	private GameObject keyboardMovementsGlyphs;

	[SerializeField]
	private GameObject controllerMovementsGlyphs;

	public event Action OnTutorialFinished;

	public void Init(Action onEnd)
	{
		StartTutorial();
		GameEvents instance = GameEvents.Instance;
		instance.OnAfterPlayerDeath = (Action)Delegate.Combine(instance.OnAfterPlayerDeath, new Action(Destroy));
		OnTutorialFinished += onEnd;
		OnControllerChanged();
		ControllerLifetime.RefreshInputs();
		ControllerLifetime.OnControllerChanged += OnControllerChanged;
	}

	private void Destroy()
	{
		if (base.gameObject != null)
		{
			UnityEngine.Object.Destroy(base.gameObject);
		}
	}

	private void OnDestroy()
	{
		ControllerLifetime.OnControllerChanged -= OnControllerChanged;
		GameEvents instance = GameEvents.Instance;
		instance.OnAfterPlayerDeath = (Action)Delegate.Remove(instance.OnAfterPlayerDeath, new Action(Destroy));
	}

	private void OnControllerChanged()
	{
		if (ControllerLifetime.ActiveControllerType == ControllerType.Keyboard || ControllerLifetime.ActiveControllerType == ControllerType.Mouse)
		{
			keyboardMovementsGlyphs.SetActive(value: true);
			controllerMovementsGlyphs.SetActive(value: false);
		}
		else
		{
			keyboardMovementsGlyphs.SetActive(value: false);
			controllerMovementsGlyphs.SetActive(value: true);
		}
	}

	public async void StartTutorial()
	{
		await ShowAnimation();
		StartCoroutine(Wait.SetTimeout(tutorialDuration, EndTutorial));
	}

	public async void EndTutorial()
	{
		await HideAnimation();
		this.OnTutorialFinished?.Invoke();
		this.OnTutorialFinished = null;
		if (base.gameObject != null)
		{
			UnityEngine.Object.Destroy(base.gameObject);
		}
	}

	private async UniTask ShowAnimation()
	{
		await AnimancerHelpers.AnimationTask(animancerComponent, showTutorial);
	}

	private async UniTask HideAnimation()
	{
		await AnimancerHelpers.AnimationTask(animancerComponent, hideTutorial);
	}
}
