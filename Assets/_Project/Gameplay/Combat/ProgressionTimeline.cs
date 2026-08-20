using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AstralShift.DebugTools;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.Combat.Spawners;
using AstralShift.HellMaiden.MapGeneration;
using AstralShift.HellMaiden.Quests;
using AstralShift.HellMaiden.Timeline.Progression;
using AstralShift.HellMaiden.Timeline.Progression.Quests;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AstralShift.HellMaiden.Combat
{
	public class ProgressionTimeline : MonoBehaviour
	{
		public PlayableDirector playableDirector;

		private TimelineAsset timeline;

		public List<DivinaQuestGoal> Quests = new List<DivinaQuestGoal>();

		public EnemyContinuousSpawner enemyContinuousSpawner;

		public ContinuousLimitedEnemySpawner enemyContinuousLimitedSpawner;

		public ContinuousWaveDefenceSpawner enemyContinuousWaveDefenceSpawner;

		public MultipleEnemySpawner multipleEnemySpawner;

		public DirectionalEnemySpawner directionalEnemySpawner;

		public BarrierTrapSpawner barrierTrapSpawner;

		public QuestSpawner questSpawner;

		public BossSpawner bossSpawner;

		public ItemSpawner itemSpawner;

		public PropReplacerManager propReplacerManagerPrefab;

		public DirectionalGustsOfWind windSpawner;

		public List<Milestone> milestones;

		private List<Milestone> _activeMilestones;

		private readonly List<int> _toRemoveMilestoneIndices = new List<int>();

		private int _currentMilestone;

		[SerializeField]
		private bool isTimeoutEnabled;

		[SerializeField]
		private float timeoutDuration = 60f;

		private float _startTime;

		private float _currentTime;

		private float _timeScaling;

		private bool _timelineIsPaused;

		private CancellationTokenSource _cts = new CancellationTokenSource();

		public bool IsTimeoutEnabled => isTimeoutEnabled;

		public float TimeoutDuration => timeoutDuration;

		public PropReplacerManager PropReplacerManagerInstance { get; private set; }

		public float CurrentTime => _currentTime;

		public float EndTime => (float)timeline.duration;

		public event Action OnTimelineEnd;

		public void Init()
		{
			milestones.Clear();
			if (playableDirector == null)
			{
				throw new Exception("PlayableDirector is not assigned.");
			}
			timeline = playableDirector.playableAsset as TimelineAsset;
			if (timeline == null)
			{
				throw new Exception("PlayableDirector does not have a TimelineAsset assigned.");
			}
			ProgressionManager.ProgressionStack.Push(this);
		}

		public void InitQuests()
		{
			foreach (TrackAsset outputTrack in timeline.GetOutputTracks())
			{
				if (outputTrack.muted || !(outputTrack is QuestSpawnerTrack))
				{
					continue;
				}
				foreach (TimelineClip clip in outputTrack.GetClips())
				{
					if (clip.asset is QuestSpawnerClip)
					{
						QuestSpawnerClip obj = clip.asset as QuestSpawnerClip;
						QuestSpawner questSpawner = UnityEngine.Object.Instantiate(this.questSpawner);
						Milestone milestone = new Milestone((float)clip.start, (float)clip.end, questSpawner);
						questSpawner.startTime = milestone.startTime;
						questSpawner.endTime = milestone.endTime;
						milestones.Add(milestone);
						DivinaQuestGoal divinaQuestGoal = (questSpawner.Quest = UnityEngine.Object.Instantiate(obj.quest, base.transform));
						Quests.Add(divinaQuestGoal);
						DBL.Log(DBL.Module.ProgressionTimeline, "Quest Processed: " + divinaQuestGoal.name);
					}
				}
			}
		}

		public void StartProgression(float startTime, float timeScaling)
		{
			_activeMilestones = new List<Milestone>();
			_startTime = startTime;
			_currentTime = _startTime;
			_timeScaling = timeScaling;
			PropReplacerManagerInstance = UnityEngine.Object.Instantiate(propReplacerManagerPrefab);
			PropReplacerManagerInstance.InitializePropReplacerPrefabs();
			ProcessEventMarkers();
			ProcessSpawnerClips();
			ProcessTracks();
			SortMilestones();
			PropReplacerManagerInstance.SortPropPlacerRequests();
		}

		private void ProcessEventMarkers()
		{
			if (!timeline.markerTrack || timeline.markerTrack.muted)
			{
				return;
			}
			IEnumerable<ProgressionEventMarker> enumerable = timeline.markerTrack.GetMarkers()?.Select((IMarker element) => element as ProgressionEventMarker);
			if (enumerable == null)
			{
				return;
			}
			foreach (ProgressionEventMarker item in enumerable)
			{
				if (!(item == null))
				{
					item.ProcessEvent(this);
					DBL.Log(DBL.Module.ProgressionTimeline, $"Progression Event Processed: {item.GetType()} at time {item.time}");
				}
			}
		}

		private void ProcessSpawnerClips()
		{
			foreach (TrackAsset outputTrack in timeline.GetOutputTracks())
			{
				if (outputTrack.muted)
				{
					continue;
				}
				foreach (TimelineClip clip in outputTrack.GetClips())
				{
					if (clip.asset is IProgressionClip progressionClip)
					{
						progressionClip.ProcessClip(this, clip);
						DBL.Log(DBL.Module.ProgressionTimeline, $"Progression Clip Processed: {clip.asset.GetType()} at time {clip.start} / {clip.end}");
					}
				}
			}
		}

		private void ProcessTracks()
		{
			foreach (TrackAsset outputTrack in timeline.GetOutputTracks())
			{
				if (!outputTrack.muted && outputTrack is IProgressionTrack progressionTrack)
				{
					progressionTrack.ProcessTrack(this);
					DBL.Log(DBL.Module.ProgressionTimeline, $"Progression Track Processed: {outputTrack.GetType()}");
				}
			}
		}

		public void CreateMilestone<T>(T progressable, TimelineClip clip) where T : IProgressable
		{
			Milestone milestone = new Milestone((float)clip.start, (float)clip.end, progressable, 1);
			float startTime = milestone.startTime;
			progressable.startTime = startTime;
			float endTime = milestone.endTime;
			progressable.endTime = endTime;
			milestones.Add(milestone);
		}

		public void CreateMilestone<T>(T progressable, ProgressionEventMarker marker) where T : IProgressable
		{
			Milestone milestone = new Milestone((float)marker.time, EndTime, progressable, 1);
			float startTime = milestone.startTime;
			progressable.startTime = startTime;
			float endTime = milestone.endTime;
			progressable.endTime = endTime;
			milestones.Add(milestone);
		}

		public void CreateMilestone<T>(T progressable, float startTime, float endTime) where T : IProgressable
		{
			Milestone milestone = new Milestone(startTime, endTime, progressable, 1);
			float startTime2 = milestone.startTime;
			progressable.startTime = startTime2;
			float endTime2 = milestone.endTime;
			progressable.endTime = endTime2;
			milestones.Add(milestone);
		}

		private void SortMilestones()
		{
			milestones.Sort((Milestone a, Milestone b) => a.startTime.CompareTo(b.startTime));
		}

		private void EvaluateMilestones()
		{
			EvaluateNextMilestones();
			ProgressMilestones();
			EvaluateEndingMilestones();
		}

		private void EvaluateNextMilestones()
		{
			if (_currentMilestone < milestones.Count && milestones[_currentMilestone].startTime < _currentTime)
			{
				milestones[_currentMilestone].progressable.Init();
				_activeMilestones.Add(milestones[_currentMilestone]);
				_currentMilestone++;
				EvaluateNextMilestones();
			}
		}

		private void EvaluateEndingMilestones()
		{
			_toRemoveMilestoneIndices.Clear();
			for (int num = _activeMilestones.Count - 1; num >= 0; num--)
			{
				Milestone milestone = _activeMilestones[num];
				if (milestone.progressable.hasEnded)
				{
					milestone.progressable.End();
					_toRemoveMilestoneIndices.Add(_activeMilestones.IndexOf(milestone));
				}
			}
			foreach (int toRemoveMilestoneIndex in _toRemoveMilestoneIndices)
			{
				_activeMilestones.RemoveAt(toRemoveMilestoneIndex);
			}
			List<Milestone> list = milestones;
			if (list[list.Count - 1].progressable.hasEnded)
			{
				EndTimeline();
			}
		}

		public void ProgressMilestones()
		{
			for (int i = 0; i < _activeMilestones.Count; i++)
			{
				Milestone milestone = _activeMilestones[i];
				if (_currentTime - milestone.lastUpdate >= (float)milestone.updateInterval)
				{
					milestone.progressable.ProgressUpdate();
					milestone.lastUpdate = _currentTime;
				}
			}
		}

		public float GetDuration()
		{
			return (float)playableDirector.duration;
		}

		private void LateUpdate()
		{
			if (_activeMilestones != null && !_timelineIsPaused)
			{
				_currentTime += _timeScaling * Time.deltaTime;
				EvaluateMilestones();
			}
		}

		public void PauseAllMilestones()
		{
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = null;
			for (int num = _activeMilestones.Count - 1; num >= 0; num--)
			{
				_activeMilestones[num].progressable.PauseProgressable();
			}
			EnemyAIManager.Instance.DeactivateAllEnemies();
			_timelineIsPaused = true;
		}

		public async void ResumeAllMilestones()
		{
			try
			{
				_cts = new CancellationTokenSource();
				await UniTask.DelayFrame(1, PlayerLoopTiming.Update, _cts.Token);
				for (int num = _activeMilestones.Count - 1; num >= 0; num--)
				{
					_activeMilestones[num].progressable.ResumeProgressable();
				}
				EnemyAIManager.Instance?.ActivateAllEnemies();
				_timelineIsPaused = false;
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception message)
			{
				Debug.LogError(message);
			}
		}

		public void Pause()
		{
			PauseAllMilestones();
		}

		public void Resume()
		{
			ResumeAllMilestones();
		}

		public void KillAllEnemies()
		{
			_activeMilestones.ForEach(delegate(Milestone am)
			{
				if (am.progressable is EnemySpawner enemySpawner)
				{
					enemySpawner.KillAllEnemies();
				}
			});
		}

		public void EndTimeline()
		{
			ProgressionManager.ProgressionStack.Pop();
			base.enabled = false;
			this.OnTimelineEnd?.Invoke();
			this.OnTimelineEnd = null;
		}

		private void OnDestroy()
		{
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = null;
		}
	}
}
