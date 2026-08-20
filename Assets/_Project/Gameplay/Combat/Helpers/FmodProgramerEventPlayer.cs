using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AOT;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using Sirenix.Utilities;
using UnityEngine;

namespace AstralShift.Helpers
{
	internal class FmodProgramerEventPlayer : MonoBehaviour
	{
		private EVENT_CALLBACK dialogueCallback;

		public EventReference EventName;

		private void Start()
		{
			dialogueCallback = DialogueEventCallback;
		}

		public void PlayDialogue(string key)
		{
			EventInstance eventInstance = RuntimeManager.CreateInstance(EventName);
			GCHandle value = GCHandle.Alloc(key);
			eventInstance.setUserData(GCHandle.ToIntPtr(value));
			eventInstance.setCallback(dialogueCallback);
			eventInstance.start();
			eventInstance.release();
		}

		public void PlayDialogue(string eventName, string key)
		{
			EventName = RuntimeManager.PathToEventReference(eventName);
			PlayDialogue(key);
		}

		public void PlayRandomDialogueFromList(string eventName, List<string> keys, float playChance = 0f, float delay = 0.5f)
		{
			if (!keys.IsNullOrEmpty() && (playChance >= 1f || UnityEngine.Random.value < playChance))
			{
				StartCoroutine(PlayRandomDialogueDelayed(eventName, keys, delay));
			}
		}

		private IEnumerator PlayRandomDialogueDelayed(string eventName, List<string> keys, float delay)
		{
			yield return new WaitForSecondsRealtime(delay);
			string key = keys[UnityEngine.Random.Range(0, keys.Count)];
			EventName = RuntimeManager.PathToEventReference(eventName);
			PlayDialogue(key);
		}

		[MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
		private static RESULT DialogueEventCallback(EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
		{
			new EventInstance(instancePtr).getUserData(out var userdata);
			GCHandle gCHandle = GCHandle.FromIntPtr(userdata);
			string text = gCHandle.Target as string;
			switch (type)
			{
			case EVENT_CALLBACK_TYPE.CREATE_PROGRAMMER_SOUND:
			{
				MODE mODE = MODE.LOOP_NORMAL | MODE.CREATECOMPRESSEDSAMPLE | MODE.NONBLOCKING;
				PROGRAMMER_SOUND_PROPERTIES structure = (PROGRAMMER_SOUND_PROPERTIES)Marshal.PtrToStructure(parameterPtr, typeof(PROGRAMMER_SOUND_PROPERTIES));
				SOUND_INFO info;
				Sound sound2;
				if (text.Contains("."))
				{
					if (RuntimeManager.CoreSystem.createSound(Application.streamingAssetsPath + "/" + text, mODE, out var sound) == RESULT.OK)
					{
						structure.sound = sound.handle;
						structure.subsoundIndex = -1;
						Marshal.StructureToPtr(structure, parameterPtr, fDeleteOld: false);
					}
				}
				else if (RuntimeManager.StudioSystem.getSoundInfo(text, out info) == RESULT.OK && RuntimeManager.CoreSystem.createSound(info.name_or_data, mODE | info.mode, ref info.exinfo, out sound2) == RESULT.OK)
				{
					structure.sound = sound2.handle;
					structure.subsoundIndex = info.subsoundindex;
					Marshal.StructureToPtr(structure, parameterPtr, fDeleteOld: false);
				}
				break;
			}
			case EVENT_CALLBACK_TYPE.DESTROY_PROGRAMMER_SOUND:
				new Sound(((PROGRAMMER_SOUND_PROPERTIES)Marshal.PtrToStructure(parameterPtr, typeof(PROGRAMMER_SOUND_PROPERTIES))).sound).release();
				break;
			case EVENT_CALLBACK_TYPE.DESTROYED:
				gCHandle.Free();
				break;
			}
			return RESULT.OK;
		}
	}
}
