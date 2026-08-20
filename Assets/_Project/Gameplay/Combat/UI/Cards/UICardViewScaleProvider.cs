using AstralShift.QTI.Helpers.Attributes;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Cards
{
	[RequireComponent(typeof(RectTransform))]
	public class UICardViewScaleProvider : MonoBehaviour
	{
		[SerializeField]
		protected bool overrideScale;

		[SerializeField]
		[ConditionalHide("overrideScale", true)]
		protected Vector3 scale = Vector3.one;

		[SerializeField]
		protected float scaleMultiplier = 1f;

		private Transform _transform;

		public Transform Transform
		{
			get
			{
				if (_transform == null)
				{
					_transform = base.transform;
				}
				return _transform;
			}
		}

		public Vector3 GetScale()
		{
			if (overrideScale)
			{
				return scale * scaleMultiplier;
			}
			return Transform.lossyScale * scaleMultiplier;
		}

		public void ChangeScale(Vector3 value)
		{
			scale = value;
		}

		public void ChangeScaleMultiplier(float value)
		{
			scaleMultiplier = value;
		}

		public void ResetScale()
		{
			scale = Vector3.one;
			scaleMultiplier = 1f;
		}
	}
}
