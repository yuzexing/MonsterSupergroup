using System;
using UnityEditor;

namespace MonsterSupergroup.GAS.Editor
{
    public static class ModifierSelectionService
    {
        public const string ModifierIdFieldName = "modifierId";
        public const string ParametersFieldName = "parameters";

        public static bool RequiresParameterReplacement(SerializedProperty modifierProperty, ModifierDescriptor descriptor)
        {
            SerializedProperty parameters = RequireRelative(modifierProperty, ParametersFieldName);
            object current = parameters.managedReferenceValue;
            return current != null && current.GetType() != descriptor.ParametersType;
        }

        public static void Assign(SerializedProperty modifierProperty, ModifierDescriptor descriptor)
        {
            if (modifierProperty == null)
            {
                throw new ArgumentNullException(nameof(modifierProperty));
            }

            SerializedProperty id = RequireRelative(modifierProperty, ModifierIdFieldName);
            SerializedProperty parameters = RequireRelative(modifierProperty, ParametersFieldName);
            id.uintValue = descriptor.Id;
            if (parameters.managedReferenceValue == null || parameters.managedReferenceValue.GetType() != descriptor.ParametersType)
            {
                parameters.managedReferenceValue = Activator.CreateInstance(descriptor.ParametersType);
            }

            modifierProperty.serializedObject.ApplyModifiedProperties();
        }

        private static SerializedProperty RequireRelative(SerializedProperty property, string name)
        {
            if (property == null)
            {
                throw new ArgumentNullException(nameof(property));
            }

            SerializedProperty relative = property.FindPropertyRelative(name);
            if (relative == null)
            {
                throw new ArgumentException($"Property '{property.propertyPath}' has no child named '{name}'.", nameof(property));
            }

            return relative;
        }
    }
}
