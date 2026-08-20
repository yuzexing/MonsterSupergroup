using AstralShift.QTI.Interactors;
using UnityEngine;

namespace AstralShift.QTI.Interactions.Demos.TPDemo
{
	[RequireComponent(typeof(Animator))]
	[RequireComponent(typeof(CapsuleCollider))]
	[RequireComponent(typeof(Rigidbody))]
	public class PlayerControllerDemo : MonoBehaviour, IInteractor, IDamageable
	{
		public float moveSpeed = 10f;

		public float rotateSpeed = 2f;

		public int hp = 100;

		[SerializeField]
		private InteractionFinder interactor;

		private Rigidbody rb;

		private Animator anim;

		private Transform _cameraTransform;

		private int damageState = Animator.StringToHash("Base Layer.Damage");

		[SerializeField]
		private bool hasInteractions;

		private void Start()
		{
			anim = GetComponent<Animator>();
			rb = GetComponent<Rigidbody>();
			_cameraTransform = GameObject.FindWithTag("MainCamera").transform;
			anim.speed = 1.5f;
		}

		private void Update()
		{
			if (Input.GetButtonDown("Jump"))
			{
				interactor.TryInteract();
			}
		}

		private void FixedUpdate()
		{
			if (anim.GetCurrentAnimatorStateInfo(0).fullPathHash != damageState)
			{
				float axis = Input.GetAxis("Horizontal");
				float axis2 = Input.GetAxis("Vertical");
				Vector3 normalized = Vector3.ProjectOnPlane(_cameraTransform.forward, Vector3.up).normalized;
				Vector3 vector = Vector3.ProjectOnPlane(_cameraTransform.right, Vector3.up).normalized * axis + normalized * axis2;
				Vector3 vector2 = Vector3.Slerp(base.transform.forward, vector.normalized, rotateSpeed * Time.fixedDeltaTime);
				base.transform.forward = vector2;
				if (vector.magnitude > 1f)
				{
					vector.Normalize();
				}
				Vector3 position = base.transform.position + base.transform.forward * (vector.magnitude * moveSpeed * Time.fixedDeltaTime);
				rb.MovePosition(position);
				anim.SetFloat("Speed", vector.magnitude);
				anim.SetFloat("Direction", (vector2 - base.transform.forward).magnitude);
			}
		}

		public void TakeDamage(int dmg)
		{
			hp -= dmg;
			anim.SetTrigger("Damage");
		}

		public Transform GetTransform()
		{
			return base.transform;
		}

		private void OnGUI()
		{
			float value = Screen.width * Screen.height / 2073600;
			value = Mathf.Clamp(value, 1f, 1.75f);
			GUIStyle label = GUI.skin.label;
			label.fontSize = (int)(12f * value);
			GUI.Box(new Rect((float)Screen.width - 260f * value, 10f * value, 250f * value, 150f * value), "");
			GUI.Label(new Rect((float)Screen.width - 245f * value, 30f * value, 250f * value, 30f * value), "Controls", label);
			GUI.Label(new Rect((float)Screen.width - 245f * value, 60f * value, 250f * value, 30f * value), "Use Arrow Keys or WASD to move.", label);
			GUI.Label(new Rect((float)Screen.width - 245f * value, 90f * value, 250f * value, 30f * value), "R to restart level.", label);
			if (hasInteractions)
			{
				GUI.Label(new Rect((float)Screen.width - 245f * value, 120f * value, 250f * value, 30f * value), "SPACE to interact.", label);
			}
		}
	}
}
