using Animancer;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class MeleeAttackWarning : AstralShift.HellMaiden.AI.Enemy.EnemyAttackWarning
{
	[SerializeField]
	private AnimancerComponent animancer;

	[SerializeField]
	private ClipTransition warningStart;

	[SerializeField]
	private ClipTransition warningEnd;

	public override void Show()
	{
		gameObject.SetActive(true);
		SetRenderersVisible(true);
		ResolveAnimancer();
		if (animancer != null && warningStart != null && warningStart.Clip != null)
		{
			animancer.Layers[0].Play(warningStart);
		}
	}

	public override void Hide()
	{
		ResolveAnimancer();
		if (warningEnd == null || warningEnd.Clip == null || animancer == null)
		{
			// The recovered Skeleton prefab has no warning-end clip and nests its
			// damage Collider under this object. Disabling the container would also
			// disable the active hit window, so hide only its visual renderers.
			SetRenderersVisible(false);
		}
		else
		{
			animancer.Layers[0].Play(warningEnd);
		}
	}

	public override async UniTask AwaitableHide()
	{
		ResolveAnimancer();
		if (warningEnd == null || warningEnd.Clip == null || animancer == null)
		{
			SetRenderersVisible(false);
		}
		else
		{
			await animancer.Layers[0].Play(warningEnd);
		}
	}

	public override void SetWarningTime(float warningTime, float attackTime)
	{
		if (warningStart != null && warningStart.Clip != null && warningTime > 0f)
		{
			warningStart.Speed = 1f / warningTime;
		}
	}

	private void ResolveAnimancer()
	{
		if (animancer == null)
		{
			animancer = GetComponent<AnimancerComponent>();
		}
	}

	private void SetRenderersVisible(bool visible)
	{
		Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
		for (int i = 0; i < renderers.Length; i++)
		{
			renderers[i].enabled = visible;
		}
	}
}
