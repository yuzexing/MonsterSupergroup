// using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralShift.HellMaiden.Data
{
	public class CutsceneDebugEntry : MonoBehaviour
	{
		public TMP_Text presetNameText;

		public UnityEngine.UI.Toggle toggle;

		private CutscenePreset preset;

		public void Setup(CutscenePreset preset)
		{
			this.preset = preset;
			presetNameText.text = preset.presetName;
			toggle.isOn = IsPresetApplied();
			toggle.onValueChanged.AddListener(OnToggleChanged);
		}

		private bool IsPresetApplied()
		{
			CutscenePreset.CutscenePresetEntry[] cutscenes = preset.cutscenes;
			for (int i = 0; i < cutscenes.Length; i++)
			{
				if (!GameDataManager.HasCutscenePlayed(cutscenes[i].assetReference.Name))
				{
					return false;
				}
			}
			CutscenePreset.DialogueBoolEntry[] dialogueBools = preset.dialogueBools;
			for (int i = 0; i < dialogueBools.Length; i++)
			{
				CutscenePreset.DialogueBoolEntry dialogueBoolEntry = dialogueBools[i];
				// if (DialogueLua.GetVariable(dialogueBoolEntry.variableName).AsBool != dialogueBoolEntry.value)
				// {
				// 	return false;
				// }
			}
			CutscenePreset.DialogueNumberEntry[] dialogueNumbers = preset.dialogueNumbers;
			for (int i = 0; i < dialogueNumbers.Length; i++)
			{
				CutscenePreset.DialogueNumberEntry dialogueNumberEntry = dialogueNumbers[i];
				// if (DialogueLua.GetVariable(dialogueNumberEntry.variableName).AsInt != dialogueNumberEntry.value)
				// {
				// 	return false;
				// }
			}
			return true;
		}

		private void OnToggleChanged(bool value)
		{
			CutscenePreset.CutscenePresetEntry[] cutscenes = preset.cutscenes;
			for (int i = 0; i < cutscenes.Length; i++)
			{
				GameDataManager.RegisterCutscene(cutscenes[i].assetReference.Name);
			}
			CutscenePreset.DialogueBoolEntry[] dialogueBools = preset.dialogueBools;
			for (int i = 0; i < dialogueBools.Length; i++)
			{
				CutscenePreset.DialogueBoolEntry dialogueBoolEntry = dialogueBools[i];
				// DialogueLua.SetVariable(dialogueBoolEntry.variableName, value ? dialogueBoolEntry.value : (!dialogueBoolEntry.value));
			}
			CutscenePreset.DialogueNumberEntry[] dialogueNumbers = preset.dialogueNumbers;
			for (int i = 0; i < dialogueNumbers.Length; i++)
			{
				CutscenePreset.DialogueNumberEntry dialogueNumberEntry = dialogueNumbers[i];
				// DialogueLua.SetVariable(dialogueNumberEntry.variableName, value ? dialogueNumberEntry.value : 0);
			}
			PoetPoolID[] unlockPoets = preset.unlockPoets;
			foreach (PoetPoolID poetPoolID in unlockPoets)
			{
				if (value && !GameData.Instance.unlockedPoets.Contains(poetPoolID))
				{
					GameDirector.Instance.runtimeDB.UnlockPoetPool(poetPoolID);
				}
			}
			Debug.Log($"Preset '{preset.presetName}' applied = {value}");
		}
	}
}
