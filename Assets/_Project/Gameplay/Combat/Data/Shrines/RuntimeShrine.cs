using System;
using System.Collections.Generic;
using System.Threading;
using AstralShift.HellMaiden.Combat.Hand;
using AstralShift.Managers;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AstralShift.HellMaiden.Data.Shrines
{
	public class RuntimeShrine
	{
		private float _remainingDuration;

		private float _tempApplyTimestamp;

		private List<RuntimePerkModifier> _cachedModifiers;

		private CancellationTokenSource _timeoutCancellationSource;

		public ShrineData ShrineData { get; private set; }

		public bool IsTemporary { get; private set; }

		public float TotalDuration => ShrineData.duration;

		public int ModifiersCount { get; private set; }

		public RuntimeShrine(ShrineData data)
		{
			ShrineData = data;
			IsTemporary = !ShrineData.permanent;
			ModifiersCount = 0;
			_timeoutCancellationSource = new CancellationTokenSource();
		}

		public void Add()
		{
			if (ShrineData.Modifiers == null || ShrineData.Modifiers.Count == 0)
			{
				return;
			}
			foreach (PerkDataModifier modifier in ShrineData.Modifiers)
			{
				if (modifier != null)
				{
					RuntimePerkModifier runtimeModifierFromPerkData = RuntimeModifierFactory.Instance.GetRuntimeModifierFromPerkData(modifier);
					StackAndApplyModifier(runtimeModifierFromPerkData);
				}
			}
		}

		public void AddTemporary(Action<RuntimeShrine> onRemoveAction)
		{
			if (ShrineData.Modifiers == null || ShrineData.Modifiers.Count == 0)
			{
				return;
			}
			foreach (PerkDataModifier modifier in ShrineData.Modifiers)
			{
				if (modifier != null)
				{
					RuntimePerkModifier runtimeModifierFromPerkData = RuntimeModifierFactory.Instance.GetRuntimeModifierFromPerkData(modifier);
					ApplyTemporaryModifier(runtimeModifierFromPerkData, onRemoveAction);
				}
			}
		}

		public float GetAtIndexModifierParameterValue(int index)
		{
			if (ShrineData.Modifiers == null || index < 0 || index >= ShrineData.Modifiers.Count)
			{
				return 0f;
			}
			PerkDataModifier perkDataModifier = ShrineData.Modifiers[index];
			if (perkDataModifier == null)
			{
				return 0f;
			}
			return perkDataModifier.GetParameterByIndex(0) * (float)ModifiersCount;
		}

		private void StackAndApplyModifier(RuntimePerkModifier modifier)
		{
			if (modifier == null)
			{
				return;
			}
			ModifiersCount++;
			RemoveModifiers();
			if (_cachedModifiers == null)
			{
				_cachedModifiers = new List<RuntimePerkModifier>();
			}
			_cachedModifiers.Add(modifier);
			_cachedModifiers = StackModifiers(_cachedModifiers);
			foreach (RuntimePerkModifier cachedModifier in _cachedModifiers)
			{
				GameDirector.Instance.Player.PlayerStats.AddModifier(cachedModifier);
			}
			GameDirector.Instance.Player.PlayerStats.EvaluateModifiers();
		}

		private List<RuntimePerkModifier> StackModifiers(List<RuntimePerkModifier> modifiers)
		{
			List<RuntimePerkModifier> list = new List<RuntimePerkModifier>();
			foreach (RuntimePerkModifier modifier in modifiers)
			{
				bool flag = false;
				foreach (RuntimePerkModifier item in list)
				{
					if (item.TryStack(modifier))
					{
						flag = true;
						break;
					}
				}
				if (!flag)
				{
					list.Add(modifier);
				}
			}
			return list;
		}

		private void ApplyTemporaryModifier(RuntimePerkModifier modifier, Action<RuntimeShrine> onRemoveAction)
		{
			ModifiersCount++;
			if (_cachedModifiers == null)
			{
				_cachedModifiers = new List<RuntimePerkModifier>();
			}
			_cachedModifiers.Add(modifier);
			AddModifier(modifier);
			ScheduleModifierTimeout(modifier, onRemoveAction).Forget();
		}

		private async UniTaskVoid ScheduleModifierTimeout(RuntimePerkModifier modifier, Action<RuntimeShrine> onRemoveAction)
		{
			try
			{
				for (_remainingDuration = ShrineData.duration; _remainingDuration > 0f; _remainingDuration -= (PauseManager.Instance.IsPaused ? 0f : Time.deltaTime))
				{
					await UniTask.NextFrame(PlayerLoopTiming.Update, _timeoutCancellationSource.Token);
				}
				_remainingDuration = 0f;
				RemoveTemporaryModifier(modifier);
				onRemoveAction?.Invoke(this);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		public float GetRemainingTime()
		{
			return _remainingDuration;
		}

		public void CancelTimeout()
		{
			_remainingDuration = 0f;
			_timeoutCancellationSource?.Cancel();
			_timeoutCancellationSource?.Dispose();
			_timeoutCancellationSource = null;
		}

		public void RemoveTemporaryModifier(RuntimePerkModifier modifier)
		{
			RemoveModifier(modifier);
			_cachedModifiers.Remove(modifier);
			ModifiersCount--;
		}

		private void RemoveModifiers()
		{
			if (_cachedModifiers == null || _cachedModifiers.Count == 0)
			{
				return;
			}
			foreach (RuntimePerkModifier cachedModifier in _cachedModifiers)
			{
				RemoveModifier(cachedModifier);
			}
		}

		private void AddModifier(RuntimePerkModifier modifier)
		{
			GameDirector.Instance.Player.PlayerStats.AddModifier(modifier);
			GameDirector.Instance.Player.EffectVisualsResolver.ApplyEffect(modifier.ID);
		}

		private void RemoveModifier(RuntimePerkModifier modifier)
		{
			GameDirector.Instance.Player.PlayerStats.RemoveModifier(modifier);
			GameDirector.Instance.Player.EffectVisualsResolver.RemoveEffect(modifier.ID);
		}
	}
}
