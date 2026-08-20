using System.Collections.Generic;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Data.Cards;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AstralShift.HellMaiden.UI.Cards
{
	public class CardVisualsFactory
	{
		private static WeaponTemplateVisualDataLUT _weaponTemplateLUT;

		private static EquipmentVisualsTemplateLUT _equipmentTemplateLUT;

		private static Dictionary<object, AsyncOperationHandle<CardVisualData>> _visualDataCache;

		public void Init()
		{
			_visualDataCache = new Dictionary<object, AsyncOperationHandle<CardVisualData>>();
			_weaponTemplateLUT = GameDirector.Instance.runtimeDB.WeaponDB.VisualDataTemplatesLUT;
			_equipmentTemplateLUT = GameDirector.Instance.runtimeDB.EquipmentDB.VisualsTemplateLUT;
		}

		public static async UniTask<UICard3DView> GetCard3DView(RuntimeCardData runtimeCardData, Transform parent = null)
		{
			if (runtimeCardData is RuntimeWeaponData runtimeData)
			{
				return await GetWeaponCard3DView(runtimeData, parent);
			}
			if (runtimeCardData is RuntimeEquipmentData runtimeData2)
			{
				return await GetEquipmentCard3DView(runtimeData2, parent);
			}
			return null;
		}

		public static async UniTask RefreshCard3DViewText(UICard3DView card3DView, RuntimeCardData runtimeCardData)
		{
			if (runtimeCardData is RuntimeWeaponData data)
			{
				await RefreshWeaponCard3DViewText(card3DView, data);
			}
			if (runtimeCardData is RuntimeEquipmentData data2)
			{
				await RefreshEquipmentCard3DViewText(card3DView, data2);
			}
		}

		public static async UniTask<UICardViewHandler> GetUICard(RuntimeCardData runtimeCardData, Transform parent = null)
		{
			if (runtimeCardData is RuntimeWeaponData runtimeData)
			{
				return await GetUIWeaponCard(runtimeData, parent);
			}
			if (runtimeCardData is RuntimeEquipmentData runtimeData2)
			{
				return await GetUIEquipmentCard(runtimeData2, parent);
			}
			return null;
		}

		public static async UniTask RefreshUICardText(UICardViewHandler cardViewHandler)
		{
			if (cardViewHandler is UIWeaponCardViewHandler cardViewHandler2)
			{
				await RefreshUIWeaponCardText(cardViewHandler2);
			}
			if (cardViewHandler is UIEquipmentCardViewHandler cardViewHandler3)
			{
				await RefreshUIEquipmentCardText(cardViewHandler3);
			}
		}

		public static async UniTask<UICardViewHandler> GetUIWeaponCard(RuntimeWeaponData runtimeData, Transform parent = null)
		{
			if (!_weaponTemplateLUT.LUT.TryGetValue(runtimeData.Data.poolID, out var templateData))
			{
				Debug.LogError("CardVisualFactory: Could not generate the Weapon. Missing Template!");
				return null;
			}
			if (templateData.UICardViewTemplate == null)
			{
				Debug.LogError("CardVisualFactory: Could not generate the Weapon. Missing Template!");
				return null;
			}
			UICardViewHandler uICardViewHandler = Object.Instantiate(templateData.UICardViewTemplate, parent);
			if (!(uICardViewHandler is UIWeaponCardViewHandler newCardView))
			{
				Debug.LogError("CardVisualFactory: Could not generate the Weapon. Invalid Template!");
				return null;
			}
			AsyncInstantiateOperation instantiateOperation = Object.InstantiateAsync(templateData.UICard3DViewTemplate);
			await instantiateOperation;
			Object obj = instantiateOperation.Result[0];
			if (!(obj is UICard3DView newCard3DView))
			{
				Debug.LogError("CardVisualFactory: Could not generate the Equipment. Invalid Template!");
				return null;
			}
			newCardView.Initialize(runtimeData);
			UICardRenderingManager.Instance.AddCard(newCardView, newCard3DView);
			WeaponRarity rarity = runtimeData.Data.Rarity;
			newCardView.SetFrameLayer(null, templateData.GetFrame(rarity).Sprite, null, templateData.GetFrame(rarity).Material);
			newCardView.SetTextBoxBackground(templateData.TextBoxBackground.Sprite);
			newCardView.SetTitleText(runtimeData.Data.GetTitle(), templateData.TextColor);
			newCardView.SetDescriptionText(runtimeData.Data.GetDescription(), templateData.TextColor);
			newCardView.SetRarity(rarity);
			newCardView.SetSelectionVFX(templateData.GetSelectionGlow(rarity));
			if (runtimeData.Data.GetQuote(out var text))
			{
				newCardView.SetQuoteText(text, templateData.QuoteColor, templateData.QuoteSeparatorColor);
			}
			if (IsVisualDataInCache(runtimeData.Data.VisualDataReference.RuntimeKey, out var opHandle))
			{
				await ApplyCardVisualData(newCard3DView, opHandle.Result);
			}
			else
			{
				AsyncOperationHandle<CardVisualData> newOpHandle;
				bool isValid = runtimeData.Data.RequestVisualData(out newOpHandle);
				await newOpHandle.Task;
				if (isValid)
				{
					SaveVisualDataToCache(runtimeData.Data.VisualDataReference.RuntimeKey, newOpHandle);
					await ApplyCardVisualData(newCard3DView, newOpHandle.Result);
				}
			}
			return newCardView;
		}

		public static async UniTask<UICard3DView> GetWeaponCard3DView(RuntimeWeaponData runtimeData, Transform parent = null)
		{
			if (!_weaponTemplateLUT.LUT.TryGetValue(runtimeData.Data.poolID, out var templateData))
			{
				Debug.LogError("CardVisualFactory: Could not generate the Weapon. Missing Template!");
				return null;
			}
			AsyncInstantiateOperation instantiateOperation = Object.InstantiateAsync(templateData.UICard3DViewTemplate, parent);
			await instantiateOperation;
			Object obj = instantiateOperation.Result[0];
			if (!(obj is UICard3DView newCard3DView))
			{
				Debug.LogError("CardVisualFactory: Could not generate the Equipment. Invalid Template!");
				return null;
			}
			WeaponRarity rarity = runtimeData.Data.Rarity;
			newCard3DView.SetFrameLayer(null, templateData.GetFrame(rarity).Sprite, null, templateData.GetFrame(rarity).Material);
			newCard3DView.SetTextBoxBackground(templateData.TextBoxBackground.Sprite);
			newCard3DView.SetTitleText(runtimeData.Data.GetTitle(), templateData.TextColor);
			newCard3DView.SetDescriptionText(runtimeData.Data.GetDescription(), templateData.TextColor);
			if (runtimeData.Data.GetQuote(out var text))
			{
				newCard3DView.SetQuoteText(text, templateData.QuoteColor, templateData.QuoteSeparatorColor);
			}
			if (IsVisualDataInCache(runtimeData.Data.VisualDataReference.RuntimeKey, out var opHandle))
			{
				await ApplyCardVisualData(newCard3DView, opHandle.Result);
			}
			else
			{
				AsyncOperationHandle<CardVisualData> newOpHandle;
				bool isValid = runtimeData.Data.RequestVisualData(out newOpHandle);
				await opHandle.Task;
				if (isValid)
				{
					SaveVisualDataToCache(runtimeData.Data.VisualDataReference.RuntimeKey, newOpHandle);
					await ApplyCardVisualData(newCard3DView, newOpHandle.Result);
				}
			}
			return newCard3DView;
		}

		public static async UniTask RefreshUIWeaponCardText(UIWeaponCardViewHandler cardViewHandler)
		{
			RuntimeWeaponData runtimeWeaponData = cardViewHandler.RuntimeWeaponData;
			if (runtimeWeaponData == null)
			{
				return;
			}
			if (!_weaponTemplateLUT.LUT.TryGetValue(runtimeWeaponData.Data.poolID, out var value))
			{
				Debug.LogError("CardVisualFactory: Could not refresh the UI Weapon Card text. Missing Template!");
				return;
			}
			cardViewHandler.SetTitleText(runtimeWeaponData.Data.GetTitle(), value.TextColor);
			cardViewHandler.SetDescriptionText(runtimeWeaponData.Data.GetDescription(), value.TextColor);
			if (runtimeWeaponData.Data.GetQuote(out var text))
			{
				cardViewHandler.SetQuoteText(text, value.QuoteColor, value.QuoteSeparatorColor);
			}
		}

		public static async UniTask RefreshWeaponCard3DViewText(UICard3DView card3DView, RuntimeWeaponData data)
		{
			if (data == null)
			{
				return;
			}
			if (!_weaponTemplateLUT.LUT.TryGetValue(data.Data.poolID, out var value))
			{
				Debug.LogError("CardVisualFactory: Could not refresh the Weapon text. Missing Template!");
				return;
			}
			card3DView.SetTitleText(data.Data.GetTitle(), value.TextColor);
			card3DView.SetDescriptionText(data.Data.GetDescription(), value.TextColor);
			if (data.Data.GetQuote(out var text))
			{
				card3DView.SetQuoteText(text, value.QuoteColor, value.QuoteSeparatorColor);
			}
		}

		public static async UniTask<UICardViewHandler> GetUIEquipmentCard(RuntimeEquipmentData runtimeData, Transform parent = null)
		{
			if (!_equipmentTemplateLUT.CardTypeLUT.TryGetValue(runtimeData.Data.cardType, out var templateData))
			{
				Debug.LogError("CardVisualFactory: Could not generate the Equipment. Missing Template!");
				return null;
			}
			if (templateData.UICardViewTemplate == null)
			{
				Debug.LogError("CardVisualFactory: Could not generate the Equipment. Missing Template!");
				return null;
			}
			UICardViewHandler uICardViewHandler = Object.Instantiate(templateData.UICardViewTemplate, parent);
			if (!(uICardViewHandler is UIEquipmentCardViewHandler newCardView))
			{
				Debug.LogError("CardVisualFactory: Could not generate the Equipment. Invalid Template!");
				return null;
			}
			AsyncInstantiateOperation instantiateOperation = Object.InstantiateAsync(templateData.UICard3DViewTemplate);
			await instantiateOperation;
			if (!(instantiateOperation.Result[0] is UICard3DView card3DView))
			{
				Debug.LogError("CardVisualFactory: Could not generate the Equipment. Invalid Template!");
				return null;
			}
			newCardView.Initialize(runtimeData);
			UICardRenderingManager.Instance.AddCard(newCardView, card3DView);
			uint levelIndex = runtimeData.LevelIndex;
			newCardView.SetFrameLayer(templateData.FrameBackground.Sprite, templateData.FramesPerLevel[levelIndex].Sprite, null, templateData.FramesPerLevel[levelIndex].Material);
			newCardView.SetLevelIcon(templateData.LevelIcons[levelIndex].Sprite);
			newCardView.SetEffectIcon(_equipmentTemplateLUT.GetPreferredModifierIconSprite(runtimeData.Data.Levels[levelIndex]));
			newCardView.SetTextBoxBackground(templateData.TextBackground.Sprite);
			newCardView.SetTitleText(runtimeData.Data.GetTitle(), templateData.TextColor);
			newCardView.SetDescriptionText(runtimeData.Data.GetDescription(levelIndex), templateData.TextColor);
			newCardView.SetRarity(levelIndex);
			newCardView.SetSelectionVFX(templateData.EffectsPerLevel[levelIndex].selectionGlow);
			if (runtimeData.Data.GetQuote(out var text))
			{
				newCardView.SetQuoteText(text, templateData.QuoteColor, templateData.QuoteSeparatorColor);
			}
			if (IsVisualDataInCache(runtimeData.Data.VisualDataReference.RuntimeKey, out var opHandle))
			{
				CardVisualData result = opHandle.Result;
				if (result is EquipmentCardVisualData visualData)
				{
					await ApplyEquipmentCardVisualData(newCardView, visualData, (int)levelIndex);
				}
				else
				{
					await ApplyCardVisualData(newCardView, result);
				}
			}
			else
			{
				AsyncOperationHandle<CardVisualData> newOpHandle;
				bool isValid = runtimeData.Data.RequestVisualData(out newOpHandle);
				await newOpHandle.Task;
				if (isValid)
				{
					SaveVisualDataToCache(runtimeData.Data.VisualDataReference.RuntimeKey, newOpHandle);
					CardVisualData result2 = newOpHandle.Result;
					if (result2 is EquipmentCardVisualData visualData2)
					{
						await ApplyEquipmentCardVisualData(newCardView, visualData2, (int)levelIndex);
					}
					else
					{
						await ApplyCardVisualData(newCardView, result2);
					}
				}
			}
			return newCardView;
		}

		public static async UniTask<UICard3DView> GetEquipmentCard3DView(RuntimeEquipmentData runtimeData, Transform parent = null)
		{
			if (!_equipmentTemplateLUT.CardTypeLUT.TryGetValue(runtimeData.Data.cardType, out var templateData))
			{
				Debug.LogError("CardVisualFactory: Could not generate the Equipment. Missing Template!");
				return null;
			}
			AsyncInstantiateOperation instantiateOperation = Object.InstantiateAsync(templateData.UICard3DViewTemplate, parent);
			await instantiateOperation;
			Object obj = instantiateOperation.Result[0];
			if (!(obj is UICard3DView newCard3DView))
			{
				Debug.LogError("CardVisualFactory: Could not generate the Equipment. Invalid Template!");
				return null;
			}
			uint levelIndex = runtimeData.LevelIndex;
			newCard3DView.SetFrameLayer(templateData.FrameBackground.Sprite, templateData.FramesPerLevel[levelIndex].Sprite, null, templateData.FramesPerLevel[levelIndex].Material);
			newCard3DView.SetLevelIcon(templateData.LevelIcons[levelIndex].Sprite);
			newCard3DView.SetEffectIcon(_equipmentTemplateLUT.GetPreferredModifierIconSprite(runtimeData.Data.Levels[levelIndex]));
			newCard3DView.SetTextBoxBackground(templateData.TextBackground.Sprite);
			newCard3DView.SetTitleText(runtimeData.Data.GetTitle(), templateData.TextColor);
			newCard3DView.SetDescriptionText(runtimeData.Data.GetDescription(levelIndex), templateData.TextColor);
			if (runtimeData.Data.GetQuote(out var text))
			{
				newCard3DView.SetQuoteText(text, templateData.QuoteColor, templateData.QuoteSeparatorColor);
			}
			if (IsVisualDataInCache(runtimeData.Data.VisualDataReference.RuntimeKey, out var opHandle))
			{
				CardVisualData result = opHandle.Result;
				if (result is EquipmentCardVisualData visualData)
				{
					await ApplyEquipmentCardVisualData(newCard3DView, visualData, (int)levelIndex);
				}
				else
				{
					await ApplyCardVisualData(newCard3DView, result);
				}
			}
			else
			{
				AsyncOperationHandle<CardVisualData> newOpHandle;
				bool isValid = runtimeData.Data.RequestVisualData(out newOpHandle);
				await newOpHandle.Task;
				if (isValid)
				{
					SaveVisualDataToCache(runtimeData.Data.VisualDataReference.RuntimeKey, newOpHandle);
					CardVisualData result2 = newOpHandle.Result;
					if (result2 is EquipmentCardVisualData visualData2)
					{
						await ApplyEquipmentCardVisualData(newCard3DView, visualData2, (int)levelIndex);
					}
					else
					{
						await ApplyCardVisualData(newCard3DView, result2);
					}
				}
			}
			return newCard3DView;
		}

		public static async UniTask RefreshUIEquipmentCard(UIEquipmentCardViewHandler cardViewHandler)
		{
			if (cardViewHandler.RuntimeEquipmentData == null)
			{
				Debug.LogError("CardVisualFactory: No RuntimeEquipmentData assigned to this card");
				return;
			}
			RuntimeEquipmentData equipmentData = cardViewHandler.RuntimeEquipmentData;
			if (!_equipmentTemplateLUT.CardTypeLUT.TryGetValue(equipmentData.Data.cardType, out var value))
			{
				Debug.LogError("CardVisualFactory: Could not generate the Equipment. Missing Template!");
				return;
			}
			uint levelIndex = equipmentData.LevelIndex;
			cardViewHandler.ReInitialize(equipmentData);
			cardViewHandler.SetFrameLayer(value.FrameBackground.Sprite, value.FramesPerLevel[levelIndex].Sprite, null, value.FramesPerLevel[levelIndex].Material);
			cardViewHandler.SetLevelIcon(value.LevelIcons[levelIndex].Sprite);
			cardViewHandler.SetEffectIcon(_equipmentTemplateLUT.GetPreferredModifierIconSprite(equipmentData.Data.Levels[levelIndex]));
			cardViewHandler.SetTextBoxBackground(value.TextBackground.Sprite);
			cardViewHandler.SetTitleText(equipmentData.Data.GetTitle(), value.TextColor);
			cardViewHandler.SetDescriptionText(equipmentData.Data.GetDescription(levelIndex), value.TextColor);
			cardViewHandler.SetRarity(levelIndex);
			cardViewHandler.SetSelectionVFX(value.EffectsPerLevel[levelIndex].selectionGlow);
			if (equipmentData.Data.GetQuote(out var text))
			{
				cardViewHandler.SetQuoteText(text, value.QuoteColor, value.QuoteSeparatorColor);
			}
			if (IsVisualDataInCache(equipmentData.Data.VisualDataReference.RuntimeKey, out var opHandle))
			{
				CardVisualData result = opHandle.Result;
				if (result is EquipmentCardVisualData visualData)
				{
					await ApplyEquipmentCardVisualData(cardViewHandler, visualData, (int)levelIndex);
				}
				else
				{
					await ApplyCardVisualData(cardViewHandler, result);
				}
			}
			else
			{
				AsyncOperationHandle<CardVisualData> newOpHandle;
				bool isValid = equipmentData.Data.RequestVisualData(out newOpHandle);
				await opHandle.Task;
				if (isValid)
				{
					SaveVisualDataToCache(equipmentData.Data.VisualDataReference.RuntimeKey, newOpHandle);
					CardVisualData result2 = newOpHandle.Result;
					if (result2 is EquipmentCardVisualData visualData2)
					{
						await ApplyEquipmentCardVisualData(cardViewHandler, visualData2, (int)levelIndex);
					}
					else
					{
						await ApplyCardVisualData(cardViewHandler, result2);
					}
				}
			}
			await UniTask.NextFrame();
		}

		public static async UniTask RefreshUIEquipmentCardText(UIEquipmentCardViewHandler cardViewHandler)
		{
			RuntimeEquipmentData runtimeEquipmentData = cardViewHandler.RuntimeEquipmentData;
			if (runtimeEquipmentData != null)
			{
				if (!_equipmentTemplateLUT.CardTypeLUT.TryGetValue(runtimeEquipmentData.Data.cardType, out var value))
				{
					Debug.LogError("CardVisualFactory: Could not refresh the UI Equipment Card text. Missing Template!");
					return;
				}
				cardViewHandler.SetTitleText(runtimeEquipmentData.Data.GetTitle(), value.TextColor);
				cardViewHandler.SetDescriptionText(runtimeEquipmentData.Data.GetDescription(), value.TextColor);
			}
		}

		public static async UniTask RefreshEquipmentCard3DViewText(UICard3DView card3DView, RuntimeEquipmentData data)
		{
			if (data != null)
			{
				if (!_equipmentTemplateLUT.CardTypeLUT.TryGetValue(data.Data.cardType, out var value))
				{
					Debug.LogError("CardVisualFactory: Could not refresh the Equipment text. Missing Template!");
					return;
				}
				card3DView.SetTitleText(data.Data.GetTitle(), value.TextColor);
				card3DView.SetDescriptionText(data.Data.GetDescription(), value.TextColor);
			}
		}

		private static async UniTask ApplyEquipmentCardVisualData(ICardVisual cardElement, EquipmentCardVisualData visualData, int levelIndex)
		{
			if (!(visualData == null))
			{
				cardElement.SetIllustrationMainLayer(visualData.IllustrationsPerLevel[levelIndex].Main.Sprite, visualData.IllustrationsPerLevel[levelIndex].Main.Material);
				cardElement.ClearIllustrationAdditionalLayers();
				for (int i = 0; i < visualData.IllustrationsPerLevel[levelIndex].Additional.Count; i++)
				{
					CardVisualLayer cardVisualLayer = visualData.IllustrationsPerLevel[levelIndex].Additional[i];
					await cardElement.SetIllustrationAdditionalLayer(i, cardVisualLayer.Sprite, cardVisualLayer.Material);
				}
				if ((bool)visualData.TextBoxBackground.Sprite)
				{
					cardElement.SetTextBoxBackground(visualData.TextBoxBackground.Sprite);
				}
			}
		}

		private static async UniTask ApplyCardVisualData(ICardVisual cardElement, CardVisualData visualData)
		{
			if (!(visualData == null))
			{
				cardElement.ClearIllustrationAdditionalLayers();
				cardElement.SetIllustrationMainLayer(visualData.Illustration.Sprite, visualData.Illustration.Material);
				for (int i = 0; i < visualData.IllustrationLayers.Count; i++)
				{
					await cardElement.SetIllustrationAdditionalLayer(i, visualData.IllustrationLayers[i].Sprite, visualData.IllustrationLayers[i].Material);
				}
				for (int i = 0; i < visualData.ForegroundLayers.Count; i++)
				{
					await cardElement.SetForegroundLayer(visualData.ForegroundLayers[i].Sprite, visualData.ForegroundLayers[i].Material);
				}
				if (visualData.TextBoxBackground.Sprite != null)
				{
					cardElement.SetTextBoxBackground(visualData.TextBoxBackground.Sprite);
				}
			}
		}

		private static bool IsVisualDataInCache(object runtimeKey, out AsyncOperationHandle<CardVisualData> opHandle)
		{
			return _visualDataCache.TryGetValue(runtimeKey, out opHandle);
		}

		private static bool IsVisualDataInCache(object runtimeKey)
		{
			return _visualDataCache.ContainsKey(runtimeKey);
		}

		private static void SaveVisualDataToCache(object runtimeKey, AsyncOperationHandle<CardVisualData> opHandle)
		{
			_visualDataCache.TryAdd(runtimeKey, opHandle);
		}

		public static void ReleaseVisualDataCache()
		{
			foreach (AsyncOperationHandle<CardVisualData> value in _visualDataCache.Values)
			{
				Addressables.Release(value);
			}
			_visualDataCache.Clear();
		}
	}
}
