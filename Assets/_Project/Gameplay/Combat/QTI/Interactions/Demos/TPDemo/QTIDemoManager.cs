using UnityEngine;
using UnityEngine.SceneManagement;

namespace AstralShift.QTI.Interactions.Demos.TPDemo
{
	public class QTIDemoManager : MonoBehaviour
	{
		public bool enableMouseCursor;

		private const string showcase = "ShowcaseDemo";

		private const string thirdPerson = "ThirdPersonDemo";

		private const string firstPerson = "FirstPersonPuzzleDemo";

		private const string platformer2D = "Platformer2D";

		private void Awake()
		{
			if (enableMouseCursor)
			{
				Cursor.visible = true;
				Cursor.lockState = CursorLockMode.None;
			}
			else
			{
				Cursor.visible = false;
				Cursor.lockState = CursorLockMode.Locked;
			}
		}

		private void Update()
		{
			if (Input.GetButtonDown("Cancel") || Input.GetKeyDown(KeyCode.R))
			{
				RestartScene();
			}
		}

		public void RestartScene()
		{
			SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
		}
	}
}
