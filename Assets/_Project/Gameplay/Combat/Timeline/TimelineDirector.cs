using System;
using System.Collections.Generic;
using System.Linq;
using AstralShift.DebugTools;
using AstralShift.FadeEffect;
using AstralShift.HellMaiden.Audio;
using AstralShift.HellMaiden.Characters;
using AstralShift.HellMaiden.Controllers;
using AstralShift.HellMaiden.Data;
using AstralShift.HellMaiden.Dialogue;
using AstralShift.HellMaiden.Interactions;
using AstralShift.HellMaiden.Scenes;
using AstralShift.Managers;
using FMODUnity;
using PixelCrushers.DialogueSystem;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Serialization;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Timeline
{
	[RequireComponent(typeof(TimelineEffects))]
	public class TimelineDirector : MonoBehaviour
	{
		public const float minClipSize = 0.5f;

		private PlayableDirector _playableDirector;

		private Action onTimelineEnded;

		public bool register = true;

		public bool pause = true;

		public bool skippable = true;

		[Tooltip("If true, fully stops and skips the entire timeline, use this when the screen fades ex:scene reloads or changes./n If false, skips to the final moment of the timeline to ensure signal calls and camera resets, use this if no fades happen")]
		public bool fullSkip = true;

		public float skipHoldTime = 1f;

		public CustomUnityUIPlayerControllerElementGlyph skipGlyph;

		public bool canOpenLog = true;

		public bool replaceNPCPosition;

		public bool overrideDialogueSettings;

		public DialogueOverrides dialogueOverrides;

		private List<string> switchedActors;

		public UnityEvent onEndUnityEvent;

		[FormerlySerializedAs("StartTransform")]
		public Transform StartTransformPlayer;

		[Tooltip("Use this transform to set Party's position at the begginning of the cutscene, leave it empty not relevant.")]
		public Transform StartTransformParty;

		private TimelineEffects _effects;

		private bool _wasDisabled;

		public bool overwriteFadeIn;

		public bool overwriteFadeOut;

		public FadeEffectEnum entryFade;

		public FadeEffectEnum exitFade;

		[SerializeField]
		private bool reloadsScene = true;

		public bool destroyOnEnd;

		[Tooltip("Recenters the camera on the Player by removing all other camera targets.")]
		public bool resetCamera;

		[Tooltip("Should the FMOD snapshot change when dialogue starts?")]
		[SerializeField]
		private bool changeDialogueSnapshot;

		[SerializeField]
		private MusicPlayer.SnapshotID dialogueSnapshot = MusicPlayer.SnapshotID.Dialogue;

		[Tooltip("Should the FMOD snapshot change when timeline starts?")]
		[SerializeField]
		private bool changeNormalSnapshot;

		[SerializeField]
		private MusicPlayer.SnapshotID normalSnapshot = MusicPlayer.SnapshotID.Normal;

		[SerializeField]
		private EventReference cutsceneSkip;

		[Header("Override BGM")]
		[SerializeField]
		private bool overrideBGM;

		[SerializeField]
		private EventReference overrideEventBGM;

		[SerializeField]
		[Tooltip("Stop the Overriden BMG On the end of the Cutscene")]
		private bool StopOverridenBGM;

		private bool choiceSelected;

		private string _lastName;

		public TimelineDialogueController controller { get; set; }

		public bool ReloadsScene => reloadsScene;

		private void Awake()
		{
			if (!StartTransformPlayer)
			{
				DBL.Log(DBL.Module.Timeline, "No start transform assigned.");
				StartTransformPlayer = GameDirector.Instance.Player.transform;
			}
		}

		private void Start()
		{
			_effects = GetComponent<TimelineEffects>();
			_playableDirector = GetComponent<PlayableDirector>();
			IEnumerable<TrackAsset> enumerable = from t in (_playableDirector.playableAsset as TimelineAsset).GetOutputTracks()
				where t.GetType() == typeof(CharacterMovementTrack)
				select t;
			if (enumerable.ToArray().Length == 0)
			{
				return;
			}
			List<GameObject> list = new List<GameObject>();
			foreach (TrackAsset item in enumerable)
			{
				Transform transform = _playableDirector.GetGenericBinding(item) as Transform;
				if (transform == null)
				{
					continue;
				}
				Transform characterTransform = GetCharacterTransform(transform.name);
				if (transform.name == "Player_CUT" || (replaceNPCPosition && (bool)characterTransform))
				{
					_playableDirector.SetGenericBinding(item, characterTransform);
					Vector3 position = transform.position;
					Vector3 vector = transform.GetComponent<CharacterMovement>().FacingDirection;
					characterTransform.position = position;
					characterTransform.GetComponent<CharacterMovement>().FacingDirection = vector;
					if (transform.name != "Player_CUT")
					{
						characterTransform.GetComponent<CircleCollider2D>().enabled = false;
					}
					list.Add(transform.gameObject);
				}
				else
				{
					_playableDirector.SetGenericBinding(item, transform);
					transform.GetComponent<CircleCollider2D>().enabled = false;
				}
			}
			foreach (GameObject item2 in list)
			{
				UnityEngine.Object.Destroy(item2);
			}
			list.Clear();
		}

		internal void ConsumeChoice()
		{
			choiceSelected = false;
		}

		public void Play()
		{
			if (!_playableDirector || _playableDirector.state == PlayState.Playing)
			{
				return;
			}
			if (ReloadsScene)
			{
				onEndUnityEvent.AddListener(SceneMaster.Instance.ReloadScene);
			}
			if (register)
			{
				GameDataManager.RegisterCutscene(base.gameObject.name);
			}
			while (ControllerManager.Instance.CurrentController.GetType() != typeof(PlayerController_HMD))
			{
				Debug.Log("Cutscene: NOT IN PLAYER CONTROLLER, YIELDING ULTILL IT IS");
				ControllerManager.Instance.YieldGameController();
			}
			if (pause)
			{
				PauseManager.Instance.PausePausables();
			}
			ControllerManager.Instance.OverrideGameController<TimelineDialogueController>();
			controller = ControllerManager.Instance.GetComponentInChildren<TimelineDialogueController>();
			// if (overrideDialogueSettings && dialogueOverrides.actorOverrides.Length != 0)
			// {
			// 	SetCharacterPanels();
			// }
			controller.SetSkip(skipGlyph, skippable, skipHoldTime);
			controller.OnSkipTimeline = SkipTimeline;
			onTimelineEnded = delegate
			{
				_effects.DisableZoomToFit();
				if (resetCamera)
				{
					_effects.EnableZoomToFit();
					_effects.CameraRetargetPlayer();
				}
				DBL.Log(DBL.Module.Timeline, "Timeline onEnd");
				GameDirector.Instance.Player.animator.ResetAnimancer();
				ControllerManager.Instance.YieldGameController();
				if (pause)
				{
					PauseManager.Instance.ResumePausables();
				}
				if (ReloadsScene || !destroyOnEnd)
				{
					SceneMaster.Instance.OnSceneHideFinish += delegate
					{
						_effects.ResetPlayerVisibility();
					};
				}
				else
				{
					_effects.ResetPlayerVisibility();
				}
				if (changeDialogueSnapshot)
				{
					// DialogueManager.instance.conversationStarted -= SetSnapshotOnConversationStarted;
					// DialogueManager.instance.conversationEnded -= SetSnapshotOnConversationEnd;
				}
				// if (overrideDialogueSettings && dialogueOverrides.actorOverrides.Length != 0)
				// {
				// 	ResetCharacterPanels();
				// }
				if (overrideBGM && StopOverridenBGM)
				{
					MusicPlayer.Instance.StopCurrentOverridenMusic();
				}
				onEndUnityEvent?.Invoke();
				if (destroyOnEnd)
				{
					UnityEngine.Object.Destroy(base.gameObject);
				}
			};
			if (changeDialogueSnapshot)
			{
				// DialogueManager.instance.conversationStarted += SetSnapshotOnConversationStarted;
				// DialogueManager.instance.conversationEnded += SetSnapshotOnConversationEnd;
			}
			MusicPlayer.Instance.SetSnapShot((!changeNormalSnapshot) ? MusicPlayer.SnapshotID.Normal : normalSnapshot);
			if (overrideBGM && !overrideEventBGM.IsNull)
			{
				MusicPlayer.Instance.PlayOverridenMusic(overrideEventBGM.Guid);
			}
			// AstralDialogueManager.Instance.SetDialogueMode(AstralDialogueManager.DialogueMode.Normal);
			_playableDirector.stopped += delegate
			{
				onTimelineEnded();
			};
			_playableDirector.Play();
		}

		private void SetSnapshotOnConversationStarted(Transform transform)
		{
			MusicPlayer.Instance.SetSnapShot(dialogueSnapshot);
		}

		private void SetSnapshotOnConversationEnd(Transform transform)
		{
			MusicPlayer.Instance.SetSnapShot((!changeNormalSnapshot) ? MusicPlayer.SnapshotID.Normal : normalSnapshot);
		}

		public static Transform GetCharacterTransform(string cName)
		{
			Transform transform = null;
			try
			{
				return (cName == "Player_CUT") ? GameObject.FindGameObjectWithTag("Player").transform : FindCharacter(cName);
			}
			catch (Exception)
			{
				DBL.Log(DBL.Module.Timeline, "<color=green><b>No character with given name!</b></color>");
				return null;
			}
		}

		private static Transform FindCharacter(string cName)
		{
			return GameObject.Find("NPCs").transform.Find(cName);
		}

		public void PauseTimeline()
		{
			_playableDirector.playableGraph.GetRootPlayable(0).SetSpeed(0.0);
		}

		public void ResumeTimeline()
		{
			if ((bool)_playableDirector && _playableDirector.playableGraph.IsValid())
			{
				_playableDirector.playableGraph.GetRootPlayable(0).SetSpeed(1.0);
			}
		}

		public bool DialogueNotExhausted()
		{
			return false;
		}

		public bool WaitingChoice()
		{
			return !choiceSelected;
		}

		public void SkipTimeline()
		{
			// DialogueManager.StopConversation();
			if (fullSkip)
			{
				_playableDirector.Stop();
			}
			else
			{
				double time = _playableDirector.duration - 0.3;
				_playableDirector.playableGraph.GetRootPlayable(0).SetSpeed(1.0);
				_playableDirector.time = time;
			}
			RuntimeManager.PlayOneShot(cutsceneSkip);
		}

		private void SetCharacterPanels()
		{
			switchedActors = new List<string>();
			// for (int i = 0; i < dialogueOverrides.actorOverrides.Length; i++)
			// {
				// AstralDialogueManager.Instance.SetActorStageSide(dialogueOverrides.actorOverrides[i], AstralDialogueManager.StageSide.Left);
				// switchedActors.Add(dialogueOverrides.actorOverrides[i]);
			// }
		}

		private void ResetCharacterPanels()
		{
			foreach (string switchedActor in switchedActors)
			{
				// AstralDialogueManager.Instance.SetActorStageSide(switchedActor, AstralDialogueManager.StageSide.Right);
			}
		}

		private void OnDisable()
		{
		}

		private void OnValidate()
		{
			if (base.gameObject.name != _lastName)
			{
				base.gameObject.name = base.gameObject.name.TrimEnd();
				_lastName = base.gameObject.name;
			}
			if (!(StartTransformPlayer != null))
			{
				return;
			}
			_playableDirector = GetComponent<PlayableDirector>();
			TimelineAsset timelineAsset = _playableDirector.playableAsset as TimelineAsset;
			if (!(timelineAsset != null))
			{
				return;
			}
			IEnumerable<TrackAsset> enumerable = from t in timelineAsset.GetOutputTracks()
				where t.GetType() == typeof(CharacterMovementTrack)
				select t;
			if (enumerable.ToArray().Length == 0)
			{
				return;
			}
			foreach (TrackAsset item in enumerable)
			{
				Transform transform = _playableDirector.GetGenericBinding(item) as Transform;
				if (!(transform == null) && transform.name == "Player")
				{
					transform.position = StartTransformPlayer.position;
				}
			}
		}
	}
}
