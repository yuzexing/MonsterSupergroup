using Animancer;
using UnityEngine;

public class UltimateIcon : MonoBehaviour
{
	[SerializeField]
	private AnimancerComponent animancer;

	[SerializeField]
	private ClipTransition ultimateChargeActive;

	[SerializeField]
	private ClipTransition ultimateChargeInactive;

	private Coroutine ultCoroutine;

	private void Start()
	{
		animancer.Play(ultimateChargeInactive);
	}

	public void SetIcon(Sprite iconSprite)
	{
	}

	public void SetCharge(bool ultimate)
	{
		animancer.Play(ultimate ? ultimateChargeActive : ultimateChargeInactive);
	}
}
