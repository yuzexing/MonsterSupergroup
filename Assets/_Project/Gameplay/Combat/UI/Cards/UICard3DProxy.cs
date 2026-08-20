using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI.Cards
{
	[RequireComponent(typeof(RawImage))]
	public class UICard3DProxy : MonoBehaviour
	{
		[SerializeField]
		[HideInInspector]
		private RawImage _rawImage;

		[Header("Render Texture Settings")]
		[SerializeField]
		protected Vector2Int resolution = new Vector2Int(924, 1528);

		[SerializeField]
		protected int antialiasing = 1;

		[SerializeField]
		protected GraphicsFormat colorFormat = GraphicsFormat.B8G8R8A8_UNorm;

		[SerializeField]
		protected GraphicsFormat stencilFormat = GraphicsFormat.D16_UNorm;

		[SerializeField]
		protected WrapMode wrapMode = WrapMode.ClampForever;

		[SerializeField]
		protected FilterMode filterMode = FilterMode.Bilinear;

		[SerializeField]
		protected ShadowSamplingMode shadowSamplingMode = ShadowSamplingMode.None;

		[Header("Camera Settings")]
		[SerializeField]
		protected int fieldOfView = 50;

		[SerializeField]
		protected float nearPlane = 0.3f;

		[SerializeField]
		protected float farPlane = 50f;

		[SerializeField]
		protected float distanceToCamera = 6f;

		private UICardViewHandler _cardViewHandler;

		public UICardViewHandler CardViewHandler => _cardViewHandler;

		public UICard3DView Card => UICardRenderingManager.Instance.GetCard3DView(_cardViewHandler);

		private float ResolutionScaleFactor => UICardRenderingManager.Instance.InternalResolutionFactor;

		public void Reset()
		{
			_rawImage = GetComponent<RawImage>();
		}

		public void Initialize(UICardViewHandler cardViewHandler)
		{
			if (!(_cardViewHandler != null))
			{
				_cardViewHandler = cardViewHandler;
				TryGetComponent<RawImage>(out _rawImage);
				_rawImage.material = new Material(_rawImage.material);
			}
		}

		public RenderTextureDescriptor GetRenderTextureDescriptor()
		{
			RenderTextureDescriptor result = new RenderTextureDescriptor((int)((float)resolution.x * ResolutionScaleFactor), (int)((float)resolution.y * ResolutionScaleFactor), colorFormat, stencilFormat);
			result.msaaSamples = 1;
			result.shadowSamplingMode = shadowSamplingMode;
			return result;
		}

		public Matrix4x4 GetWorldToCameraMatrix()
		{
			return Matrix4x4.TRS(Card.transform.position - Vector3.forward * distanceToCamera, Quaternion.identity, new Vector3(1f, 1f, -1f));
		}

		public Matrix4x4 GetCameraProjectionMatrix()
		{
			return Matrix4x4.Perspective(fieldOfView, (float)resolution.x / (float)resolution.y, nearPlane, farPlane);
		}

		public Material Get2DMaterial()
		{
			if ((bool)_rawImage)
			{
				return _rawImage.materialForRendering;
			}
			return null;
		}

		public void BindDynamicTexture()
		{
			_rawImage.texture = UICardRenderingManager.Instance.GetCardDynamicTexture(_cardViewHandler);
		}

		private void OnDestroy()
		{
			if ((bool)_rawImage && (bool)_rawImage.material)
			{
				Object.Destroy(_rawImage.material);
			}
		}
	}
}
