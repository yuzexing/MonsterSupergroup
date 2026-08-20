using AstralShift.HellMaiden.UI.Cards;
using AstralShift.Helpers.Attributes;
using UnityEngine;
using UnityEngine.Animations;

namespace AstralShift.HellMaiden.UI
{
	public class ViewFollower : MonoBehaviour
	{
		[SerializeField]
		[ReadOnly]
		private int _siblingIndex;

		[Header("Base References")]
		[SerializeField]
		protected Transform followTransform;

		[SerializeField]
		protected CanvasGroup canvasGroup;

		[SerializeField]
		protected ScaleConstraint scaleConstraint;

		[Space]
		[SerializeField]
		[ReadOnly]
		private Transform parentToReturnTo;

		[Header("Follow Settings")]
		[SerializeField]
		protected CardAnimationSettings animationSettings;

		public bool movementFollow = true;

		public bool hardFollow;

		public int SiblingIndex => _siblingIndex;

		public Transform ParentToReturnTo => parentToReturnTo;

		public void Show()
		{
			canvasGroup.alpha = 1f;
		}

		public void Hide()
		{
			canvasGroup.alpha = 0f;
		}

		protected virtual void LateUpdate()
		{
			if (hardFollow)
			{
				HardMovementFollow();
			}
			else if (movementFollow)
			{
				SmoothMovementFollow();
			}
		}

		protected virtual void HardMovementFollow()
		{
			base.transform.position = followTransform.position;
		}

		protected virtual void SmoothMovementFollow()
		{
			base.transform.position = Vector3.Lerp(base.transform.position, followTransform.position, animationSettings.FollowSpeed * Time.unscaledDeltaTime);
		}

		public void ResumeMovementFollow()
		{
			hardFollow = true;
			movementFollow = true;
		}

		public void StopMovementFollow()
		{
			hardFollow = false;
			movementFollow = false;
		}

		public void AssignParent(Transform parent)
		{
			parentToReturnTo = parent;
			base.transform.SetParent(parentToReturnTo);
		}

		public void ReturnToParent()
		{
			if (!(parentToReturnTo == null))
			{
				AssignParent(parentToReturnTo.transform);
			}
		}

		public void SetSiblingIndex(int siblingIndex)
		{
			base.transform.SetSiblingIndex(siblingIndex);
			_siblingIndex = siblingIndex;
		}

		public void DeactivateParentCardScaling()
		{
			if (!(scaleConstraint == null))
			{
				scaleConstraint.constraintActive = false;
			}
		}

		public void ActivateParentCardScaling()
		{
			if (!(scaleConstraint == null))
			{
				scaleConstraint.constraintActive = true;
			}
		}
	}
}
