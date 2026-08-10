using System;
using System.Collections.Generic;
using MonsterSupergroup.GAS.Authoring;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.GAS.Editor
{
    public abstract class ModifierDataDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;

        protected abstract IReadOnlyList<ModifierDescriptor> Descriptors { get; }
        protected abstract bool TryGetDescriptor(uint id, out ModifierDescriptor descriptor);

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            SerializedProperty id = property.FindPropertyRelative(ModifierSelectionService.ModifierIdFieldName);
            SerializedProperty parameters = property.FindPropertyRelative(ModifierSelectionService.ParametersFieldName);
            Rect selectorRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            string selectedName = TryGetDescriptor(id.uintValue, out ModifierDescriptor selected)
                ? selected.DisplayName
                : id.uintValue == 0 ? "Select modifier..." : $"Unknown ({id.uintValue})";

            Rect buttonRect = EditorGUI.PrefixLabel(selectorRect, label);
            if (EditorGUI.DropdownButton(buttonRect, new GUIContent(selectedName), FocusType.Keyboard))
            {
                ShowMenu(property, parameters);
            }

            Rect parametersRect = new Rect(
                position.x,
                selectorRect.yMax + Spacing,
                position.width,
                Math.Max(EditorGUIUtility.singleLineHeight, EditorGUI.GetPropertyHeight(parameters, true)));
            if (parameters.managedReferenceValue == null)
            {
                EditorGUI.HelpBox(parametersRect, "Select a modifier to create its parameters.", MessageType.Info);
            }
            else
            {
                EditorGUI.PropertyField(parametersRect, parameters, new GUIContent("Parameters"), true);
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            SerializedProperty parameters = property.FindPropertyRelative(ModifierSelectionService.ParametersFieldName);
            float parameterHeight = parameters.managedReferenceValue == null
                ? EditorGUIUtility.singleLineHeight
                : EditorGUI.GetPropertyHeight(parameters, true);
            return EditorGUIUtility.singleLineHeight + Spacing + parameterHeight;
        }

        private void ShowMenu(SerializedProperty property, SerializedProperty parameters)
        {
            var menu = new GenericMenu();
            foreach (ModifierDescriptor descriptor in Descriptors)
            {
                ModifierDescriptor captured = descriptor;
                menu.AddItem(new GUIContent(captured.DisplayName), false, () => Select(property, parameters, captured));
            }

            if (Descriptors.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No registered modifiers"));
            }

            menu.ShowAsContext();
        }

        private static void Select(SerializedProperty property, SerializedProperty parameters, ModifierDescriptor descriptor)
        {
            if (parameters.managedReferenceValue != null &&
                parameters.managedReferenceValue.GetType() != descriptor.ParametersType &&
                !EditorUtility.DisplayDialog(
                    "Replace modifier parameters?",
                    $"Changing to '{descriptor.DisplayName}' replaces {parameters.managedReferenceValue.GetType().Name} with {descriptor.ParametersType.Name}.",
                    "Replace",
                    "Cancel"))
            {
                return;
            }

            Undo.RecordObjects(property.serializedObject.targetObjects, "Change GAS modifier type");
            ModifierSelectionService.Assign(property, descriptor);
        }
    }

    [CustomPropertyDrawer(typeof(EquipmentDataModifier))]
    public sealed class EquipmentDataModifierDrawer : ModifierDataDrawer
    {
        protected override IReadOnlyList<ModifierDescriptor> Descriptors => ModifierTypeCatalog.Equipment;

        protected override bool TryGetDescriptor(uint id, out ModifierDescriptor descriptor)
        {
            return ModifierTypeCatalog.TryGetEquipment(id, out descriptor);
        }
    }

    [CustomPropertyDrawer(typeof(PerkDataModifier))]
    public sealed class PerkDataModifierDrawer : ModifierDataDrawer
    {
        protected override IReadOnlyList<ModifierDescriptor> Descriptors => ModifierTypeCatalog.Perks;

        protected override bool TryGetDescriptor(uint id, out ModifierDescriptor descriptor)
        {
            return ModifierTypeCatalog.TryGetPerk(id, out descriptor);
        }
    }
}
