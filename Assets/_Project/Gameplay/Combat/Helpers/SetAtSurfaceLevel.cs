using AstralShift.HellMaiden;
using UnityEngine;

namespace AstralShift.Helpers
{
	public class SetAtSurfaceLevel : MonoBehaviour
	{
		[SerializeField]
		private bool updateOnTick;

		private void Start()
		{
			float z = GameDirector.Instance.Player.CurrentPosition.z;
			base.gameObject.transform.position = new Vector3(base.gameObject.transform.position.x, base.gameObject.transform.position.y, z);
		}

		private void LateUpdate()
		{
			if (updateOnTick)
			{
				float z = GameDirector.Instance.Player.CurrentPosition.z;
				base.gameObject.transform.position = new Vector3(base.transform.parent.position.x, base.transform.parent.position.y, z);
			}
		}
	}
}
