using System;
using System.Collections.Generic;
using System.Linq;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	[EquipmentModifierType("Stat Swap Chance Modifier")]
	public class StatSwapModifier : DynamicStatModifier
	{
		[EquipmentModifierParams]
		private class ParamsData
		{
			public float chance;

			public float multiplierIncrement;
		}

		[Serializable]
		public struct StatsRemap
		{
			public AttackStatType stat1;

			public AttackStatType stat2;

			public bool swap;

			public float multiplierIncrement;
		}

		[InjectEquipmentModifierParams]
		private ParamsData _parameters;

		private WeaponBehaviourStats _stats;

		private static readonly AttackStatType[] ValidStats = new AttackStatType[6]
		{
			AttackStatType.Damage,
			AttackStatType.Size,
			AttackStatType.Speed,
			AttackStatType.Duration,
			AttackStatType.CritRate,
			AttackStatType.CritDamage
		};

		private static bool _isRemapCacheGenerated = false;

		private static StatsRemap[][] _statsRemaps;

		private static Dictionary<LinkedListNode<PlayerHandSlot>, StatsRemap[]> _slotStatsRemaps;

		public StatSwapModifier()
		{
			if (!_isRemapCacheGenerated)
			{
				GenerateStatsRemapCache();
				_isRemapCacheGenerated = true;
			}
		}

		public override int GetSortPriority()
		{
			return int.MaxValue;
		}

		public override void Apply(WeaponBehaviourStats stats, WeaponBehaviour weapon)
		{
			_stats = stats;
			if (UnityEngine.Random.Range(0f, 1f) <= _parameters.chance)
			{
				RandomStatSwap(stats);
			}
			else
			{
				ResetStatsRemaps();
			}
		}

		private void RandomStatSwap(WeaponBehaviourStats stats)
		{
			StatsRemap[] array = _slotStatsRemaps[_sourceSlotNode];
			for (int i = 0; i < array.Length; i++)
			{
				if (array[i].swap)
				{
					_stats.RemapStat(array[i].stat1, array[i].stat2);
					_stats.RemapStat(array[i].stat2, array[i].stat1);
					IncrementMultipliers(stats, array[i]);
				}
				else
				{
					_stats.RemapStat(array[i].stat1, array[i].stat2);
				}
			}
		}

		private void IncrementMultipliers(WeaponBehaviourStats stats, StatsRemap remap)
		{
			IncrementStatMultiplier(stats, remap.stat1, remap.multiplierIncrement);
			IncrementStatMultiplier(stats, remap.stat2, remap.multiplierIncrement);
		}

		private void IncrementStatMultiplier(WeaponBehaviourStats stats, AttackStatType stat, float multiplierIncrement)
		{
			AttackStatsMultipliers baseStatsMultipliers = stats.BaseStatsMultipliers;
			AttackStatsMultipliers dynamicStatsMultipliers = stats.DynamicStatsMultipliers;
			switch (stat)
			{
			case AttackStatType.Damage:
				if (baseStatsMultipliers.damage != 0f || dynamicStatsMultipliers.damage != 0f)
				{
					dynamicStatsMultipliers.damage += _parameters.multiplierIncrement;
				}
				break;
			case AttackStatType.Size:
				if (baseStatsMultipliers.size != 0f || dynamicStatsMultipliers.size != 0f)
				{
					dynamicStatsMultipliers.size += _parameters.multiplierIncrement;
				}
				break;
			case AttackStatType.Speed:
				if (baseStatsMultipliers.speed != 0f || dynamicStatsMultipliers.speed != 0f)
				{
					dynamicStatsMultipliers.speed += _parameters.multiplierIncrement;
				}
				break;
			case AttackStatType.Duration:
				if (baseStatsMultipliers.duration != 0f || dynamicStatsMultipliers.duration != 0f)
				{
					dynamicStatsMultipliers.duration += _parameters.multiplierIncrement;
				}
				break;
			case AttackStatType.CritRate:
				if (baseStatsMultipliers.critRate != 0f || dynamicStatsMultipliers.critRate != 0f)
				{
					dynamicStatsMultipliers.critRate += _parameters.multiplierIncrement;
				}
				break;
			case AttackStatType.CritDamage:
				if (baseStatsMultipliers.critDamage != 0f || dynamicStatsMultipliers.critDamage != 0f)
				{
					dynamicStatsMultipliers.critDamage += _parameters.multiplierIncrement;
				}
				break;
			case AttackStatType.ProjectileCount:
				break;
			}
		}

		public override void Apply(AttackStatsMultipliers multipliers, WeaponBehaviour weapon)
		{
		}

		private void ResetStatsRemaps()
		{
			_stats?.ResetStatRemaps();
		}

		public override void Dispose()
		{
			ResetStatsRemaps();
		}

		private void GenerateStatsRemapCache()
		{
			if (_slotStatsRemaps == null)
			{
				_slotStatsRemaps = new Dictionary<LinkedListNode<PlayerHandSlot>, StatsRemap[]>();
			}
			_statsRemaps = new StatsRemap[4][];
			for (int i = 0; i < _statsRemaps.Length; i++)
			{
				_statsRemaps[i] = GetNewStatsRemaps();
			}
			LinkedListNode<PlayerHandSlot> linkedListNode = PlayerHand.Instance.Slots.First;
			int num = 0;
			while (linkedListNode != null && num < _statsRemaps.Length)
			{
				_slotStatsRemaps.Add(linkedListNode, _statsRemaps[num]);
				linkedListNode = linkedListNode.Next;
				num++;
			}
			PlayerHand.Instance.OnReset += DisposeRemapCache;
		}

		private StatsRemap[] GetNewStatsRemaps()
		{
			List<StatsRemap> list = new List<StatsRemap>();
			List<AttackStatType> list2 = ValidStats.ToList();
			while (list2.Count >= 2)
			{
				int index = UnityEngine.Random.Range(0, list2.Count);
				AttackStatType stat = list2[index];
				list2.RemoveAt(index);
				int index2 = UnityEngine.Random.Range(0, list2.Count);
				AttackStatType stat2 = list2[index2];
				list2.RemoveAt(index2);
				list.Add(new StatsRemap
				{
					stat1 = stat,
					stat2 = stat2,
					swap = true
				});
			}
			return list.ToArray();
		}

		private void DisposeRemapCache()
		{
			if (PlayerHand.Instance != null)
			{
				PlayerHand.Instance.OnReset -= DisposeRemapCache;
			}
			_slotStatsRemaps?.Clear();
			_statsRemaps = null;
			_slotStatsRemaps = null;
			_isRemapCacheGenerated = false;
		}
	}
}
