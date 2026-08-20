using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Data.Perks;
using AstralShift.HellMaiden.UI.Cards;
using AstralShift.HellMaiden.UI.Perks;
using AstralShift.Rendering;
using Coffee.UISoftMask;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.UI
{
	public class UICardOrPerkStaticElement : MonoBehaviour
	{
		[SerializeField]
		private GameObject renderTextureGroup;

		[SerializeField]
		private GameObject emptyVisualsGroup;

		[SerializeField]
		private SoftMask softMask;

		[SerializeField]
		private RawImage mask;

		[SerializeField]
		private RawImage renderImage;

		[SerializeField]
		private Image containerMask;

		[Space]
		[Header("Resolution Settings")]
		[SerializeField]
		private bool dynamicResolution;

		[SerializeField]
		private ResolutionFactorEnum resolutionFactor = ResolutionFactorEnum.Full;

		[Space]
		[SerializeField]
		private bool hideOnAwake = true;

		private RuntimeCardData _currentCardData;

		private Color _colorToApply;

		private void Awake()
		{
			if (hideOnAwake)
			{
				if ((bool)renderTextureGroup)
				{
					renderTextureGroup.SetActive(value: false);
				}
				if ((bool)emptyVisualsGroup)
				{
					emptyVisualsGroup.SetActive(value: true);
				}
			}
			GameDirector.Instance.Settings.OnResolutionChanged += RefreshCardVisuals;
		}

		private void OnDestroy()
		{
			GameDirector.Instance.Settings.OnResolutionChanged -= RefreshCardVisuals;
		}

		public void SetCardVisuals(RuntimeCardData cardData, bool isCompat = true)
		{
			_currentCardData = cardData;
			if ((bool)renderTextureGroup)
			{
				renderTextureGroup.SetActive(value: false);
			}
			if ((bool)emptyVisualsGroup)
			{
				emptyVisualsGroup.SetActive(value: false);
			}
			if (cardData == null)
			{
				if ((bool)emptyVisualsGroup)
				{
					emptyVisualsGroup.SetActive(value: true);
				}
				return;
			}
			if ((bool)renderTextureGroup)
			{
				renderTextureGroup.SetActive(value: true);
			}
			RenderTexture texture = ((!dynamicResolution) ? UICardRenderingManager.Instance.GetOrCreateCardStaticTextureByResFactor(cardData, resolutionFactor) : UICardRenderingManager.Instance.GetOrCreateCardStaticTextureByIndex(cardData, 0));
			renderImage.texture = texture;
			if (mask.texture == null)
			{
				mask.texture = texture;
			}
			SetCompatStateAsync(isCompat).Forget();
		}

		public async UniTask SetCardVisualsAsync(RuntimeCardData cardData)
		{
			_currentCardData = cardData;
			if ((bool)renderTextureGroup)
			{
				renderTextureGroup.SetActive(value: false);
			}
			if ((bool)emptyVisualsGroup)
			{
				emptyVisualsGroup.SetActive(value: false);
			}
			if (cardData == null)
			{
				if ((bool)emptyVisualsGroup)
				{
					emptyVisualsGroup.SetActive(value: true);
				}
				return;
			}
			if ((bool)renderTextureGroup)
			{
				renderTextureGroup.SetActive(value: true);
			}
			bool isDone = false;
			RenderTexture renderTexture = ((!dynamicResolution) ? UICardRenderingManager.Instance.GetOrCreateCardStaticTextureByResFactor(cardData, resolutionFactor, delegate
			{
				isDone = true;
			}) : UICardRenderingManager.Instance.GetOrCreateCardStaticTextureByIndex(cardData, 0, delegate
			{
				isDone = true;
			}));
			await UniTask.WaitUntil(() => isDone);
			renderImage.texture = renderTexture;
			mask.texture = renderTexture;
		}

		public void RefreshCardVisuals()
		{
			SetCardVisualsAsync(_currentCardData).Forget();
		}

		public async UniTask RefreshCardVisualsAsync()
		{
			if (_currentCardData != null)
			{
				await SetCardVisualsAsync(_currentCardData);
			}
		}

		public void SetPerkVisuals(RuntimePerkData perkData)
		{
			if ((bool)renderTextureGroup)
			{
				renderTextureGroup.SetActive(value: false);
			}
			if ((bool)emptyVisualsGroup)
			{
				emptyVisualsGroup.SetActive(value: false);
			}
			if (perkData == null)
			{
				if ((bool)emptyVisualsGroup)
				{
					emptyVisualsGroup.SetActive(value: true);
				}
				return;
			}
			if ((bool)renderTextureGroup)
			{
				renderTextureGroup.SetActive(value: true);
			}
			RenderTexture staticTexture = UIPerkRenderingManager.Instance.GetStaticTexture(perkData);
			renderImage.texture = staticTexture;
			mask.texture = staticTexture;
		}

		public void SetEmptyVisuals()
		{
			if ((bool)renderTextureGroup)
			{
				renderTextureGroup.SetActive(value: false);
			}
			if ((bool)emptyVisualsGroup)
			{
				emptyVisualsGroup.SetActive(value: true);
			}
		}

		public void SetCompatState(bool state)
		{
			if (renderImage.materialForRendering.HasProperty(Allin1ShaderProps.GreyScaleBlend))
			{
				renderImage.materialForRendering.SetFloat(Allin1ShaderProps.GreyScaleBlend, state ? 0f : 1f);
			}
		}

		public async UniTaskVoid SetCompatStateAsync(bool state)
		{
			await UniTask.NextFrame();
			SetCompatState(state);
		}

		public void Show()
		{
			containerMask.fillAmount = 1f;
		}

		public void Hide()
		{
			containerMask.fillAmount = 0f;
		}

		public void SetColor(Color color)
		{
			_colorToApply = color;
			renderImage.color = _colorToApply;
		}
	}
}
