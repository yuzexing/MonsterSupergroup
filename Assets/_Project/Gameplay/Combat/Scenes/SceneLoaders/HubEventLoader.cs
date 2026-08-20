using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Dialogue;
using AstralShift.HellMaiden.Interactions;
using AstralShift.Initialization;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Scenes.SceneLoaders
{
	public class HubEventLoader : SceneLoader
	{
		public DialogueSystemInteraction dialogueInteractionPrefab;

		public CutsceneLoader cutsceneLoader;

		public NPCLoader npcLoader;

		public ObjectLoader objectLoader;

		public Transform spawnedDialoguesParent;

		public PoetsDialogueLUT PoetsDialogueLUT;

		private int extraPoetsAllowed = 2;

		private List<DialogueLUTEntry> chosenDialogues;

		private List<PoetID> chosenPoets;

		private void Load()
		{
			chosenDialogues = new List<DialogueLUTEntry>();
			chosenPoets = new List<PoetID>();
			DialogueLUTEntry highestPriorityDialogue = GetHighestPriorityDialogue(PoetID.Virgil);
			chosenDialogues.Add(highestPriorityDialogue);
			chosenPoets.Add(PoetID.Virgil);
			List<PoetID> list = new List<PoetID>(GameData.Instance.availableHubNPCs);
			list.Remove(PoetID.Virgil);
			for (int i = 0; i < extraPoetsAllowed; i++)
			{
				if (list.Count <= 0)
				{
					break;
				}
				int index = Random.Range(0, list.Count);
				PoetID poetID = list[index];
				DialogueLUTEntry highestPriorityDialogue2 = GetHighestPriorityDialogue(poetID);
				chosenDialogues.Add(highestPriorityDialogue2);
				chosenPoets.Add(poetID);
				list.Remove(poetID);
			}
			npcLoader.Init(spawnedDialoguesParent);
			list = new List<PoetID>(GameData.Instance.availableHubNPCs);
			for (int j = 0; j < list.Count; j++)
			{
				PoetID poetID2 = list[j];
				GameObject gameObject = npcLoader.SpawnPoet(poetID2);
				if (chosenPoets.Contains(poetID2))
				{
					int index2 = chosenPoets.IndexOf(poetID2);
					// string conversation = chosenDialogues[index2].conversation;
					DialogueSystemInteraction dialogueSystemInteraction = Object.Instantiate(dialogueInteractionPrefab, spawnedDialoguesParent);
					dialogueSystemInteraction.gameObject.name = gameObject.name + "interaction";
					// dialogueSystemInteraction.conversation = conversation;
					dialogueSystemInteraction.transform.position = gameObject.transform.position;
					// if (!GameDataManager.HasDialoguePlayed(conversation))
					// {
					// 	dialogueSystemInteraction.NPC = gameObject;
					// 	dialogueSystemInteraction.showNPCBaloon = true;
					// }
					if (chosenDialogues[index2].overrideDialogueSettings)
					{
						dialogueSystemInteraction.SetDialogueOverrides(chosenDialogues[index2].DialogueOverrides);
					}
				}
			}
			Debug.Log("finished hub loading: no cutscenes");
		}

		public override UniTask LoadAsync()
		{
			Debug.Log("started hub loading");
			objectLoader.Init();
			if (cutsceneLoader != null)
			{
				Debug.Log("finished hub loading: cutscene will spawn");
				bool cutsceneQueued;
				UniTask result = cutsceneLoader.Init(out cutsceneQueued);
				if (cutsceneQueued)
				{
					return result;
				}
			}
			Load();
			return UniTask.CompletedTask;
		}

		public DialogueLUTEntry GetHighestPriorityDialogue(PoetID poet)
		{
			HubDialogueLUT hubDialogueLUT = PoetsDialogueLUT.LUT[poet];
			if (GetHighestPriorityDialogue(hubDialogueLUT.HighPriority, out var result))
			{
				return result;
			}
			if (GetHighestPriorityDialogue(hubDialogueLUT.MediumPriority, out result))
			{
				return result;
			}
			if (GetHighestPriorityDialogue(hubDialogueLUT.LowPriority, out result))
			{
				return result;
			}
			return hubDialogueLUT.LowPriority[0];
		}

		public bool GetHighestPriorityDialogue(List<DialogueLUTEntry> priorityList, out DialogueLUTEntry result)
		{
			result = default(DialogueLUTEntry);
			List<DialogueLUTEntry> list = priorityList.OrderByDescending((DialogueLUTEntry e) => e.priority).ToList();
			List<DialogueLUTEntry> list2 = new List<DialogueLUTEntry>();
			int num = -1;
			for (int num2 = 0; num2 < list.Count; num2++)
			{
				if (list[num2].priority >= num && VerifyEntry(list[num2]))
				{
					if (num == -1)
					{
						num = list[num2].priority;
					}
					list2.Add(list[num2]);
				}
			}
			if (list2.Count > 0)
			{
				int index = Random.Range(0, list2.Count);
				result = list2[index];
				return true;
			}
			return false;
		}

		private bool VerifyEntry(DialogueLUTEntry entry)
		{
			// Debug.Log("HUBLOADER: verifying entry " + entry.conversation);
			// if (!entry.isRewatchable && GameDataManager.HasDialoguePlayed(entry.conversation))
			// {
			// 	return false;
			// }
			if (entry.dialogueDependencies != null)
			{
				for (int i = 0; i < entry.dialogueDependencies.Length; i++)
				{
					DialogueLUTDialogueDependency dialogueLUTDialogueDependency = entry.dialogueDependencies[i];
					// if (GameDataManager.HasDialoguePlayed(dialogueLUTDialogueDependency.dialogue) != dialogueLUTDialogueDependency.state)
					// {
					// 	return false;
					// }
				}
			}
			if (entry.triggerDependencies != null)
			{
				for (int j = 0; j < entry.triggerDependencies.Length; j++)
				{
					DialogueLUTTriggerDependency dialogueLUTTriggerDependency = entry.triggerDependencies[j];
					// if (GameDataManager.GetGameTriggerState(dialogueLUTTriggerDependency.variable) != dialogueLUTTriggerDependency.state)
					// {
					// 	return false;
					// }
				}
			}
			if (entry.numberDependencies != null)
			{
				for (int k = 0; k < entry.numberDependencies.Length; k++)
				{
					DialogueLUTNumberDependency dialogueLUTNumberDependency = entry.numberDependencies[k];
					// if (!dialogueLUTNumberDependency.Compare(GameDataManager.GetGameInt(dialogueLUTNumberDependency.variable)))
					// {
					// 	return false;
					// }
				}
			}
			if (entry.cutsceneDependencies != null)
			{
				for (int l = 0; l < entry.cutsceneDependencies.Length; l++)
				{
					CutsceneLUT.CutsceneLUTDependency cutsceneLUTDependency = entry.cutsceneDependencies[l];
					if (GameDataManager.HasCutscenePlayed(cutsceneLUTDependency.cutscene.Name) != cutsceneLUTDependency.played)
					{
						return false;
					}
				}
			}
			return true;
		}
	}
}
