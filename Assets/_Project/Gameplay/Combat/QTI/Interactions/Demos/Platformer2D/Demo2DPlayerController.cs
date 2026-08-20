using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions.Demos.Platformer2D
{
	[RequireComponent(typeof(Rigidbody2D))]
	public class Demo2DPlayerController : MonoBehaviour, IInteractor
	{
		[Header("References")]
		public Animator animator;

		public Interaction2DFinder interactionFinder;

		[Header("Movement")]
		public float speed;

		public float jumpHeight = 2f;

		public float gravityScale = 3f;

		public float fallMultiplier = 2.5f;

		[Header("Ground Detection")]
		public int numberOfRays;

		public float raysSpacing;

		public LayerMask groundLayerMask;

		[Header("Audio")]
		public AudioClip jump;

		private Rigidbody2D _rigidBody2D;

		private BoxCollider2D _collider;

		private float _colliderDefaultXOffset;

		private const float RaysLength = 0.05f;

		private readonly int _horizontalVelocityAnimParamHash = Animator.StringToHash("HorizontalVelocity");

		private readonly int _verticalVelocityAnimParamHash = Animator.StringToHash("VerticalVelocity");

		private readonly int _isAirborneAnimParamHash = Animator.StringToHash("IsAirborne");

		private float _moveInput;

		private bool _jumpInput;

		private bool _interactInput;

		private float _moveDirection;

		private bool _isGrounded;

		private void Reset()
		{
			_rigidBody2D = GetComponent<Rigidbody2D>();
			_collider = GetComponent<BoxCollider2D>();
			_colliderDefaultXOffset = _collider.offset.x;
			_rigidBody2D.gravityScale = gravityScale;
		}

		private void Awake()
		{
			Reset();
			Application.targetFrameRate = Mathf.CeilToInt((float)Screen.currentResolution.refreshRateRatio.value);
			QualitySettings.vSyncCount = 2;
		}

		private void Update()
		{
			HandleInputs();
			Move();
			ProcessInteract();
			Animate();
		}

		private void HandleInputs()
		{
			_moveInput = Input.GetAxis("Horizontal");
			_jumpInput = Input.GetKeyDown(KeyCode.Space) || Input.GetButton("Fire2");
			_interactInput = Input.GetKeyDown(KeyCode.Return) || Input.GetButton("Fire1");
		}

		private void Move()
		{
			_rigidBody2D.linearVelocity = new Vector2(_moveInput * speed, _rigidBody2D.linearVelocity.y);
			_isGrounded = IsGrounded();
			TryJump();
			if (_rigidBody2D.linearVelocity.y < 0f)
			{
				_rigidBody2D.linearVelocity += Vector2.up * (Physics2D.gravity.y * (fallMultiplier - 1f) * Time.deltaTime);
			}
		}

		private bool IsGrounded()
		{
			float num = (float)(numberOfRays - 1) * raysSpacing;
			float num2 = base.transform.position.x - num / 2f + _collider.offset.x;
			for (int i = 0; i < numberOfRays; i++)
			{
				Vector2 vector = new Vector3(num2 + (float)i * raysSpacing, base.transform.position.y);
				Debug.DrawRay(vector, Vector2.down * 0.05f, Color.magenta);
				if (Physics2D.Raycast(vector, Vector2.down, 0.05f, groundLayerMask.value).collider != null)
				{
					return true;
				}
			}
			return false;
		}

		private void ProcessInteract()
		{
			if (_interactInput)
			{
				_interactInput = false;
				interactionFinder.TryInteract();
			}
		}

		private void TryJump()
		{
			if (_jumpInput && _isGrounded)
			{
				PlayJumpSFX();
				float y = Mathf.Sqrt(2f * Mathf.Abs(Physics2D.gravity.y * gravityScale) * jumpHeight);
				_rigidBody2D.linearVelocity = new Vector2(_rigidBody2D.linearVelocity.x, y);
			}
		}

		public Transform GetTransform()
		{
			return base.transform;
		}

		private void Animate()
		{
			if (_rigidBody2D.linearVelocity.x > 0f)
			{
				animator.transform.localScale = new Vector3(1f, 1f, 1f);
				_collider.offset = new Vector2(_colliderDefaultXOffset, _collider.offset.y);
			}
			else if (_rigidBody2D.linearVelocity.x < 0f)
			{
				animator.transform.localScale = new Vector3(1f, 1f, -1f);
				_collider.offset = new Vector2(0f - _colliderDefaultXOffset, _collider.offset.y);
			}
			animator.SetFloat(_horizontalVelocityAnimParamHash, Mathf.Abs(_rigidBody2D.linearVelocity.x));
			animator.SetFloat(_verticalVelocityAnimParamHash, _rigidBody2D.linearVelocity.y);
			animator.SetBool(_isAirborneAnimParamHash, !_isGrounded);
		}

		protected void PlayJumpSFX()
		{
			if (!(jump == null))
			{
				AudioSource.PlayClipAtPoint(jump, base.transform.position);
			}
		}

		private void OnGUI()
		{
			float value = Screen.width * Screen.height / 2073600;
			value = Mathf.Clamp(value, 1f, 1.75f);
			GUIStyle label = GUI.skin.label;
			label.fontSize = (int)(12f * value);
			GUI.Box(new Rect((float)Screen.width - 260f * value, 10f * value, 250f * value, 180f * value), "");
			GUI.Box(new Rect((float)Screen.width - 245f * value, 30f * value, 250f * value, 180f * value), "Controls", label);
			GUI.Label(new Rect((float)Screen.width - 245f * value, 60f * value, 250f * value, 30f * value), "Use Arrow Keys or WASD to Move.", label);
			GUI.Label(new Rect((float)Screen.width - 245f * value, 90f * value, 250f * value, 30f * value), "Use Space to Jump.", label);
			GUI.Label(new Rect((float)Screen.width - 245f * value, 120f * value, 250f * value, 30f * value), "Use Return to Interact.", label);
			GUI.Label(new Rect((float)Screen.width - 245f * value, 150f * value, 250f * value, 30f * value), "R to restart level.", label);
		}
	}
}
