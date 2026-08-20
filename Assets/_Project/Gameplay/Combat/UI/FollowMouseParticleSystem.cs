using UnityEngine;

namespace AstralShift.UI
{
	public class FollowMouseParticleSystem : MonoBehaviour
	{
		public RectTransform canvasRoot;

		public GameObject burstPrefab;

		private Vector3 _currentMousePosition;

		private void Update()
		{
			_currentMousePosition = Input.mousePosition;
			_currentMousePosition.z = -10f;
			base.transform.position = Camera.main.ScreenToWorldPoint(_currentMousePosition);
			if (Input.GetMouseButtonDown(0) && burstPrefab != null)
			{
				RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRoot, Input.mousePosition, null, out var localPoint);
				GameObject obj = Object.Instantiate(burstPrefab, canvasRoot);
				obj.GetComponent<RectTransform>().anchoredPosition = localPoint;
				ParticleSystem componentInChildren = obj.GetComponentInChildren<ParticleSystem>();
				componentInChildren.Play();
				Object.Destroy(obj, componentInChildren.main.duration + componentInChildren.main.startLifetime.constantMax);
			}
		}
	}
}
