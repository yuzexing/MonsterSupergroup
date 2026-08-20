using System;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	[CreateAssetMenu(fileName = "New Cutscene LUT", menuName = "HellMaiden/Data/Cutscenes/Cutscene LUT")]
	public class CutsceneLUT : ScriptableObject
	{
		[Serializable]
		public struct CutsceneLUTEntry
		{
			public NamedAssetReference assetReference;

			[SerializeReference]
			public int priority;

			public bool isRewatchable;

			public DialogueLUTDialogueDependency[] dialogueDependencies;

			public DialogueLUTTriggerDependency[] triggerDependencies;

			public DialogueLUTNumberDependency[] numberDependencies;

			public CutsceneLUTDependency[] cutsceneDependencies;
		}

		[Serializable]
		public struct CutsceneLUTDependency
		{
			public NamedAssetReference cutscene;

			public bool played;
		}

		public CutsceneLUTEntry[] cutscenes;

		public CutsceneLUTEntry[] Cutscenes => cutscenes;
	}
}
