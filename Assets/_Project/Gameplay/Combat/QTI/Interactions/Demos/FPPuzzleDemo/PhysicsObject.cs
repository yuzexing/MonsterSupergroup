using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions.Demos.FPPuzzleDemo
{
	[RequireComponent(typeof(Rigidbody), typeof(Collider))]
	public class PhysicsObject : MonoBehaviour, IGravityField, IInteractor
	{
		public Rigidbody rb;

		private Collider _collider;

		[Header("Ground Detection")]
		public LayerMask layerMask = -1;

		public float rayDistance = 1f;

		private RaycastHit[] _groundHits;

		private bool _inGravityField;

		private Vector3 _gravityFieldVelocity;

		private Vector3 _gravityVelocity;

		private const float GravityConst = -9.81f;

		public void Awake()
		{
			if (rb == null)
			{
				rb = GetComponent<Rigidbody>();
			}
			rb.interpolation = RigidbodyInterpolation.Interpolate;
			rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
			_collider = GetComponent<Collider>();
			_groundHits = new RaycastHit[2];
		}

		public void FixedUpdate()
		{
			DetectGround();
		}

		private void Update()
		{
			ApplyGravity();
		}

		private void ApplyGravity()
		{
			if (_inGravityField)
			{
				rb.useGravity = false;
				rb.MovePosition(rb.position + _gravityFieldVelocity);
			}
			else
			{
				rb.useGravity = true;
			}
		}

		private void DetectGround()
		{
			if (Physics.RaycastNonAlloc(_collider.bounds.center, Vector3.down, _groundHits, rayDistance, layerMask, QueryTriggerInteraction.Ignore) == 0)
			{
				_inGravityField = false;
				return;
			}
			RaycastHit[] groundHits = _groundHits;
			for (int i = 0; i < groundHits.Length; i++)
			{
				RaycastHit raycastHit = groundHits[i];
				if (!(raycastHit.collider == null) && !(raycastHit.collider == _collider) && raycastHit.collider.TryGetComponent<IGravityField>(out var component))
				{
					_inGravityField = true;
					_gravityFieldVelocity = component.GetMovementDelta();
					return;
				}
			}
			_gravityFieldVelocity = Vector3.zero;
			_inGravityField = false;
		}

		public Transform GetTransform()
		{
			return base.transform;
		}
	}
}
