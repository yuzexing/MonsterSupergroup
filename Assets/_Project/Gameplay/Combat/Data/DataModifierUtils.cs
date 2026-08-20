using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEngine;

namespace AstralShift.HellMaiden.Data
{
	public static class DataModifierUtils
	{
		private const string ATKSUnitKey = "STT_ATKs";

		private const int UnitSize = 25;

		private static StringBuilder _tempStringBuilder = new StringBuilder();

		public static void CopyModifierParams(object source, object destination)
		{
			BindingFlags bindingAttr = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			FieldInfo[] fields = source.GetType().GetFields(bindingAttr);
			foreach (FieldInfo fieldInfo in fields)
			{
				if (!fieldInfo.Name.StartsWith("<") && !fieldInfo.IsInitOnly)
				{
					fieldInfo.SetValue(destination, fieldInfo.GetValue(source));
				}
			}
		}

		public static object GetModifierParamByIndex(object source, int idx)
		{
			Type type = source.GetType();
			if (DataModifierResolver.EquipmentModifierParamsTypeFields.TryGetValue(type, out var value))
			{
				if (value.Length <= idx)
				{
					return null;
				}
				return value[idx].GetValue(source);
			}
			if (DataModifierResolver.PerkModifierParamsTypeFields.TryGetValue(type, out value))
			{
				if (value.Length <= idx)
				{
					return null;
				}
				return value[idx].GetValue(source);
			}
			return null;
		}

		public static string FormatMultiplierToPercentage(float multiplier)
		{
			return (multiplier * 100f).ToString("0.##", CultureInfo.InvariantCulture);
		}

		public static string FormatAbsoluteValue(float value)
		{
			return value.ToString("0.##", CultureInfo.InvariantCulture);
		}

		public static string FormatStatChange(float value, AttackStatType statType, bool appendUnits = true)
		{
			return FormatStatChangeValue(value, statType, value > 0f, appendUnits);
		}

		public static string FormatTotalValue(float value, AttackStatType statType, bool appendUnits = true)
		{
			_tempStringBuilder.Clear();
			switch (statType)
			{
			case AttackStatType.Damage:
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, $"{Mathf.CeilToInt(value)}");
				break;
			case AttackStatType.Speed:
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, "{0:0.##}", value);
				if (appendUnits)
				{
					_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, string.Format(" <size={0}>{1}</size>", 25, LocalizationMediator.GetTranslation("STT_ATKs")));
				}
				break;
			case AttackStatType.Size:
				if (appendUnits)
				{
					_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, $"<size={25}>x</size>");
				}
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, " {0:0.##}", value);
				break;
			case AttackStatType.Duration:
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, "{0:0.##}", value);
				if (appendUnits)
				{
					_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, $" <size={25}>s</size>");
				}
				break;
			case AttackStatType.CritDamage:
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, "{0:0.##}", value * 100f);
				if (appendUnits)
				{
					_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, $" <size={25}>%</size>");
				}
				break;
			case AttackStatType.CritRate:
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, "{0:0.##}", value * 100f);
				if (appendUnits)
				{
					_tempStringBuilder.AppendFormat($" <size={25}>%</size>");
				}
				break;
			case AttackStatType.ProjectileCount:
				return $"{Mathf.Abs(value)}";
			}
			return _tempStringBuilder.ToString();
		}

		private static string FormatStatChangeValue(float value, AttackStatType statType, bool isPositive, bool appendUnits = true)
		{
			_tempStringBuilder.Clear();
			value = Mathf.Abs(value);
			switch (statType)
			{
			case AttackStatType.Damage:
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, isPositive ? "+" : "-");
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, $" {Mathf.CeilToInt(value)}");
				break;
			case AttackStatType.Speed:
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, isPositive ? "+" : "-");
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, " {0:0.##}", value);
				if (appendUnits)
				{
					_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, string.Format(" <size={0}>{1}</size>", 25, LocalizationMediator.GetTranslation("STT_ATKs")));
				}
				break;
			case AttackStatType.Size:
				if (appendUnits)
				{
					_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, $"<size={25}>x</size> ");
				}
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, isPositive ? "+" : "-");
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, " {0:0.##}", value);
				break;
			case AttackStatType.Duration:
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, isPositive ? "+" : "-");
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, " {0:0.##}", value);
				if (appendUnits)
				{
					_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, $" <size={25}>s</size>");
				}
				break;
			case AttackStatType.CritDamage:
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, isPositive ? "+" : "-");
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, " {0:0.##}", value * 100f);
				if (appendUnits)
				{
					_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, $" <size={25}>%</size>");
				}
				break;
			case AttackStatType.CritRate:
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, isPositive ? "+" : "-");
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, " {0:0.0#}", value * 100f);
				if (appendUnits)
				{
					_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, $" <size={25}>%</size>");
				}
				break;
			case AttackStatType.ProjectileCount:
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, isPositive ? "+" : "-");
				_tempStringBuilder.AppendFormat(CultureInfo.InvariantCulture, $" {Mathf.Abs(value)}");
				break;
			}
			return _tempStringBuilder.ToString();
		}
	}
}
