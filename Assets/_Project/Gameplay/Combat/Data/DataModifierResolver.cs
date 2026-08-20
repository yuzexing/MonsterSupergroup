using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.Combat.Hand;

namespace AstralShift.HellMaiden.Data
{
	public static class DataModifierResolver
	{
		private static bool _initialized;

		public static Dictionary<Type, FieldInfo[]> EquipmentModifierParamsTypeFields = new Dictionary<Type, FieldInfo[]>();

		public static Dictionary<Type, FieldInfo[]> PerkModifierParamsTypeFields = new Dictionary<Type, FieldInfo[]>();

		public static Type[] EquipmentModifierTypes { get; private set; }

		public static string[] EquipmentModifierDisplayNames { get; private set; }

		public static string[] EquipmentModifierNames { get; private set; }

		private static Dictionary<uint, Type> EquipmentModifierTypeById { get; set; }

		private static Dictionary<uint, Type> EquipmentModifierBaseTypeById { get; set; }

		private static Dictionary<uint, string> EquipmentModifierDisplayNameById { get; set; }

		private static Dictionary<uint, string> EquipmentModifierNameById { get; set; }

		private static Dictionary<uint, Type> EquipmentModifierParamsTypeById { get; set; }

		public static Dictionary<uint, FieldInfo> EquipmentModifierParamsInstanceFieldById { get; private set; }

		public static Type[] PerkModifierTypes { get; private set; }

		public static string[] PerkModifierDisplayNames { get; private set; }

		public static string[] PerkModifierNames { get; private set; }

		private static Dictionary<uint, Type> PerkModifierTypeById { get; set; }

		private static Dictionary<uint, Type> PerkModifierBaseTypeById { get; set; }

		private static Dictionary<uint, string> PerkModifierDisplayNameById { get; set; }

		private static Dictionary<uint, string> PerkModifierNameById { get; set; }

		private static Dictionary<uint, Type> PerkModifierParamsTypeById { get; set; }

		public static Dictionary<uint, FieldInfo> PerkModifierParamsInstanceFieldById { get; private set; }

		public static void BuildCache()
		{
			if (!_initialized)
			{
				_initialized = true;
				BuildEquipmentModifiersCache();
				BuildPerkModifiersCache();
			}
		}

		private static void BuildEquipmentModifiersCache()
		{
			HashSet<Type> targetBaseTypes = new HashSet<Type>
			{
				typeof(StaticStatModifier),
				typeof(DynamicStatModifier),
				typeof(DynamicOnDamageModifier),
				typeof(OnHitModifier),
				typeof(OnKillModifier)
			};
			var list = (from x in (from type in AppDomain.CurrentDomain.GetAssemblies().SelectMany(delegate(Assembly a)
					{
						try
						{
							return a.GetTypes();
						}
						catch
						{
							return Array.Empty<Type>();
						}
					})
					select new
					{
						Type = type,
						Attr = type.GetCustomAttribute<EquipmentModifierTypeAttribute>()
					} into typeAttributePair
					where typeAttributePair.Attr != null
					select typeAttributePair).Select(typeAttributePair =>
				{
					Type type = typeAttributePair.Type;
					EquipmentModifierTypeAttribute attr = typeAttributePair.Attr;
					string text = (string.IsNullOrEmpty(attr.DisplayName) ? type.Name : attr.DisplayName);
					uint id = DeterministicHash.Apply(type.AssemblyQualifiedName ?? type.FullName ?? type.Name);
					Type type2 = null;
					Type type3 = null;
					Type type4 = type;
					while (type4 != null && type4 != typeof(object))
					{
						if (type2 == null)
						{
							type2 = type4.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((Type n) => n.GetCustomAttribute<EquipmentModifierParamsAttribute>() != null);
						}
						if (type3 == null && targetBaseTypes.Contains(type4))
						{
							type3 = type4;
						}
						if (type2 != null && type3 != null)
						{
							break;
						}
						type4 = type4.BaseType;
					}
					FieldInfo instanceField = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((FieldInfo f) => f.GetCustomAttribute<InjectEquipmentModifierParamsAttribute>() != null);
					FieldInfo[] fieldsFromParamsType = GetFieldsFromParamsType(type2);
					return new
					{
						Id = id,
						Type = type,
						BaseType = type3,
						DisplayName = text,
						Name = text.Replace(" ", ""),
						ParamsType = type2,
						InstanceField = instanceField,
						ParamsTypeFields = fieldsFromParamsType
					};
				})
				orderby x.DisplayName
				select x).ToList();
			int count = list.Count;
			EquipmentModifierTypes = list.Select(x => x.Type).ToArray();
			EquipmentModifierDisplayNames = list.Select(x => x.DisplayName).ToArray();
			EquipmentModifierNames = list.Select(x => x.Name).ToArray();
			EquipmentModifierTypeById = new Dictionary<uint, Type>(count);
			EquipmentModifierBaseTypeById = new Dictionary<uint, Type>(count);
			EquipmentModifierDisplayNameById = new Dictionary<uint, string>(count);
			EquipmentModifierNameById = new Dictionary<uint, string>(count);
			EquipmentModifierParamsTypeById = new Dictionary<uint, Type>(count);
			EquipmentModifierParamsInstanceFieldById = new Dictionary<uint, FieldInfo>(count);
			EquipmentModifierParamsTypeFields = new Dictionary<Type, FieldInfo[]>();
			foreach (var item in list)
			{
				EquipmentModifierTypeById[item.Id] = item.Type;
				EquipmentModifierBaseTypeById[item.Id] = item.BaseType;
				EquipmentModifierDisplayNameById[item.Id] = item.DisplayName;
				EquipmentModifierNameById[item.Id] = item.Name;
				EquipmentModifierParamsTypeById[item.Id] = item.ParamsType;
				EquipmentModifierParamsInstanceFieldById[item.Id] = item.InstanceField;
				if (item.ParamsTypeFields != null && item.ParamsTypeFields.Length != 0)
				{
					EquipmentModifierParamsTypeFields.TryAdd(item.ParamsType, item.ParamsTypeFields);
				}
			}
		}

		public static bool TryGetEquipmentModifierClassTypeByID(uint id, out Type type)
		{
			BuildCache();
			if (EquipmentModifierTypeById != null)
			{
				return EquipmentModifierTypeById.TryGetValue(id, out type);
			}
			type = null;
			return false;
		}

		public static bool TryGetEquipmentBaseTypeByID(uint id, out Type baseType)
		{
			BuildCache();
			if (EquipmentModifierBaseTypeById != null)
			{
				return EquipmentModifierBaseTypeById.TryGetValue(id, out baseType);
			}
			baseType = null;
			return false;
		}

		public static bool TryGetEquipmentParamsClassTypeByID(uint id, out Type paramType)
		{
			BuildCache();
			if (EquipmentModifierParamsTypeById != null)
			{
				return EquipmentModifierParamsTypeById.TryGetValue(id, out paramType);
			}
			paramType = null;
			return false;
		}

		public static bool TryGetEquipmentDisplayName(uint id, out string display)
		{
			BuildCache();
			if (EquipmentModifierDisplayNameById != null)
			{
				return EquipmentModifierDisplayNameById.TryGetValue(id, out display);
			}
			display = null;
			return false;
		}

		private static void ClearEquipmentModifierCache()
		{
			EquipmentModifierTypes = null;
			EquipmentModifierDisplayNames = null;
			EquipmentModifierNames = null;
			EquipmentModifierTypeById = null;
			EquipmentModifierBaseTypeById = null;
			EquipmentModifierDisplayNameById = null;
			EquipmentModifierNameById = null;
			EquipmentModifierParamsTypeById = null;
			EquipmentModifierParamsInstanceFieldById = null;
			EquipmentModifierParamsTypeFields = null;
		}

		private static void BuildPerkModifiersCache()
		{
			HashSet<Type> targetBaseTypes = new HashSet<Type>
			{
				typeof(PlayerPerkModifier),
				typeof(WeaponStatsPerkModifier),
				typeof(PlayerConditionPerkModifier),
				typeof(EnemyConditionPerkModifier)
			};
			var list = (from x in (from type in AppDomain.CurrentDomain.GetAssemblies().SelectMany(delegate(Assembly a)
					{
						try
						{
							return a.GetTypes();
						}
						catch
						{
							return Array.Empty<Type>();
						}
					})
					select new
					{
						Type = type,
						Attr = type.GetCustomAttribute<PerkModifierTypeAttribute>()
					} into typeAttributePair
					where typeAttributePair.Attr != null
					select typeAttributePair).Select(typeAttributePair =>
				{
					Type type = typeAttributePair.Type;
					PerkModifierTypeAttribute attr = typeAttributePair.Attr;
					string text = (string.IsNullOrEmpty(attr.DisplayName) ? type.Name : attr.DisplayName);
					uint id = DeterministicHash.Apply(type.AssemblyQualifiedName ?? type.FullName ?? type.Name);
					Type type2 = null;
					Type type3 = null;
					Type type4 = type;
					while (type4 != null && type4 != typeof(object))
					{
						if (type2 == null)
						{
							type2 = type4.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((Type n) => n.GetCustomAttribute<PerkModifierParamsAttribute>() != null);
						}
						if (type3 == null && targetBaseTypes.Contains(type4))
						{
							type3 = type4;
						}
						if (type2 != null && type3 != null)
						{
							break;
						}
						type4 = type4.BaseType;
					}
					FieldInfo instanceField = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((FieldInfo f) => f.GetCustomAttribute<InjectPerkModifierParamsAttribute>() != null);
					FieldInfo[] fieldsFromParamsType = GetFieldsFromParamsType(type2);
					return new
					{
						Id = id,
						Type = type,
						BaseType = type3,
						DisplayName = text,
						Name = text.Replace(" ", ""),
						ParamsType = type2,
						InstanceField = instanceField,
						ParamsTypeFields = fieldsFromParamsType
					};
				})
				orderby x.DisplayName
				select x).ToList();
			int count = list.Count;
			PerkModifierTypes = list.Select(x => x.Type).ToArray();
			PerkModifierDisplayNames = list.Select(x => x.DisplayName).ToArray();
			PerkModifierNames = list.Select(x => x.Name).ToArray();
			PerkModifierTypeById = new Dictionary<uint, Type>(count);
			PerkModifierBaseTypeById = new Dictionary<uint, Type>(count);
			PerkModifierDisplayNameById = new Dictionary<uint, string>(count);
			PerkModifierNameById = new Dictionary<uint, string>(count);
			PerkModifierParamsTypeById = new Dictionary<uint, Type>(count);
			PerkModifierParamsInstanceFieldById = new Dictionary<uint, FieldInfo>(count);
			PerkModifierParamsTypeFields = new Dictionary<Type, FieldInfo[]>();
			foreach (var item in list)
			{
				PerkModifierTypeById[item.Id] = item.Type;
				PerkModifierBaseTypeById[item.Id] = item.BaseType;
				PerkModifierDisplayNameById[item.Id] = item.DisplayName;
				PerkModifierNameById[item.Id] = item.Name;
				PerkModifierParamsTypeById[item.Id] = item.ParamsType;
				PerkModifierParamsInstanceFieldById[item.Id] = item.InstanceField;
				if (item.ParamsTypeFields != null && item.ParamsTypeFields.Length != 0)
				{
					PerkModifierParamsTypeFields.TryAdd(item.ParamsType, item.ParamsTypeFields);
				}
			}
		}

		public static bool TryGetPerkModifierClassTypeByID(uint id, out Type type)
		{
			BuildCache();
			if (PerkModifierTypeById != null)
			{
				return PerkModifierTypeById.TryGetValue(id, out type);
			}
			type = null;
			return false;
		}

		public static bool TryGetPerkModifierIDByClassType(Type type, out uint id)
		{
			BuildCache();
			if (PerkModifierTypeById != null)
			{
				foreach (KeyValuePair<uint, Type> item in PerkModifierBaseTypeById)
				{
					if (item.Value == type)
					{
						id = item.Key;
						return true;
					}
				}
			}
			id = 0u;
			return false;
		}

		public static bool TryGetPerkBaseTypeByID(uint id, out Type baseType)
		{
			BuildCache();
			if (PerkModifierBaseTypeById != null)
			{
				return PerkModifierBaseTypeById.TryGetValue(id, out baseType);
			}
			baseType = null;
			return false;
		}

		public static bool TryGetPerkParamsClassTypeByID(uint id, out Type paramType)
		{
			BuildCache();
			if (PerkModifierParamsTypeById != null)
			{
				return PerkModifierParamsTypeById.TryGetValue(id, out paramType);
			}
			paramType = null;
			return false;
		}

		public static bool TryGetPerkDisplayName(uint id, out string display)
		{
			BuildCache();
			if (PerkModifierDisplayNameById != null)
			{
				return PerkModifierDisplayNameById.TryGetValue(id, out display);
			}
			display = null;
			return false;
		}

		private static void ClearPerkModifierCache()
		{
			PerkModifierTypes = null;
			PerkModifierDisplayNames = null;
			PerkModifierNames = null;
			PerkModifierTypeById = null;
			PerkModifierBaseTypeById = null;
			PerkModifierDisplayNameById = null;
			PerkModifierNameById = null;
			PerkModifierParamsTypeById = null;
			PerkModifierParamsInstanceFieldById = null;
			PerkModifierParamsTypeFields = null;
		}

		private static FieldInfo[] GetFieldsFromParamsType(Type type)
		{
			List<FieldInfo> list = new List<FieldInfo>();
			Type type2 = type;
			while (type2 != null && type2 != typeof(object))
			{
				BindingFlags bindingAttr = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
				IEnumerable<FieldInfo> collection = from f in type2.GetFields(bindingAttr)
					where !f.Name.StartsWith("<") && !f.IsInitOnly
					select f;
				list.InsertRange(0, collection);
				type2 = type2.BaseType;
			}
			return list.ToArray();
		}

		private static void ClearCache()
		{
			_initialized = false;
			ClearEquipmentModifierCache();
			ClearPerkModifierCache();
		}
	}
}
