using System;
using System.Reflection;
using AstralShift.HellMaiden.Data;
using UnityEngine;

namespace AstralShift.HellMaiden.Combat.Hand
{
	/// <summary>
	/// Deferred legacy boundary used only by RuntimeShrine. Weapon, Equipment and
	/// PerkData runtime paths must use MonsterSupergroup.GAS.RuntimeModifierFactory.
	/// </summary>
	public sealed class LegacyShrineModifierFactory
	{
		private static LegacyShrineModifierFactory instance;

		public static LegacyShrineModifierFactory Instance =>
			instance ?? (instance = new LegacyShrineModifierFactory());

		public RuntimePerkModifier Create(PerkDataModifier dataModifier)
		{
			if (dataModifier == null)
			{
				throw new ArgumentNullException(nameof(dataModifier));
			}

			DataModifierResolver.BuildCache();
			PerkModifierID modifierID = dataModifier.ModifierID;
			if (!DataModifierResolver.TryGetPerkModifierClassTypeByID(
				modifierID,
				out Type type))
			{
				Debug.LogError(
					$"LegacyShrineModifierFactory: Unknown Shrine Modifier ID: " +
					$"{modifierID}");
				return null;
			}

			var runtimeModifier =
				(RuntimePerkModifier)Activator.CreateInstance(type);
			if (runtimeModifier == null)
			{
				return null;
			}

			if (!DataModifierResolver.TryGetPerkParamsClassTypeByID(
				modifierID,
				out Type parameterType) || parameterType == null)
			{
				return runtimeModifier;
			}

			object sourceParameters = dataModifier.Parameters;
			if (sourceParameters == null)
			{
				return runtimeModifier;
			}

			object runtimeParameters = Activator.CreateInstance(parameterType);
			DataModifierUtils.CopyModifierParams(
				sourceParameters,
				runtimeParameters);
			FieldInfo field =
				DataModifierResolver.PerkModifierParamsInstanceFieldById[modifierID];
			if (field == null)
			{
				Debug.LogError(
					"LegacyShrineModifierFactory: Modifier " + type.Name +
					" has no field marked with [InjectPerkModifierParams]");
				return runtimeModifier;
			}

			field.SetValue(runtimeModifier, runtimeParameters);
			runtimeModifier.ID = modifierID;
			return runtimeModifier;
		}
	}
}
