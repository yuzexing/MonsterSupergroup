using System;
using System.Collections;
using UnityEngine;
using UnityEngine.VFX;

public class ChainLightningController : MonoBehaviour
{
	[SerializeField]
	private VisualEffect vfx;

	public Transform start;

	public Transform end;

	public GameObject disableController;

	private bool active;

	private float maxDistance = 12f;

	private float duration;

	private float timeActive;

	private Transform StartTransform;

	private Transform EndTransform;

	public event Action OnEnd;

	private void Update()
	{
		if (active)
		{
			start.position = StartTransform.position;
			end.position = EndTransform.position;
			float num = Vector3.Distance(start.position, end.position);
			timeActive += Time.deltaTime;
			if (num > maxDistance || timeActive > duration)
			{
				active = false;
				StartCoroutine(KillMyself(0.5f));
			}
		}
	}

	private IEnumerator KillMyself(float t)
	{
		yield return new WaitForSeconds(t);
		disableController.SetActive(value: false);
		yield return null;
		this.OnEnd?.Invoke();
		this.OnEnd = null;
	}

	public void InitializeEffect(Transform start, Transform end, float maxRange = 12f, float duration = 0.5f)
	{
		StartTransform = start;
		EndTransform = end;
		this.start.position = StartTransform.position;
		this.end.position = EndTransform.position;
		maxDistance = maxRange;
		this.duration = duration;
	}

	public void Play(Action OnEnd = null)
	{
		this.OnEnd = OnEnd;
		vfx.Reinit();
		active = true;
		timeActive = 0f;
		disableController.SetActive(value: true);
	}
}
