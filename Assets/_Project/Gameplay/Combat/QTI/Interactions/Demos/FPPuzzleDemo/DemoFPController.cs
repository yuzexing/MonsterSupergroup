using AstralShift.QTI.Interactors;
using AstralShift.QTI.Triggers.Physics;
using UnityEngine;

namespace AstralShift.QTI.Interactions.Demos.FPPuzzleDemo
{
	[RequireComponent(typeof(CharacterController))]
	public class DemoFPController : MonoBehaviour, IInputInteractor, IInteractor
	{
		[Header("References")]
		public CharacterController characterController;

		public Camera camera;

		[Space]
		[Header("Movement")]
		[Tooltip("Speed in Units(Meters)/Second")]
		public float moveSpeed = 4f;

		public LayerMask groundLayerMask = -1;

		public bool useGravity = true;

		[Space]
		[Header("Camera")]
		[Tooltip("X Axis Speed in Angle(Degrees)/Second")]
		public float xAxisLookSpeed = 150f;

		[Tooltip("Y Axis Speed in Angle(Degrees)/Second")]
		public float yAxisLookSpeed = 80f;

		[Space]
		[Header("Camera Analog")]
		[Tooltip("X Axis Speed in Angle(Degrees)/Second")]
		public float xAxisAnalogLookSpeed = 25f;

		[Tooltip("Y Axis Speed in Angle(Degrees)/Second")]
		public float yAxisAnalogLookSpeed = 25f;

		public float lookAccelerationX = 10f;

		public float lookDecelerationX = 10f;

		public float maxLookSpeedX = 4f;

		public float lookAccelerationY = 10f;

		public float lookDecelerationY = 10f;

		public float maxLookSpeedY = 4f;

		[Space]
		[Header("Interaction")]
		public LayerMask interactionLayerMask;

		[Tooltip("X Axis Speed in Angle(Degrees)/Second")]
		public float interactionDistance = 1.25f;

		public int searchFrameCount = 2;

		private Transform _transform;

		private Transform _parent;

		private Transform _cameraTransform;

		private RaycastHit[] _groundResults;

		private Vector3 _gravityVelocity;

		private Vector3 _gravityFieldVelocityDelta;

		private const float GravityConst = -9.81f;

		private bool _canMove;

		private Vector2 _moveInput;

		private Vector2 _lookInput;

		private Vector2 _lookAnalogInput;

		private bool _interactInput;

		private Vector3 _moveDirection;

		private float _pitch;

		private float _yaw;

		private Vector2 currentLookSpeed;

		private RaycastHit _hitInfo;

		private int _numberOfHits;

		private InputTrigger _currentInteraction;

		private void Reset()
		{
			characterController = GetComponent<CharacterController>();
			characterController.detectCollisions = true;
		}

		private void Awake()
		{
			Reset();
			Application.targetFrameRate = Mathf.CeilToInt((float)Screen.currentResolution.refreshRateRatio.value);
			QualitySettings.vSyncCount = 2;
			_cameraTransform = camera.transform;
			_transform = base.transform;
			_yaw = _transform.localEulerAngles.y;
			_pitch = 0f;
		}

		private void Update()
		{
			if (_canMove)
			{
				HandleInputs();
				Move();
				ProcessLook();
				ProcessInteract();
			}
		}

		private void FixedUpdate()
		{
			if (_canMove)
			{
				SearchForKeyTriggerInteractions();
				Vector3 vector = base.transform.position + Vector3.up;
				Debug.DrawRay(vector, Vector3.down * characterController.height, Color.red, Time.fixedDeltaTime, depthTest: false);
				Physics.SphereCast(vector, characterController.radius, Vector3.down, out var hitInfo, characterController.height, groundLayerMask.value, QueryTriggerInteraction.Ignore);
				if (hitInfo.collider != null && hitInfo.collider.TryGetComponent<IGravityField>(out var component))
				{
					_gravityFieldVelocityDelta = component.GetMovementDelta();
				}
				else
				{
					_gravityFieldVelocityDelta = Vector3.zero;
				}
			}
		}

		private void LateUpdate()
		{
			if (_canMove)
			{
				Look();
			}
		}

		private void HandleInputs()
		{
			_moveInput.x = Input.GetAxisRaw("Horizontal");
			_moveInput.y = Input.GetAxisRaw("Vertical");
			_lookAnalogInput.x = Input.GetAxis("RightHorizontal");
			_lookAnalogInput.y = Input.GetAxis("RightVertical");
			_lookInput.x = Input.GetAxisRaw("Mouse X");
			_lookInput.y = Input.GetAxisRaw("Mouse Y");
			_interactInput = Input.GetButtonDown("Fire1");
		}

		public void SetPlayerFreeze(bool state)
		{
			_canMove = !state;
		}

		private void Move()
		{
			characterController.Move(_gravityFieldVelocityDelta);
			_moveDirection = _moveInput.x * _transform.right + _moveInput.y * _transform.forward;
			if (_moveDirection.magnitude > 1f)
			{
				_moveDirection.Normalize();
			}
			characterController.Move(_moveDirection * (Time.deltaTime * moveSpeed));
			if (characterController.isGrounded)
			{
				_gravityVelocity.y = 0f;
			}
			else
			{
				_gravityVelocity.y += -9.81f * Time.deltaTime;
			}
			characterController.Move(_gravityVelocity * Time.deltaTime);
		}

		public void SetGravity(bool state)
		{
			useGravity = state;
		}

		private void ProcessLook()
		{
			if (_lookInput.magnitude == 0f)
			{
				currentLookSpeed.x = Mathf.MoveTowards(currentLookSpeed.x, _lookAnalogInput.x * maxLookSpeedX, lookAccelerationX * Time.smoothDeltaTime);
				currentLookSpeed.y = Mathf.MoveTowards(currentLookSpeed.y, _lookAnalogInput.y * maxLookSpeedY, lookAccelerationY * Time.smoothDeltaTime);
				if (_lookAnalogInput.x == 0f)
				{
					currentLookSpeed.x = Mathf.MoveTowards(currentLookSpeed.x, 0f, lookDecelerationX * Time.smoothDeltaTime);
				}
				if (_lookAnalogInput.y == 0f)
				{
					currentLookSpeed.y = Mathf.MoveTowards(currentLookSpeed.y, 0f, lookDecelerationY * Time.smoothDeltaTime);
				}
				_yaw += currentLookSpeed.x * xAxisAnalogLookSpeed * Time.smoothDeltaTime;
				_pitch -= currentLookSpeed.y * yAxisAnalogLookSpeed * Time.smoothDeltaTime;
			}
			else
			{
				currentLookSpeed.x = _lookInput.x;
				currentLookSpeed.y = _lookInput.y;
				_yaw += currentLookSpeed.x * xAxisLookSpeed * Time.smoothDeltaTime;
				_pitch -= currentLookSpeed.y * yAxisLookSpeed * Time.smoothDeltaTime;
			}
			_pitch = Mathf.Clamp(_pitch, -89f, 89f);
		}

		private void ProcessInteract()
		{
			if (_interactInput)
			{
				_interactInput = false;
				TryInteract();
			}
		}

		private void Look()
		{
			_transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
			_cameraTransform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
		}

		public Transform GetTransform()
		{
			return base.transform;
		}

		public bool TryInteract()
		{
			_currentInteraction?.Interact(this);
			return _currentInteraction != null;
		}

		public InputTrigger GetInteraction()
		{
			if (!Physics.Raycast(camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out _hitInfo, interactionDistance, interactionLayerMask.value))
			{
				return null;
			}
			if (_hitInfo.collider.TryGetComponent<InputTrigger>(out var component))
			{
				return component;
			}
			return null;
		}

		private void SearchForKeyTriggerInteractions()
		{
			if (Time.frameCount % searchFrameCount == 0)
			{
				InputTrigger interaction = GetInteraction();
				if ((object)interaction == null)
				{
					_currentInteraction?.ResetVisuals();
					_currentInteraction = null;
				}
				else if (interaction != null && _currentInteraction == null)
				{
					_currentInteraction = interaction;
					_currentInteraction.HighlightVisuals();
				}
				else if (interaction != _currentInteraction)
				{
					_currentInteraction.ResetVisuals();
					_currentInteraction = interaction;
					_currentInteraction.HighlightVisuals();
				}
			}
		}

		protected void OnGUI()
		{
			float value = Screen.width * Screen.height / 2073600;
			value = Mathf.Clamp(value, 1f, 1.75f);
			GUIStyle label = GUI.skin.label;
			label.fontSize = (int)(12f * value);
			GUI.Box(new Rect((float)Screen.width - 260f * value, 10f * value, 250f * value, 150f * value), "");
			GUI.Box(new Rect((float)Screen.width - 245f * value, 30f * value, 250f * value, 150f * value), "Controls", label);
			GUI.Label(new Rect((float)Screen.width - 245f * value, 60f * value, 250f * value, 30f * value), "Use Arrow Keys or WASD to move.", label);
			GUI.Label(new Rect((float)Screen.width - 245f * value, 90f * value, 250f * value, 30f * value), "Use Mouse Left Button to Interact.", label);
			GUI.Label(new Rect((float)Screen.width - 245f * value, 120f * value, 250f * value, 30f * value), "R to restart level.", label);
		}
	}
}
