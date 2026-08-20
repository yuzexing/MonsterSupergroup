using System.Collections.Generic;
using AstralShift.HellMaiden.UI.Quests;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.HUD
{
	public class MinimapUIView : MonoBehaviour
	{
		[SerializeField]
		private RectTransform minimapIconContainer;

		[SerializeField]
		private MapPointerManager mapPointerManager;

		[SerializeField]
		private CanvasGroup minimapCanvasGroup;

		[SerializeField]
		private Image gridImage;

		private readonly int MinimapGridPositionSID = Shader.PropertyToID("_Position");

		private Material _gridMaterialInstance;

		private void Start()
		{
			MinimapUIManager.Instance.RegisterMinimapUI(this);
			if ((bool)gridImage && (bool)gridImage.materialForRendering)
			{
				_gridMaterialInstance = gridImage.materialForRendering;
			}
		}

		private void OnDestroy()
		{
			MinimapUIManager.Instance?.UnRegisterMinimapUI(this);
		}

		public RectTransform GetMinimapIconContainer()
		{
			return minimapIconContainer;
		}

		public void ShowMinimap()
		{
			minimapCanvasGroup.alpha = 1f;
		}

		public void HideMinimap()
		{
			minimapCanvasGroup.alpha = 0f;
		}

		public void DeactivateMinimap()
		{
			base.gameObject.SetActive(value: false);
		}

		public void ActivateMinimap()
		{
			base.gameObject.SetActive(value: true);
		}

		public void FixedUpdate()
		{
			if (Time.frameCount % MinimapUIManager.Instance.FrameDivider == 0)
			{
				UpdateIcons();
				UpdateGrid();
			}
		}

		private void UpdateIcons()
		{
			IReadOnlyList<MinimapIcon> icons = MinimapUIManager.Instance.Icons;
			for (int num = icons.Count - 1; num >= 0; num--)
			{
				icons[num].OnUpdate();
			}
		}

		private void UpdateGrid()
		{
			if (!(_gridMaterialInstance == null))
			{
				Vector2 vector = MinimapUIManager.Instance.FollowTarget.position;
				float num = MinimapUIManager.Instance.HeightInUnits * 0.5f;
				_gridMaterialInstance.SetVector(MinimapGridPositionSID, vector / num);
			}
		}
	}
}
