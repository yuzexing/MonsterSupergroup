using AstralShift.HellMaiden.Combat.Hand.Data;
using AstralShift.HellMaiden.Data.Cards;
using MonsterSupergroup.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    [CustomEditor(typeof(PlayerBuildRuntime))]
    [CanEditMultipleObjects]
    public sealed class PlayerBuildRuntimeEditor : UnityEditor.Editor
    {
        private const string InitialWeaponIdField = "initialWeaponId";
        private static readonly GUIContent InitialWeaponLabel = new GUIContent("初始武器");
        private WeaponDB projectDatabase;

        private void OnEnable()
        {
            RefreshProjectDatabase();
            EditorApplication.projectChanged += RefreshProjectDatabase;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= RefreshProjectDatabase;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            DrawPropertiesExcluding(serializedObject, "m_Script", InitialWeaponIdField);

            SerializedProperty id = serializedObject.FindProperty(InitialWeaponIdField);
            WeaponDB database = ResolveCommonDatabase(out string message);
            string selectedName = id.hasMultipleDifferentValues ? "—" : GetSelectedName(database, id.uintValue);
            Rect position = EditorGUILayout.GetControlRect();
            EditorGUI.BeginProperty(position, InitialWeaponLabel, id);
            bool previousMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = id.hasMultipleDifferentValues;
            using (new EditorGUI.DisabledScope(database == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                Rect button = EditorGUI.PrefixLabel(position, InitialWeaponLabel);
                if (EditorGUI.DropdownButton(button, new GUIContent(selectedName), FocusType.Keyboard))
                    ShowWeaponMenu(database, id);
            }
            EditorGUI.showMixedValue = previousMixedValue;
            EditorGUI.EndProperty();

            if (message != null)
                EditorGUILayout.HelpBox(message, MessageType.Warning);
            else if (!id.hasMultipleDifferentValues && FindWeapon(database, id.uintValue) == null)
                EditorGUILayout.HelpBox("当前初始武器未在 WeaponDB 中注册，请从列表重新选择。", MessageType.Warning);

            serializedObject.ApplyModifiedProperties();
        }

        private void ShowWeaponMenu(WeaponDB database, SerializedProperty id)
        {
            var menu = new GenericMenu { allowDuplicateNames = true };
            if (database.Weapons != null)
            {
                foreach (WeaponData weapon in database.Weapons)
                {
                    if (weapon == null || weapon.ID == 0u) continue;
                    uint weaponId = weapon.ID;
                    bool selected = !id.hasMultipleDifferentValues && id.uintValue == weaponId;
                    menu.AddItem(new GUIContent(GetWeaponName(weapon)), selected, () => SelectWeapon(weaponId));
                }
            }

            if (menu.GetItemCount() == 0)
                menu.AddDisabledItem(new GUIContent("WeaponDB 中没有可选武器"));
            menu.ShowAsContext();
        }

        private void SelectWeapon(uint weaponId)
        {
            if (this == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            serializedObject.Update();
            serializedObject.FindProperty(InitialWeaponIdField).uintValue = weaponId;
            // SerializedProperty preserves Undo, multi-object edits and prefab overrides.
            serializedObject.ApplyModifiedProperties();
            Repaint();
        }

        private WeaponDB ResolveCommonDatabase(out string message)
        {
            message = null;
            WeaponDB database = null;
            foreach (UnityEngine.Object inspected in targets)
            {
                WeaponDB candidate = ResolveDatabase((PlayerBuildRuntime)inspected);
                if (candidate == null)
                {
                    message = "未能确定武器数据库。请在场景 RuntimeDB 中配置 WeaponDB；预制体单独编辑时，项目需有唯一的 WeaponDB。";
                    return null;
                }
                if (database != null && database != candidate)
                {
                    message = "所选对象使用不同的 WeaponDB，请分别配置初始武器。";
                    return null;
                }
                database = candidate;
            }
            return database;
        }

        private WeaponDB ResolveDatabase(PlayerBuildRuntime build)
        {
            if (build.BuildDatabase != null) return build.BuildDatabase.WeaponDB;

            NetworkPlayerBootstrap bootstrap = build.GetComponent<NetworkPlayerBootstrap>();
            if (bootstrap != null)
            {
                // Read the serialized override without invoking the runtime resolver,
                // which caches its result on the component just by inspecting it.
                using (var bootstrapObject = new SerializedObject(bootstrap))
                {
                    var assigned = bootstrapObject.FindProperty("runtimeDatabase").objectReferenceValue as RuntimeDB;
                    if (assigned != null) return assigned.WeaponDB;
                }
            }

            RuntimeDB shared = UnityEngine.Object.FindFirstObjectByType<RuntimeDB>();
            if (shared != null) return shared.WeaponDB;
            // Prefab assets can be configured without opening a gameplay scene.
            return projectDatabase;
        }

        private void RefreshProjectDatabase()
        {
            string[] guids = AssetDatabase.FindAssets("t:WeaponDB");
            projectDatabase = guids.Length == 1
                ? AssetDatabase.LoadAssetAtPath<WeaponDB>(AssetDatabase.GUIDToAssetPath(guids[0]))
                : null;
            Repaint();
        }

        private static string GetSelectedName(WeaponDB database, uint id)
        {
            WeaponData weapon = FindWeapon(database, id);
            return weapon != null ? GetWeaponName(weapon) : id == 0u ? "选择武器…" : $"未注册武器（{id}）";
        }

        private static WeaponData FindWeapon(WeaponDB database, uint id)
        {
            if (database != null && database.Weapons != null)
                foreach (WeaponData weapon in database.Weapons)
                    if (weapon != null && weapon.ID == id) return weapon;
            return null;
        }

        private static string GetWeaponName(WeaponData weapon)
        {
            return string.IsNullOrWhiteSpace(weapon.Title) ? weapon.name : weapon.Title;
        }
    }
}
