using AstralShift.HellMaiden.Scenes;
using UnityEngine;

namespace AstralShift.HellMaiden.UI.Area_Name_Popup
{
	public class UIAreaNamePopupCreator : MonoBehaviour
	{
		[SerializeField]
		private float defaultShowTime = 1f;

		[SerializeField]
		private UI_AreaNamePopup uiAreaNamePopupPrefab;

		private UI_AreaNamePopup _popupInstance;

		private float waitDuration = 1f;

		private void Start()
		{
			SceneMaster.Instance.OnSceneShowFinish += ShowFinishAreaNamePopup;
			SceneMaster.Instance.OnSceneHideStart += DestroyAreaNamePopup;
		}

		private void OnDestroy()
		{
			SceneMaster.Instance.OnSceneHideStart -= DestroyAreaNamePopup;
		}

		public void ShowFinishAreaNamePopup()
		{
			SceneMaster.Instance.OnSceneShowFinish -= ShowFinishAreaNamePopup;
			_popupInstance = Object.Instantiate(uiAreaNamePopupPrefab, base.transform);
			_popupInstance.ShowPopup();
		}

		private void DestroyAreaNamePopup()
		{
			if (_popupInstance != null)
			{
				Object.Destroy(_popupInstance.gameObject);
			}
		}
	}
}
