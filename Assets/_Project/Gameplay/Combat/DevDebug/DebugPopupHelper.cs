using DamageNumbersPro;
using UnityEngine;

namespace AstralShift.HellMaiden.DevDebug
{
	public class DebugPopupHelper : MonoBehaviour
	{
		public static DebugPopupHelper Instance;

		[SerializeField]
		private DamageNumber floatingDebugText;

		private void Awake()
		{
			if (Instance == null)
			{
				Instance = this;
				return;
			}
			Debug.LogError("Developer Debug Manager instance already exists!");
			Object.Destroy(base.gameObject);
		}

		public void CreateFloatingDebugText(Vector3 spawnPosition, string text)
		{
			if (DeveloperDebug.ShowDebugText)
			{
				floatingDebugText.Spawn(spawnPosition, text);
			}
		}
	}
}
