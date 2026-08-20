	using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Timeline;
using AstralShift.Helpers.Attributes;
using AstralShift.Initialization;
using Cysharp.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AstralShift.HellMaiden.Scenes.SceneLoaders
{
	public class CutsceneLoader : SceneLoader
	{
		[SerializeField]
		private CutsceneLUT cutsceneLUT;

		[ReadOnly]
		public IEnumerable<CutsceneLUT.CutsceneLUTEntry> cutscenes;

		private AssetReference reference;

		private TimelineDirector[] inSceneCutscenes;

		public override UniTask LoadAsync()
		{
			bool cutsceneQueued;
			UniTask result = Init(out cutsceneQueued);
			if (cutsceneQueued)
			{
				return result;
			}
			return UniTask.CompletedTask;
		}

		public UniTask Init(out bool cutsceneQueued)
		{
			cutsceneQueued = false;
			if (cutsceneLUT == null)
			{
				return UniTask.CompletedTask;
			}
			cutscenes = (cutsceneLUT.Cutscenes ?? Array.Empty<CutsceneLUT.CutsceneLUTEntry>())
				.OrderByDescending((CutsceneLUT.CutsceneLUTEntry c) => c.priority);
			foreach (CutsceneLUT.CutsceneLUTEntry cutscene in cutscenes)
			{
				if (!VerifyEntry(cutscene))
				{
					continue;
				}
				inSceneCutscenes = GetComponentsInChildren<TimelineDirector>(includeInactive: true);
				if (inSceneCutscenes != null)
				{
					for (int num = 0; num < inSceneCutscenes.Length; num++)
					{
						if (inSceneCutscenes[num].name == cutscene.assetReference.Name)
						{
							ScheduleCutsceneActivation(inSceneCutscenes[num]);
							cutsceneQueued = true;
							return UniTask.CompletedTask;
						}
					}
				}
				reference = cutscene.assetReference;
				AsyncOperationHandle handle = reference.LoadAssetAsync<GameObject>();
				handle.Completed += Handle_Completed;
				cutsceneQueued = true;
				return Task.Run(async () => await handle.Task).AsUniTask();
			}
			return UniTask.CompletedTask;
		}

		private void Handle_Completed(AsyncOperationHandle obj)
		{
			if (obj.Status == AsyncOperationStatus.Succeeded)
			{
				// SpawnCutscene(reference.Asset.GetComponent<TimelineDirector>());
				SpawnCutscene(
					(reference.Asset as GameObject)?.GetComponent<TimelineDirector>()
				);
			}
			else
			{
				Debug.LogError($"AssetReference {reference.RuntimeKey} failed to load.");
			}
		}

		private bool VerifyEntry(CutsceneLUT.CutsceneLUTEntry cutscene)
		{
			if (!cutscene.isRewatchable && GameDataManager.HasCutscenePlayed(cutscene.assetReference.Name))
			{
				return false;
			}
			for (int i = 0; i < cutscene.cutsceneDependencies.Length; i++)
			{
				CutsceneLUT.CutsceneLUTDependency cutsceneLUTDependency = cutscene.cutsceneDependencies[i];
				if (GameDataManager.HasCutscenePlayed(cutsceneLUTDependency.cutscene.Name) != cutsceneLUTDependency.played)
				{
					return false;
				}
			}
			for (int j = 0; j < cutscene.dialogueDependencies.Length; j++)
			{
				DialogueLUTDialogueDependency dialogueLUTDialogueDependency = cutscene.dialogueDependencies[j];
				// if (GameDataManager.HasDialoguePlayed(dialogueLUTDialogueDependency.dialogue) != dialogueLUTDialogueDependency.state)
				// {
				// 	return false;
				// }
			}
			for (int k = 0; k < cutscene.triggerDependencies.Length; k++)
			{
				DialogueLUTTriggerDependency dialogueLUTTriggerDependency = cutscene.triggerDependencies[k];
				// if (GameDataManager.GetGameTriggerState(dialogueLUTTriggerDependency.variable) != dialogueLUTTriggerDependency.state)
				// {
				// 	return false;
				// }
			}
			for (int l = 0; l < cutscene.numberDependencies.Length; l++)
			{
				DialogueLUTNumberDependency dialogueLUTNumberDependency = cutscene.numberDependencies[l];
				// if (!dialogueLUTNumberDependency.Compare(GameDataManager.GetGameInt(dialogueLUTNumberDependency.variable)))
				// {
				// 	return false;
				// }
			}
			return true;
		}

		public void SpawnCutscene(TimelineDirector cutscene)
		{
			TimelineDirector timelineDirector = UnityEngine.Object.Instantiate(cutscene, cutscene.transform.position, Quaternion.identity, base.transform.parent);
			timelineDirector.name = cutscene.name;
			ScheduleCutsceneActivation(timelineDirector);
		}

		public void ScheduleCutsceneActivation(TimelineDirector timelineDirector)
		{
			timelineDirector.gameObject.SetActive(value: true);
			GameDataManager.RegisterCutscene(timelineDirector.name);
			if (timelineDirector.overwriteFadeIn)
			{
				SceneMaster.Instance.overrideFadeIn = true;
			}
			else
			{
				ScreenFader.Instance.SetFadeIn(timelineDirector.entryFade);
				ScreenFader.Instance.SetFadeOut(timelineDirector.exitFade);
			}
			SceneMaster.Instance.OnSceneShowStart += timelineDirector.Play;
		}

		private void OnDestroy()
		{
			if (reference != null)
			{
				reference.ReleaseAsset();
			}
		}
	}
}
