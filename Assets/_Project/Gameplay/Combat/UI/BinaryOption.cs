using FMODUnity;
using UnityEngine;
using UnityEngine.Events;

namespace AstralShift.UI
{
	public class BinaryOption : MonoBehaviour
	{
		[Tooltip("Any Animator can be used as long as it only has two states and a bool parameter called State, Check example if needed")]
		public Animator animator;

		private static int param = Animator.StringToHash("State");

		public bool defaultState;

		private bool _state;

		public UnityEvent<int> onValueChanged;

		[SerializeField]
		private EventReference stateChangeSound;

		public void SetDefaultState(bool state)
		{
			defaultState = state;
			_state = state;
			animator.SetBool(param, defaultState);
		}

		public void ToggleState()
		{
			_state = !_state;
			animator.SetBool(param, _state);
			if (!stateChangeSound.IsNull)
			{
				RuntimeManager.PlayOneShot(stateChangeSound);
			}
			onValueChanged.Invoke(_state ? 1 : 0);
		}

		internal bool GetState()
		{
			return _state;
		}

		private void OnEnable()
		{
			animator.SetBool(param, _state);
		}
	}
}
