using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions
{
	[AddComponentMenu("QTI/Interactions/AddForceInteraction")]
	public class AddForceInteraction : Interaction
	{
		public enum Mode
		{
			_3D = 0,
			_2D = 1
		}

		public enum ForceType
		{
			world = 0,
			local = 1,
			relative = 2
		}

		public Mode mode;

		public ForceType forceType;

		[Tooltip("Rigidbody to add force to.")]
		public Rigidbody body;

		public Vector2 orientation;

		public ForceMode forceMode;

		[Tooltip("Rigidbody2D to add force to.")]
		public Rigidbody2D body2D;

		public float angle;

		public ForceMode2D forceMode2D;

		public float magnitude = 1f;

		public override void Interact(IInteractor interactor)
		{
			base.Interact(interactor);
			switch (mode)
			{
			case Mode._3D:
			{
				Vector3 vector2 = Quaternion.Euler(orientation) * Vector3.forward;
				vector2.Normalize();
				if ((bool)body)
				{
					switch (forceType)
					{
					case ForceType.world:
						body.AddForce(vector2 * magnitude, forceMode);
						break;
					case ForceType.local:
						body.AddRelativeForce(vector2 * magnitude, forceMode);
						break;
					case ForceType.relative:
						vector2 = body.transform.position - base.transform.position;
						vector2.Normalize();
						body.AddForce(vector2 * magnitude, forceMode);
						break;
					}
				}
				else
				{
					Debug.LogWarning("AddForceInteraction: no Rigidbody assigned!");
				}
				break;
			}
			case Mode._2D:
			{
				Vector2 vector = Quaternion.Euler(new Vector3(0f, 0f, angle)) * Vector3.right;
				vector.Normalize();
				if ((bool)body2D)
				{
					switch (forceType)
					{
					case ForceType.world:
						body2D.AddForce(vector * magnitude, forceMode2D);
						break;
					case ForceType.local:
						body2D.AddRelativeForce(vector * magnitude, forceMode2D);
						break;
					case ForceType.relative:
					{
						Vector3 vector2 = body2D.transform.position - base.transform.position;
						vector2.Normalize();
						body2D.AddForce(vector2 * magnitude, forceMode2D);
						break;
					}
					}
				}
				else
				{
					Debug.LogWarning("AddForceInteraction: no Rigidbody2D assigned!");
				}
				break;
			}
			}
			OnEnd();
		}

		private bool GetStartPoint(out Vector3 startPoint)
		{
			switch (forceType)
			{
			case ForceType.world:
				_ = mode;
				startPoint = base.transform.position;
				return true;
			case ForceType.local:
				if (mode == Mode._3D)
				{
					if (!body)
					{
						startPoint = new Vector3(0f, 0f, 0f);
						return false;
					}
					startPoint = body.transform.position;
					return true;
				}
				if (!body2D)
				{
					startPoint = new Vector3(0f, 0f, 0f);
					return false;
				}
				startPoint = body2D.transform.position;
				return true;
			case ForceType.relative:
				if (mode == Mode._3D)
				{
					if (!body)
					{
						startPoint = new Vector3(0f, 0f, 0f);
						return false;
					}
					startPoint = body.transform.position;
					return true;
				}
				if (!body2D)
				{
					startPoint = new Vector3(0f, 0f, 0f);
					return false;
				}
				startPoint = body2D.transform.position;
				return true;
			default:
				startPoint = new Vector3(0f, 0f, 0f);
				return false;
			}
		}

		private Vector3 GetDirection()
		{
			switch (forceType)
			{
			case ForceType.world:
			{
				Vector3 result;
				if (mode == Mode._3D)
				{
					result = Quaternion.Euler(orientation) * Vector3.forward;
					result.Normalize();
					return result;
				}
				result = Quaternion.Euler(new Vector3(0f, 0f, angle)) * Vector3.right;
				result.Normalize();
				return result;
			}
			case ForceType.local:
			{
				Vector3 result;
				if (mode == Mode._3D)
				{
					result = Quaternion.Euler(orientation) * Vector3.forward;
					result.Normalize();
					return result;
				}
				result = Quaternion.Euler(new Vector3(0f, 0f, angle)) * Vector3.right;
				result.Normalize();
				return result;
			}
			case ForceType.relative:
			{
				Vector3 result;
				if (mode == Mode._3D)
				{
					if (!body)
					{
						return Vector3.zero;
					}
					result = body.transform.position - base.transform.position;
					result.Normalize();
					return result;
				}
				if (!body2D)
				{
					return Vector3.zero;
				}
				result = body2D.transform.position - base.transform.position;
				result.Normalize();
				return result;
			}
			default:
				return Vector3.zero;
			}
		}
	}
}
