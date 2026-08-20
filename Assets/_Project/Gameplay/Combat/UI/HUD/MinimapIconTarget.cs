using UnityEngine;

namespace AstralShift.HellMaiden.UI.HUD
{
	public class MinimapIconTarget : MonoBehaviour
	{
		[SerializeField]
		private bool autoInit = true;

		[SerializeField]
		private Transform target;

		[SerializeField]
		private Sprite iconSprite;

		[SerializeField]
		private float iconSize = 1f;

		[SerializeField]
		private MinimapIcon.PingMode pingMode;

		[SerializeField]
		private MinimapUIManager.MinimapIconType iconType;

		private MinimapIcon _minimapIcon;

		public void CreateIcon()
		{
			MinimapUIManager.Instance.RequestMinimapIcon(target, iconSprite, iconSize, pingMode, iconType, HandleIcon);
		}

		public void DisposeIcon()
		{
			_minimapIcon?.Release();
			_minimapIcon = null;
		}

		private void OnEnable()
		{
			if (autoInit)
			{
				CreateIcon();
			}
		}

		private void OnDisable()
		{
			if (autoInit)
			{
				DisposeIcon();
			}
		}

		private void HandleIcon(MinimapIcon icon)
		{
			_minimapIcon = icon;
		}
	}
}
