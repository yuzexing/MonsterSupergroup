using System;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.Managers;
using AstralShift.Rendering;
using Cysharp.Threading.Tasks;
using I2.Loc;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace AstralShift.HellMaiden.UI.Cards
{
	[DefaultExecutionOrder(100000)]
	public class UICardRenderingManager : MonoBehaviour
	{
		public static UICardRenderingManager Instance;

		[Header("Camera")]
		[SerializeField]
		protected Camera cardsCamera;

		[SerializeField]
		protected int fieldOfView = 50;

		[SerializeField]
		protected float nearPlane = 0.3f;

		[SerializeField]
		protected float farPlane = 50f;

		[SerializeField]
		protected float distanceToCamera = 6f;

		[Header("Render Texture Template")]
		[SerializeField]
		protected Vector2Int resolution = new Vector2Int(924, 1526);

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

		[Space]
		[SerializeField]
		protected Camera genericCamera;

		private Transform _genericPivot;

		private Transform _dynamicCardsParent;

		private Transform _staticCardsParent;

		private Dictionary<UICardViewHandler, UICard3DView> _cardViewHandlerTo3DViewLut;

		private Dictionary<UICard3DView, UICardViewHandler> _card3DViewToViewHandlerLut;

		private Dictionary<UICard3DView, RenderTexture> _cardsDynamicTextures;

		private Dictionary<RuntimeCardData, UICard3DView> _dataTo3DViewLut;

		private Dictionary<RuntimeCardData, UICard3DView> _staticCardsDataToView;

		private Dictionary<RuntimeCardData, RenderTexture[]> _cardsStaticTextures;

		private RuntimeCardDataEqualityComparer _dataEqualityComparer;

		private List<UICard3DView> _cardsList;

		private Queue<Action> _forceRenderQueue;

		private Matrix4x4 _cardsProjectionMatrix;

		private Material _blitHalfMaterial;

		private const string BlitShaderName = "Hidden/AstralShift/RenderingManagerRTBlit";

		private readonly int BlitGaussianStrengthPropID = Shader.PropertyToID("_GaussianStrength");

		private float _currentResolutionFactor;

		private Dictionary<UIGeneric3DRenderTarget, UIGeneric3DRenderer> _targetToRendererLUT;

		private Dictionary<UIGeneric3DRenderer, UIGeneric3DRenderTarget> _rendererToTargetLUT;

		private List<UIGeneric3DRenderer> _rendererList;

		private Dictionary<UIGeneric3DRenderTarget, RenderTexture> _genericDynamicTextures;

		private Dictionary<UIGeneric3DRenderTarget, RenderTexture> _genericStaticTextures;

		public Camera CardsCamera => cardsCamera;

		public Camera GenericCamera => genericCamera;

		public Transform GenericPivot
		{
			get
			{
				if (_genericPivot == null)
				{
					_genericPivot = new GameObject("Renderers").transform;
					_genericPivot.SetParent(base.transform);
					_genericPivot.localPosition = new Vector3(0f, 0f, 1f);
				}
				return _genericPivot;
			}
		}

		public Transform DynamicCardsParent
		{
			get
			{
				if (_dynamicCardsParent == null)
				{
					_dynamicCardsParent = new GameObject("Dynamic Cards").transform;
					_dynamicCardsParent.SetParent(base.transform);
					_dynamicCardsParent.localPosition = new Vector3(0f, 0f, 1f);
				}
				return _dynamicCardsParent;
			}
		}

		public Transform StaticCardsParent
		{
			get
			{
				if (_staticCardsParent == null)
				{
					_staticCardsParent = new GameObject("Static Cards").transform;
					_staticCardsParent.SetParent(base.transform);
					_staticCardsParent.localPosition = new Vector3(0f, 0f, 1f);
				}
				return _staticCardsParent;
			}
		}

		public Dictionary<RuntimeCardData, RenderTexture[]> CardsStaticTextures => _cardsStaticTextures;

		public Material BlitHalfResMaterial => _blitHalfMaterial;

		public float InternalResolutionFactor => Mathf.Clamp((float)Screen.height / 2160f, ResolutionFactorLowerBound, ResolutionFactorUpperBound);

		public float ResolutionFactorLowerBound => 0.66f;

		public float ResolutionFactorUpperBound => 1f;

		private bool IsInitialized { get; set; }

		public void Init()
		{
			if (!Instance)
			{
				Instance = this;
			}
			_ = GenericPivot;
			_ = DynamicCardsParent;
			_ = StaticCardsParent;
			_cardsProjectionMatrix = CardsCamera.projectionMatrix;
			_cardViewHandlerTo3DViewLut = new Dictionary<UICardViewHandler, UICard3DView>();
			_card3DViewToViewHandlerLut = new Dictionary<UICard3DView, UICardViewHandler>();
			_cardsList = new List<UICard3DView>();
			_forceRenderQueue = new Queue<Action>();
			_cardsDynamicTextures = new Dictionary<UICard3DView, RenderTexture>();
			_staticCardsDataToView = new Dictionary<RuntimeCardData, UICard3DView>(new RuntimeCardDataEqualityComparer());
			_cardsStaticTextures = new Dictionary<RuntimeCardData, RenderTexture[]>(new RuntimeCardDataEqualityComparer());
			_targetToRendererLUT = new Dictionary<UIGeneric3DRenderTarget, UIGeneric3DRenderer>();
			_rendererToTargetLUT = new Dictionary<UIGeneric3DRenderer, UIGeneric3DRenderTarget>();
			_genericDynamicTextures = new Dictionary<UIGeneric3DRenderTarget, RenderTexture>();
			_genericStaticTextures = new Dictionary<UIGeneric3DRenderTarget, RenderTexture>();
			_rendererList = new List<UIGeneric3DRenderer>();
			cardsCamera.enabled = false;
			genericCamera.enabled = false;
			SetupBlitMaterials();
			GameDirector.Instance.Settings.OnResolutionChanged += ResizeAllDynamicTextures;
			GameDirector.Instance.Settings.OnResolutionChanged += ResizeAllCardStaticTextures;
			LocalizationManager.OnLocalizeEvent += RefreshAllCardStaticTextures;
			IsInitialized = true;
		}

		private void SetupBlitMaterials()
		{
			_blitHalfMaterial = new Material(Shader.Find("Hidden/AstralShift/RenderingManagerRTBlit"));
			_blitHalfMaterial.SetFloat(BlitGaussianStrengthPropID, 2f);
		}

		private void LateUpdate()
		{
			if (IsInitialized)
			{
				TryRenderGenericDynamicTextures();
				TryRenderCardsDynamicTextures();
				ProcessCardsRenderQueue();
			}
		}

		public void AddCard(UICardViewHandler cardViewHandler, UICard3DView card3DView)
		{
			if (!_cardViewHandlerTo3DViewLut.ContainsKey(cardViewHandler))
			{
				card3DView.Initialize(cardViewHandler.CardView.Card3DProxy);
				card3DView.name = cardViewHandler.RuntimeCardData.BaseData.Title + " (3D Card View)";
				card3DView.transform.SetParent(DynamicCardsParent);
				card3DView.transform.localPosition = Vector3.zero;
				_cardViewHandlerTo3DViewLut.Add(cardViewHandler, card3DView);
				_card3DViewToViewHandlerLut.Add(card3DView, cardViewHandler);
				_cardsList.Add(card3DView);
				CreateCardDynamicTexture(cardViewHandler, card3DView);
				cardViewHandler.CardView.Card3DProxy.BindDynamicTexture();
				card3DView.EnableVisibility(state: false);
			}
		}

		public void RemoveCard(UICardViewHandler cardViewHandler)
		{
			if (_cardViewHandlerTo3DViewLut.TryGetValue(cardViewHandler, out var value))
			{
				if ((bool)value)
				{
					_cardsList.Remove(value);
					_card3DViewToViewHandlerLut.Remove(value);
					DestroyCardDynamicTexture(value);
					UnityEngine.Object.Destroy(value.gameObject);
				}
				_cardViewHandlerTo3DViewLut.Remove(cardViewHandler);
			}
		}

		public UICard3DView GetCard3DView(UICardViewHandler card)
		{
			if ((bool)card)
			{
				return _cardViewHandlerTo3DViewLut?.GetValueOrDefault(card);
			}
			return null;
		}

		private void CreateCardDynamicTexture(UICardViewHandler cardViewHandler, UICard3DView card3DView)
		{
			RenderTexture renderTexture = new RenderTexture(cardViewHandler.CardView.Card3DProxy.GetRenderTextureDescriptor());
			renderTexture.name = "UI Card Dynamic Texture - " + cardViewHandler.RuntimeCardData.BaseData.Title;
			renderTexture.filterMode = FilterMode.Bilinear;
			renderTexture.Create();
			card3DView.AssignTexture(renderTexture);
			_cardsDynamicTextures.Add(card3DView, renderTexture);
		}

		private void ResizeCardDynamicTexture(UICardViewHandler cardViewHandler)
		{
			if (_cardViewHandlerTo3DViewLut.TryGetValue(cardViewHandler, out var value) && _cardsDynamicTextures.TryGetValue(value, out var value2))
			{
				value2.Release();
				value2.DiscardContents(discardColor: true, discardDepth: true);
				UnityEngine.Object.Destroy(value2);
				RenderTexture renderTexture = new RenderTexture(cardViewHandler.CardView.Card3DProxy.GetRenderTextureDescriptor());
				renderTexture.name = "UI Card Dynamic Texture - " + cardViewHandler.RuntimeCardData.BaseData.Title;
				renderTexture.filterMode = FilterMode.Bilinear;
				renderTexture.Create();
				_cardsDynamicTextures[value] = renderTexture;
				value.AssignTexture(renderTexture);
				cardViewHandler.CardView.Card3DProxy.BindDynamicTexture();
				cardViewHandler.CardView.EnqueueRender();
			}
		}

		public void ResizeAllDynamicTextures()
		{
			if (_cardViewHandlerTo3DViewLut == null)
			{
				return;
			}
			foreach (KeyValuePair<UICardViewHandler, UICard3DView> item in _cardViewHandlerTo3DViewLut)
			{
				ResizeCardDynamicTexture(item.Key);
			}
		}

		public RenderTexture GetCardDynamicTexture(UICardViewHandler cardViewHandler)
		{
			if (_cardViewHandlerTo3DViewLut.TryGetValue(cardViewHandler, out var value))
			{
				return _cardsDynamicTextures.GetValueOrDefault(value, null);
			}
			return null;
		}

		private void DestroyCardDynamicTexture(UICard3DView card3DView)
		{
			if (_cardsDynamicTextures.Remove(card3DView, out var value))
			{
				value.Release();
				value.DiscardContents(discardColor: true, discardDepth: true);
				UnityEngine.Object.Destroy(value);
				value = null;
			}
		}

		private void DestroyAllCardDynamicTextures()
		{
			foreach (KeyValuePair<UICard3DView, RenderTexture> cardsDynamicTexture in _cardsDynamicTextures)
			{
				cardsDynamicTexture.Value.Release();
				cardsDynamicTexture.Value.DiscardContents(discardColor: true, discardDepth: true);
				UnityEngine.Object.Destroy(cardsDynamicTexture.Value);
			}
			_cardsDynamicTextures.Clear();
			_cardsList.Clear();
		}

		private int GetStaticTextureIndex(ResolutionFactorEnum resolutionFactor)
		{
			if (resolutionFactor != ResolutionFactorEnum.Half)
			{
				_ = 100;
				return 1;
			}
			return 2;
		}

		public RenderTexture GetOrCreateCardStaticTextureByIndex(RuntimeCardData data, int index, Action onRenderFinished = null, bool inEditor = false)
		{
			if (_cardsStaticTextures.TryGetValue(data, out var value))
			{
				onRenderFinished?.Invoke();
				return value[index];
			}
			value = CreateCardStaticTexture(data, onRenderFinished, inEditor);
			return value[index];
		}

		public RenderTexture GetOrCreateCardStaticTextureByResFactor(RuntimeCardData data, ResolutionFactorEnum resolutionFactor, Action onAfterRender = null, bool inEditor = false)
		{
			int staticTextureIndex = GetStaticTextureIndex(resolutionFactor);
			return GetOrCreateCardStaticTextureByIndex(data, staticTextureIndex, onAfterRender, inEditor);
		}

		public RenderTexture[] CreateCardStaticTexture(RuntimeCardData data, Action onRenderFinished = null, bool inEditor = false)
		{
			if (data == null)
			{
				return null;
			}
			if (!(data.Clone() is RuntimeCardData runtimeCardData))
			{
				return null;
			}
			if (_cardsStaticTextures.ContainsKey(runtimeCardData))
			{
				return null;
			}
			string text = "UI Card Static Texture - " + data.BaseData.Title + " Lvl: " + (data.LevelIndex + 1);
			RenderTexture[] array = new RenderTexture[3];
			_cardsStaticTextures.Add(runtimeCardData, array);
			RenderTextureDescriptor staticRenderTextureDescriptor = GetStaticRenderTextureDescriptor();
			RenderTextureDescriptor desc = staticRenderTextureDescriptor;
			desc.width = (int)((float)staticRenderTextureDescriptor.width * InternalResolutionFactor);
			desc.height = (int)((float)staticRenderTextureDescriptor.height * InternalResolutionFactor);
			RenderTexture renderTexture = new RenderTexture(desc);
			renderTexture.name = text + " (Dynamic Res)";
			renderTexture.filterMode = FilterMode.Bilinear;
			renderTexture.Create();
			array[0] = renderTexture;
			RenderTexture renderTexture2 = new RenderTexture(staticRenderTextureDescriptor);
			renderTexture2.name = text + " (Full Res)";
			renderTexture2.filterMode = FilterMode.Bilinear;
			renderTexture2.Create();
			array[1] = renderTexture2;
			staticRenderTextureDescriptor.width /= 2;
			staticRenderTextureDescriptor.height /= 2;
			RenderTexture renderTexture3 = new RenderTexture(staticRenderTextureDescriptor);
			renderTexture3.name = text + " (Half Res)";
			renderTexture3.filterMode = FilterMode.Bilinear;
			renderTexture3.Create();
			array[2] = renderTexture3;
			CreateCardAndQueueStaticRender(runtimeCardData, onRenderFinished, inEditor);
			return array;
		}

		private async void CreateCardAndQueueStaticRender(RuntimeCardData data, Action onRenderFinished = null, bool inEditor = false)
		{
			UICard3DView card3DView;
			if (_cardsStaticTextures.TryGetValue(data, out var renderTextures))
			{
				if (!_staticCardsDataToView.TryGetValue(data, out card3DView))
				{
					card3DView = await CardVisualsFactory.GetCard3DView(data, StaticCardsParent);
					_staticCardsDataToView.Add(data, card3DView);
				}
				card3DView.transform.SetParent(StaticCardsParent);
				card3DView.transform.localPosition = Vector3.zero;
				await UniTask.DelayFrame(2, PlayerLoopTiming.PostLateUpdate);
				_forceRenderQueue.Enqueue(Render);
			}
			void Render()
			{
				card3DView.RenderStatic(CardsCamera, renderTextures);
				onRenderFinished?.Invoke();
			}
		}

		public async void ResizeCardStaticTexture(RuntimeCardData data)
		{
			UICard3DView card3DView;
			if (data != null && data.Clone() is RuntimeCardData runtimeCardData && _cardsStaticTextures.TryGetValue(runtimeCardData, out var renderTextures))
			{
				UnityEngine.Object.Destroy(renderTextures[0]);
				string text = "UI Card Static Texture - " + data.BaseData.Title + " Lvl: " + (data.LevelIndex + 1);
				RenderTextureDescriptor staticRenderTextureDescriptor = GetStaticRenderTextureDescriptor();
				staticRenderTextureDescriptor.width = (int)((float)staticRenderTextureDescriptor.width * InternalResolutionFactor);
				staticRenderTextureDescriptor.height = (int)((float)staticRenderTextureDescriptor.height * InternalResolutionFactor);
				RenderTexture renderTexture = new RenderTexture(staticRenderTextureDescriptor);
				renderTexture.name = text + " (Dynamic Res)";
				renderTexture.filterMode = FilterMode.Point;
				renderTexture.Create();
				renderTextures[0] = renderTexture;
				card3DView = _staticCardsDataToView[runtimeCardData];
				await CardVisualsFactory.RefreshCard3DViewText(card3DView, runtimeCardData);
				await UniTask.NextFrame(PlayerLoopTiming.PostLateUpdate);
				_forceRenderQueue.Enqueue(Render);
			}
			void Render()
			{
				card3DView.RenderStatic(CardsCamera, renderTextures, renderOnlyDynamicRes: true);
			}
		}

		public async void RefreshCardStaticTexture(RuntimeCardData data, bool refreshOnlyDynamicRes = false)
		{
			UICard3DView card3DView;
			if (data != null && data.Clone() is RuntimeCardData runtimeCardData && _cardsStaticTextures.TryGetValue(runtimeCardData, out var renderTextures))
			{
				card3DView = _staticCardsDataToView[runtimeCardData];
				await CardVisualsFactory.RefreshCard3DViewText(card3DView, runtimeCardData);
				await UniTask.NextFrame(PlayerLoopTiming.PostLateUpdate);
				_forceRenderQueue.Enqueue(Render);
			}
			void Render()
			{
				card3DView.RenderStatic(CardsCamera, renderTextures, refreshOnlyDynamicRes);
			}
		}

		public void EnqueueRender(UICard3DView card3DView)
		{
			_forceRenderQueue.Enqueue(Render);
			void Render()
			{
				card3DView.Render(CardsCamera);
			}
		}

		public void ResizeAllCardStaticTextures()
		{
			if (_staticCardsDataToView == null)
			{
				return;
			}
			foreach (KeyValuePair<RuntimeCardData, UICard3DView> item in _staticCardsDataToView)
			{
				ResizeCardStaticTexture(item.Key);
			}
		}

		public void RefreshAllCardStaticTextures()
		{
			if (_staticCardsDataToView == null)
			{
				return;
			}
			foreach (KeyValuePair<RuntimeCardData, UICard3DView> item in _staticCardsDataToView)
			{
				RefreshCardStaticTexture(item.Key);
			}
		}

		public RenderTextureDescriptor GetStaticRenderTextureDescriptor()
		{
			RenderTextureDescriptor result = new RenderTextureDescriptor(resolution.x, resolution.y, colorFormat, stencilFormat);
			result.msaaSamples = 1;
			result.shadowSamplingMode = shadowSamplingMode;
			return result;
		}

		public Matrix4x4 GetStaticCardWorldToCameraMatrix(UICard3DView card3DView)
		{
			return Matrix4x4.TRS(card3DView.transform.position - Vector3.forward * distanceToCamera, Quaternion.identity, new Vector3(1f, 1f, -1f));
		}

		public Matrix4x4 GetStaticCardCameraProjectionMatrix()
		{
			return Matrix4x4.Perspective(fieldOfView, (float)resolution.x / (float)resolution.y, nearPlane, farPlane);
		}

		private void DestroyCardStaticTexture(RuntimeCardData data)
		{
			if (_cardsStaticTextures.Remove(data, out var value))
			{
				if (_staticCardsDataToView.Remove(data, out var value2))
				{
					UnityEngine.Object.Destroy(value2.gameObject);
				}
				for (int num = value.Length - 1; num >= 0; num--)
				{
					value[num].Release();
					value[num].DiscardContents(discardColor: true, discardDepth: true);
					UnityEngine.Object.Destroy(value[num]);
					value[num] = null;
				}
			}
		}

		public void DisposeUnusedStaticTextures()
		{
			if (PlayerHand.Instance == null)
			{
				return;
			}
			HashSet<RuntimeCardData> usedCards = new HashSet<RuntimeCardData>(new RuntimeCardDataEqualityComparer());
			foreach (PlayerHandSlot slot in PlayerHand.Instance.Slots)
			{
				if (slot.RuntimeWeaponData != null)
				{
					usedCards.Add(slot.RuntimeWeaponData);
				}
				if (slot.Equipments != null && slot.Equipments.Count > 0)
				{
					usedCards.UnionWith(slot.Equipments);
				}
			}
			foreach (RuntimeCardData item in _staticCardsDataToView.Keys.Where((RuntimeCardData data) => !usedCards.Contains(data)).ToList())
			{
				DestroyCardStaticTexture(item);
			}
		}

		private void DestroyAllCardStaticTextures()
		{
			foreach (KeyValuePair<RuntimeCardData, RenderTexture[]> cardsStaticTexture in _cardsStaticTextures)
			{
				for (int num = cardsStaticTexture.Value.Length - 1; num >= 0; num--)
				{
					cardsStaticTexture.Value[num].Release();
					cardsStaticTexture.Value[num].DiscardContents(discardColor: true, discardDepth: true);
					UnityEngine.Object.Destroy(cardsStaticTexture.Value[num]);
					cardsStaticTexture.Value[num] = null;
				}
			}
			_cardsStaticTextures.Clear();
		}

		private void TryRenderCardsDynamicTextures()
		{
			if ((ControllerManager.Instance.CurrentController is CardPickMenuController || ControllerManager.Instance.CurrentController is WeaponSelectionMenuController) && _cardsList.Count != 0)
			{
				for (int num = _cardsList.Count - 1; num >= 0; num--)
				{
					CardsCamera.projectionMatrix = _cardsProjectionMatrix;
					_cardsList[num].TryRender(CardsCamera);
				}
			}
		}

		private void ProcessCardsRenderQueue()
		{
			while (_forceRenderQueue.Count > 0)
			{
				_forceRenderQueue.Dequeue()?.Invoke();
			}
		}

		public async void RegisterRenderer(UIGeneric3DRenderTarget renderTarget)
		{
			if (!_targetToRendererLUT.ContainsKey(renderTarget))
			{
				UIGeneric3DRenderer renderer = UnityEngine.Object.Instantiate(renderTarget.Prefab, GenericPivot.position, Quaternion.identity, GenericPivot);
				renderer.Init(renderTarget);
				_targetToRendererLUT.Add(renderTarget, renderer);
				_rendererToTargetLUT.Add(renderer, renderTarget);
				RenderTexture renderTexture = CreateGenericDynamicTexture(renderTarget);
				renderTarget.BindTexture(renderTexture);
				await UniTask.NextFrame(PlayerLoopTiming.PreUpdate);
				_rendererList.Add(renderer);
			}
		}

		public async void RegisterRenderer(UIGeneric3DRenderTarget renderTarget, UIGeneric3DRenderer renderer)
		{
			if (!_targetToRendererLUT.ContainsKey(renderTarget))
			{
				renderer.Transform.SetParent(GenericPivot);
				renderer.Transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
				renderer.Transform.localScale = Vector3.one;
				renderer.Init(renderTarget);
				_targetToRendererLUT.Add(renderTarget, renderer);
				_rendererToTargetLUT.Add(renderer, renderTarget);
				RenderTexture renderTexture = CreateGenericDynamicTexture(renderTarget);
				renderTarget.BindTexture(renderTexture);
				await UniTask.NextFrame(PlayerLoopTiming.PreUpdate);
				_rendererList.Add(renderer);
			}
		}

		public async UniTask RegisterRendererAsync(UIGeneric3DRenderTarget renderTarget)
		{
			if (!_targetToRendererLUT.ContainsKey(renderTarget))
			{
				AsyncInstantiateOperation<UIGeneric3DRenderer> asyncInstantiateOperation = UnityEngine.Object.InstantiateAsync(renderTarget.Prefab, GenericPivot, GenericPivot.position, Quaternion.identity);
				await asyncInstantiateOperation;
				UIGeneric3DRenderer renderer = asyncInstantiateOperation.Result[0];
				renderer.Init(renderTarget);
				_targetToRendererLUT.Add(renderTarget, renderer);
				_rendererToTargetLUT.Add(renderer, renderTarget);
				RenderTexture renderTexture = CreateGenericDynamicTexture(renderTarget);
				renderTarget.BindTexture(renderTexture);
				await UniTask.NextFrame(PlayerLoopTiming.PreUpdate);
				_rendererList.Add(renderer);
			}
		}

		public async void RegisterRendererAsync(UIGeneric3DRenderTarget renderTarget, UIGeneric3DRenderer renderer)
		{
			if (!_targetToRendererLUT.ContainsKey(renderTarget))
			{
				renderer.Transform.SetParent(GenericPivot);
				renderer.Transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
				renderer.Transform.localScale = Vector3.one;
				renderer.Init(renderTarget);
				_targetToRendererLUT.Add(renderTarget, renderer);
				_rendererToTargetLUT.Add(renderer, renderTarget);
				RenderTexture renderTexture = CreateGenericDynamicTexture(renderTarget);
				renderTarget.BindTexture(renderTexture);
				await UniTask.NextFrame(PlayerLoopTiming.PreUpdate);
				_rendererList.Add(renderer);
			}
		}

		public void UnRegisterRenderer(UIGeneric3DRenderTarget renderTarget)
		{
			if (_targetToRendererLUT.Remove(renderTarget, out var value))
			{
				_rendererList.Remove(value);
				if ((bool)value)
				{
					DestroyGenericStaticTexture(renderTarget);
					DestroyGenericDynamicTexture(renderTarget);
					UnityEngine.Object.Destroy(value.gameObject);
				}
			}
		}

		public UIGeneric3DRenderer GetRenderer(UIGeneric3DRenderTarget renderTarget)
		{
			if ((bool)renderTarget)
			{
				return _targetToRendererLUT?.GetValueOrDefault(renderTarget);
			}
			return null;
		}

		public UIGeneric3DRenderTarget GetTarget(UIGeneric3DRenderer gameObject)
		{
			if ((bool)gameObject)
			{
				return _rendererToTargetLUT?.GetValueOrDefault(gameObject);
			}
			return null;
		}

		private RenderTexture CreateGenericDynamicTexture(UIGeneric3DRenderTarget renderTarget)
		{
			RenderTexture renderTexture = new RenderTexture(renderTarget.GetRenderTextureDescriptor());
			renderTexture.name = "UI3DRenderer - " + renderTarget.name + " (Dynamic Texture)";
			renderTexture.Create();
			_genericDynamicTextures.TryAdd(renderTarget, renderTexture);
			return renderTexture;
		}

		public void RefreshGenericDynamicTexture(UIGeneric3DRenderTarget renderTarget)
		{
			if (_targetToRendererLUT.ContainsKey(renderTarget))
			{
				RenderTexture genericDynamicTexture = GetGenericDynamicTexture(renderTarget);
				genericDynamicTexture.DiscardContents(discardColor: true, discardDepth: true);
				genericDynamicTexture.Release();
				genericDynamicTexture = CreateGenericDynamicTexture(renderTarget);
				_genericDynamicTextures[renderTarget] = genericDynamicTexture;
				renderTarget.BindTexture(genericDynamicTexture);
			}
		}

		public RenderTexture GetGenericDynamicTexture(UIGeneric3DRenderTarget renderTarget)
		{
			if (_targetToRendererLUT.TryGetValue(renderTarget, out var _))
			{
				return _genericDynamicTextures.GetValueOrDefault(renderTarget, null);
			}
			return null;
		}

		private void DestroyGenericDynamicTexture(UIGeneric3DRenderTarget renderTarget)
		{
			if (_genericDynamicTextures.Remove(renderTarget, out var value))
			{
				value.Release();
				value.DiscardContents(discardColor: true, discardDepth: true);
				UnityEngine.Object.Destroy(value);
				value = null;
			}
		}

		private void DestroyAllGenericDynamicTextures()
		{
			foreach (KeyValuePair<UIGeneric3DRenderTarget, RenderTexture> genericDynamicTexture in _genericDynamicTextures)
			{
				genericDynamicTexture.Value.Release();
				genericDynamicTexture.Value.DiscardContents(discardColor: true, discardDepth: true);
				UnityEngine.Object.Destroy(genericDynamicTexture.Value);
			}
			_genericDynamicTextures.Clear();
			_rendererList.Clear();
		}

		private void DestroyGenericStaticTexture(UIGeneric3DRenderTarget renderTarget)
		{
			if (_genericStaticTextures.Remove(renderTarget, out var value))
			{
				value.Release();
				value.DiscardContents(discardColor: true, discardDepth: true);
				UnityEngine.Object.Destroy(value);
				value = null;
			}
		}

		private void DestroyAllGenericStaticTextures()
		{
			foreach (KeyValuePair<UIGeneric3DRenderTarget, RenderTexture> genericStaticTexture in _genericStaticTextures)
			{
				genericStaticTexture.Value.Release();
				genericStaticTexture.Value.DiscardContents(discardColor: true, discardDepth: true);
				UnityEngine.Object.Destroy(genericStaticTexture.Value);
			}
			_genericStaticTextures.Clear();
		}

		public RenderTexture GetGenericStaticTexture(UIGeneric3DRenderTarget renderTarget)
		{
			return _genericStaticTextures.GetValueOrDefault(renderTarget, null);
		}

		private void TryRenderGenericDynamicTextures()
		{
			if (_rendererList.Count != 0)
			{
				for (int num = _rendererList.Count - 1; num >= 0; num--)
				{
					_rendererList[num].TryRender(GenericCamera);
				}
			}
		}

		private void OnDestroy()
		{
			GameDirector.Instance.Settings.OnResolutionChanged -= ResizeAllCardStaticTextures;
			LocalizationManager.OnLocalizeEvent -= RefreshAllCardStaticTextures;
			UnityEngine.Object.Destroy(_blitHalfMaterial);
			DestroyAllGenericDynamicTextures();
			DestroyAllGenericStaticTextures();
			DestroyAllCardDynamicTextures();
			DestroyAllCardStaticTextures();
		}
	}
}
