using AstralShift.HellMaiden.UI.Cards;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI
{
	[RequireComponent(typeof(RawImage))]
	public class UIGeneric3DRenderTarget : MonoBehaviour
	{
		[SerializeField]
		[HideInInspector]
		private RawImage _rawImage;

		[SerializeField]
		private bool initializeOnAwake = true;

		[SerializeField]
		protected bool usePrefab = true;

		[SerializeField]
		protected UIGeneric3DRenderer prefab;

		[Header("Render Texture Settings")]
		[SerializeField]
		protected Vector2Int resolution = new Vector2Int(512, 512);

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
		protected int fieldOfView = 60;

		[SerializeField]
		protected float nearPlane = 0.3f;

		[SerializeField]
		protected float farPlane = 50f;

		[SerializeField]
		protected float distanceToCamera = 6f;

		private RenderTexture _renderTexture;

		public RawImage RawImage => _rawImage;

		public bool UsePrefab => usePrefab;

		public UIGeneric3DRenderer Prefab => prefab;

		public UIGeneric3DRenderer Renderer => UICardRenderingManager.Instance.GetRenderer(this);

		public RenderTexture RenderTexture => _renderTexture;

		public void Reset()
		{
			TryGetComponent<RawImage>(out _rawImage);
		}

		private void Awake()
		{
			TryGetComponent<RawImage>(out _rawImage);
			_rawImage.material = new Material(_rawImage.material);
			if (initializeOnAwake)
			{
				Init();
			}
		}

		public void Init()
		{
			if ((bool)prefab && usePrefab)
			{
				UICardRenderingManager.Instance.RegisterRenderer(this);
			}
		}

		public void Init(UIGeneric3DRenderer instance)
		{
			UICardRenderingManager.Instance.RegisterRenderer(this, instance);
		}

		public UniTask InitAsync()
		{
			if ((bool)prefab && usePrefab)
			{
				return UICardRenderingManager.Instance.RegisterRendererAsync(this);
			}
			return UniTask.CompletedTask;
		}

		public RenderTextureDescriptor GetRenderTextureDescriptor()
		{
			RenderTextureDescriptor result = new RenderTextureDescriptor(resolution.x, resolution.y, colorFormat, stencilFormat);
			result.msaaSamples = antialiasing;
			result.shadowSamplingMode = shadowSamplingMode;
			return result;
		}

		public Matrix4x4 GetCameraWorldToCameraMatrix()
		{
			return Matrix4x4.TRS(Renderer.Transform.position - Vector3.forward * distanceToCamera, Quaternion.identity, new Vector3(1f, 1f, -1f));
		}

		public Matrix4x4 GetCameraProjectionMatrix()
		{
			return Matrix4x4.Perspective(fieldOfView, (float)resolution.x / (float)resolution.y, nearPlane, farPlane);
		}

		public Material GetMaterial()
		{
			if (!_rawImage)
			{
				return null;
			}
			return _rawImage.materialForRendering;
		}

		public void BindTexture(RenderTexture renderTexture)
		{
			_renderTexture = renderTexture;
			_rawImage.texture = renderTexture;
		}

		public void Release()
		{
			UICardRenderingManager.Instance?.UnRegisterRenderer(this);
		}

		private void OnDestroy()
		{
			Release();
			Object.Destroy(_rawImage.material);
		}
	}
}
