using UnityEngine;

namespace AstralShift.Initialization.Verification
{
	public class EventVerifier : MonoBehaviour
	{
		public bool Verify()
		{
			IEventCondition[] components = GetComponents<IEventCondition>();
			for (int i = 0; i < components.Length; i++)
			{
				if (!components[i].VerifyCondition())
				{
					Debug.Log("condition failed");
					return false;
				}
			}
			Debug.Log("condition passed");
			return true;
		}
	}
}
