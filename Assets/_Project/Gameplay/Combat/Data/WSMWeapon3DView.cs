using AstralShift.HellMaiden.UI;
using DG.Tweening;
using UnityEngine;

public class WSMWeapon3DView : MonoBehaviour
{
	[SerializeField]
	protected UIGeneric3DRenderTarget renderTarget;

	[SerializeField]
	protected UIGeneric3DRenderer prefab;

	[SerializeField]
	protected CanvasGroup canvasGroup;

	[SerializeField]
	protected float fadeDuration = 0.1f;

	protected UIGeneric3DRenderer _instance;

	private Tween _fadeTween;

	public void Initialize()
	{
		if ((bool)prefab && !_instance)
		{
			_instance = Object.Instantiate(prefab);
			renderTarget.Init(_instance);
			canvasGroup.alpha = 0f;
		}
	}

	public void Show()
	{
		_fadeTween?.Kill();
		_fadeTween = canvasGroup.DOFade(1f, fadeDuration).SetUpdate(UpdateType.Late, isIndependentUpdate: true);
	}

	public void Hide(bool instant = false)
	{
		if (instant)
		{
			_fadeTween?.Kill();
			canvasGroup.alpha = 0f;
		}
		else
		{
			_fadeTween?.Kill();
			_fadeTween = canvasGroup.DOFade(0f, fadeDuration).SetUpdate(UpdateType.Late, isIndependentUpdate: true);
		}
	}
}
