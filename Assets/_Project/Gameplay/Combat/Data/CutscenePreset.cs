using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	[CreateAssetMenu(fileName = "New CutsceneSkipPreset LUT", menuName = "HellMaiden/Data/Cutscenes/CutsceneSkipPreset")]
	public class CutscenePreset : ScriptableObject
	{
		[Serializable]
		public struct CutscenePresetEntry
		{
			public NamedAssetReference assetReference;
		}

		[Serializable]
		public struct DialogueBoolEntry
		{
			public string variableName;

			public bool value;
		}

		[Serializable]
		public struct DialogueNumberEntry
		{
			public string variableName;

			public int value;
		}

		public string presetName;

		public CutscenePresetEntry[] cutscenes;

		public DialogueBoolEntry[] dialogueBools;

		public DialogueNumberEntry[] dialogueNumbers;

		public PoetPoolID[] unlockPoets;
	}
}
