using System;
using AstralShift.HellMaiden.Data;
using UnityEngine;

namespace AstralShift.HellMaiden.Scenes.SceneLoaders
{
	public class ObjectLoader : MonoBehaviour
	{
		[Serializable]
		public struct ObjectLUTEntry
		{
			public GameObject[] objectsToActivate;

			public DialogueLUTDialogueDependency[] dialogueDependencies;

			public DialogueLUTTriggerDependency[] triggerDependencies;

			public DialogueLUTNumberDependency[] numberDependencies;

			public CutsceneLUTDependencyByString[] cutsceneDependencies;
		}

		[Serializable]
		public struct CutsceneLUTDependencyByString
		{
			public string name;

			public bool played;
		}

		public ObjectLUTEntry[] objects;

		private bool VerifyEntry(ObjectLUTEntry objectToActivate)
		{
			for (int i = 0; i < objectToActivate.cutsceneDependencies.Length; i++)
			{
				CutsceneLUTDependencyByString cutsceneLUTDependencyByString = objectToActivate.cutsceneDependencies[i];
				if (GameDataManager.HasCutscenePlayed(cutsceneLUTDependencyByString.name) != cutsceneLUTDependencyByString.played)
				{
					return false;
				}
			}
			for (int j = 0; j < objectToActivate.dialogueDependencies.Length; j++)
			{
				DialogueLUTDialogueDependency dialogueLUTDialogueDependency = objectToActivate.dialogueDependencies[j];
				// if (GameDataManager.HasDialoguePlayed(dialogueLUTDialogueDependency.dialogue) != dialogueLUTDialogueDependency.state)
				// {
				// 	return false;
				// }
			}
			for (int k = 0; k < objectToActivate.triggerDependencies.Length; k++)
			{
				DialogueLUTTriggerDependency dialogueLUTTriggerDependency = objectToActivate.triggerDependencies[k];
				// if (GameDataManager.GetGameTriggerState(dialogueLUTTriggerDependency.variable) != dialogueLUTTriggerDependency.state)
				// {
				// 	return false;
				// }
			}
			for (int l = 0; l < objectToActivate.numberDependencies.Length; l++)
			{
				DialogueLUTNumberDependency dialogueLUTNumberDependency = objectToActivate.numberDependencies[l];
				// if (!dialogueLUTNumberDependency.Compare(GameDataManager.GetGameInt(dialogueLUTNumberDependency.variable)))
				// {
				// 	return false;
				// }
			}
			return true;
		}

		public void Init()
		{
			for (int i = 0; i < objects.Length; i++)
			{
				if (VerifyEntry(objects[i]))
				{
					for (int j = 0; j < objects[i].objectsToActivate.Length; j++)
					{
						objects[i].objectsToActivate[j].SetActive(value: true);
					}
				}
				else
				{
					for (int k = 0; k < objects[i].objectsToActivate.Length; k++)
					{
						objects[i].objectsToActivate[k].SetActive(value: false);
					}
				}
			}
		}
	}
}
