using System.Threading.Tasks;
using UnityEngine;

namespace AstralShift.SceneLoading
{
	public abstract class LoadingScreen : MonoBehaviour
	{
		public Canvas canvas;

		public Camera camera;

		public void Awake()
		{
			base.gameObject.SetActive(value: false);
		}

		public abstract Task Run();

		public abstract Task Stop();
	}
}
