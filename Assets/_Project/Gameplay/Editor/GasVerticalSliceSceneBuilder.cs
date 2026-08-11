using System;
using System.IO;
using System.Linq;
using MonsterSupergroup.GAS;
using MonsterSupergroup.GAS.Authoring;
using MonsterSupergroup.GAS.Unity;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.Gameplay.DebugUI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSupergroup.Gameplay.Editor
{
    public static class GasVerticalSliceSceneBuilder
    {
        public const string ScenePath =
            "Assets/_Project/Scenes/Development/GASVerticalSlice.unity";
        public const string EquipmentPath =
            "Assets/_Project/Content/Development/GASVerticalSliceEquipment.asset";
        public const string PerkPath =
            "Assets/_Project/Content/Development/GASVerticalSlicePerks.asset";

        private const string MenuPath =
            "Tools/MonsterSupergroup/Gameplay/Rebuild GAS Vertical Slice";

        [MenuItem(MenuPath)]
        private static void RebuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            RebuildAndValidate();
        }

        public static void RebuildAndValidate()
        {
            EnsureProjectFolders();
            EquipmentModifierSet equipment = BuildEquipmentAsset();
            PerkModifierSet perks = BuildPerkAsset();
            BuildScene(equipment, perks);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateGeneratedContent();
            Debug.Log($"GAS vertical slice rebuilt and validated: {ScenePath}");
        }

        public static void ValidateGeneratedContent()
        {
            EquipmentModifierSet equipment =
                AssetDatabase.LoadAssetAtPath<EquipmentModifierSet>(EquipmentPath);
            PerkModifierSet perks = AssetDatabase.LoadAssetAtPath<PerkModifierSet>(PerkPath);
            Require(equipment != null, $"Missing equipment asset at {EquipmentPath}.");
            Require(perks != null, $"Missing perk asset at {PerkPath}.");
            ValidateEquipment(equipment);
            ValidatePerks(perks);

            Require(File.Exists(ScenePath), $"Missing development scene at {ScenePath}.");
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject root = scene.GetRootGameObjects()
                .SingleOrDefault(gameObject => gameObject.name == "GAS Vertical Slice");
            Require(root != null, "Scene root 'GAS Vertical Slice' is missing.");

            Transform playerTransform = root.transform.Find("Player");
            Transform enemyTransform = root.transform.Find("Enemy");
            Require(playerTransform != null, "Player object is missing.");
            Require(enemyTransform != null, "Enemy object is missing.");

            CombatantBehaviour player = playerTransform.GetComponent<CombatantBehaviour>();
            CombatantBehaviour enemy = enemyTransform.GetComponent<CombatantBehaviour>();
            WeaponRuntimeBehaviour weapon = playerTransform.GetComponent<WeaponRuntimeBehaviour>();
            StatusUpdateDriver playerStatus = playerTransform.GetComponent<StatusUpdateDriver>();
            StatusUpdateDriver enemyStatus = enemyTransform.GetComponent<StatusUpdateDriver>();
            VerticalSliceCombatController controller =
                root.GetComponent<VerticalSliceCombatController>();
            CombatDebugPresenter presenter = root.GetComponent<CombatDebugPresenter>();

            Require(player != null, "Player CombatantBehaviour is missing.");
            Require(enemy != null, "Enemy CombatantBehaviour is missing.");
            Require(weapon != null, "Player WeaponRuntimeBehaviour is missing.");
            Require(playerStatus != null && playerStatus.Combatant == player,
                "Player StatusUpdateDriver is not wired.");
            Require(enemyStatus != null && enemyStatus.Combatant == enemy,
                "Enemy StatusUpdateDriver is not wired.");
            Require(controller != null && controller.Weapon == weapon && controller.Target == enemy,
                "VerticalSliceCombatController is not wired.");
            Require(presenter != null &&
                    presenter.Controller == controller &&
                    presenter.Player == player &&
                    presenter.Enemy == enemy &&
                    presenter.Weapon == weapon,
                "CombatDebugPresenter is not wired.");
            Require(playerTransform.GetComponent<SpriteRenderer>() != null,
                "Player development sprite is missing.");
            Require(enemyTransform.GetComponent<SpriteRenderer>() != null,
                "Enemy development sprite is missing.");
            Require(root.GetComponentInChildren<Camera>(true) != null,
                "Development camera is missing.");

            try
            {
                weapon.Initialize(new SeededRandomSource(2026));
                Require(weapon.ModifierCount == 2,
                    $"Expected 2 equipment modifiers, found {weapon.ModifierCount}.");
                Require(weapon.Stats.DamageValue == 15,
                    $"Expected resolved damage 15, found {weapon.Stats.DamageValue}.");
                Require(Math.Abs(weapon.Stats.SpeedValue - 1.25f) < 0.0001f,
                    $"Expected resolved speed 1.25, found {weapon.Stats.SpeedValue}.");
            }
            finally
            {
                weapon.Shutdown();
            }

            bool isInBuildSettings = EditorBuildSettings.scenes.Any(
                buildScene => string.Equals(
                    buildScene.path,
                    ScenePath,
                    StringComparison.OrdinalIgnoreCase));
            Require(!isInBuildSettings,
                "The development GAS vertical-slice scene must not be in product Build Settings.");
        }

        private static EquipmentModifierSet BuildEquipmentAsset()
        {
            EquipmentModifierSet asset =
                AssetDatabase.LoadAssetAtPath<EquipmentModifierSet>(EquipmentPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<EquipmentModifierSet>();
                AssetDatabase.CreateAsset(asset, EquipmentPath);
            }

            var serialized = new SerializedObject(asset);
            SerializedProperty modifiers = RequireProperty(serialized, "modifiers");
            modifiers.arraySize = 2;
            SetEquipmentEntry(
                modifiers.GetArrayElementAtIndex(0),
                DamageStatModifier.ModifierIdValue,
                new DamageStatModifierParameters(0.5f));
            SetEquipmentEntry(
                modifiers.GetArrayElementAtIndex(1),
                OnHitBurnModifier.ModifierIdValue,
                new OnHitBurnModifierParameters(1f, 0.5f, 2, 0.5f));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static PerkModifierSet BuildPerkAsset()
        {
            PerkModifierSet asset = AssetDatabase.LoadAssetAtPath<PerkModifierSet>(PerkPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<PerkModifierSet>();
                AssetDatabase.CreateAsset(asset, PerkPath);
            }

            var serialized = new SerializedObject(asset);
            SerializedProperty modifiers = RequireProperty(serialized, "modifiers");
            modifiers.arraySize = 1;
            SerializedProperty entry = modifiers.GetArrayElementAtIndex(0);
            RequireRelative(entry, "modifierId").uintValue =
                WeaponSpeedPerkModifier.ModifierIdValue;
            RequireRelative(entry, "parameters").managedReferenceValue =
                new WeaponSpeedPerkModifierParameters(0.25f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void BuildScene(
            EquipmentModifierSet equipment,
            PerkModifierSet perks)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("GAS Vertical Slice");

            GameObject playerObject = CreateCombatantVisual(
                "Player",
                new Vector3(-3f, 0f, 0f),
                new Color(0.2f, 0.55f, 1f),
                root.transform);
            CombatantBehaviour player = playerObject.AddComponent<CombatantBehaviour>();
            StatusUpdateDriver playerStatus = playerObject.AddComponent<StatusUpdateDriver>();
            playerStatus.Configure(player);
            WeaponRuntimeBehaviour weapon = playerObject.AddComponent<WeaponRuntimeBehaviour>();
            ConfigureSerializedWeapon(weapon, equipment, perks);

            GameObject enemyObject = CreateCombatantVisual(
                "Enemy",
                new Vector3(3f, 0f, 0f),
                new Color(1f, 0.25f, 0.25f),
                root.transform);
            CombatantBehaviour enemy = enemyObject.AddComponent<CombatantBehaviour>();
            StatusUpdateDriver enemyStatus = enemyObject.AddComponent<StatusUpdateDriver>();
            enemyStatus.Configure(enemy);

            VerticalSliceCombatController controller =
                root.AddComponent<VerticalSliceCombatController>();
            controller.Configure(weapon, enemy);
            controller.AutoAttack = true;

            CombatDebugPresenter presenter = root.AddComponent<CombatDebugPresenter>();
            presenter.Configure(controller, player, enemy, weapon);

            var cameraObject = new GameObject("Development Camera");
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.065f, 0.09f);

            Require(EditorSceneManager.SaveScene(scene, ScenePath),
                $"Failed to save scene at {ScenePath}.");
        }

        private static GameObject CreateCombatantVisual(
            string name,
            Vector3 position,
            Color color,
            Transform parent)
        {
            Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Require(sprite != null, "Unity built-in development sprite could not be loaded.");

            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = position;
            gameObject.transform.localScale = new Vector3(1.8f, 1.8f, 1f);
            SpriteRenderer renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            return gameObject;
        }

        private static void ConfigureSerializedWeapon(
            WeaponRuntimeBehaviour weapon,
            EquipmentModifierSet equipment,
            PerkModifierSet perks)
        {
            var serialized = new SerializedObject(weapon);
            RequireProperty(serialized, "combatId").uintValue = 1u;
            RequireProperty(serialized, "equipmentModifierSet").objectReferenceValue = equipment;
            RequireProperty(serialized, "perkModifierSet").objectReferenceValue = perks;

            SerializedProperty stats = RequireProperty(serialized, "baseStats");
            RequireRelative(stats, "damage").intValue = 10;
            RequireRelative(stats, "critMultiplier").floatValue = 1.5f;
            RequireRelative(stats, "critRate").floatValue = 0f;
            RequireRelative(stats, "speed").floatValue = 1f;
            RequireRelative(stats, "size").floatValue = 1f;
            RequireRelative(stats, "duration").floatValue = 1f;
            RequireRelative(stats, "projectileCount").intValue = 1;
            RequireRelative(stats, "knockbackDistance").floatValue = 1f;
            RequireRelative(stats, "damageType").enumValueIndex = (int)DamageType.Normal;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ValidateEquipment(EquipmentModifierSet equipment)
        {
            Require(equipment.Modifiers.Count == 2,
                $"Expected 2 equipment entries, found {equipment.Modifiers.Count}.");
            Require(equipment.Modifiers[0].ModifierIdValue == DamageStatModifier.ModifierIdValue,
                "The first equipment entry must be DamageStatModifier.");
            Require(equipment.Modifiers[0].Parameters is DamageStatModifierParameters damage &&
                    Math.Abs(damage.MultiplierIncrement - 0.5f) < 0.0001f,
                "DamageStatModifier parameters are invalid.");
            Require(equipment.Modifiers[1].ModifierIdValue == OnHitBurnModifier.ModifierIdValue,
                "The second equipment entry must be OnHitBurnModifier.");
            Require(equipment.Modifiers[1].Parameters is OnHitBurnModifierParameters burn &&
                    Math.Abs(burn.Chance - 1f) < 0.0001f &&
                    Math.Abs(burn.DamageMultiplier - 0.5f) < 0.0001f &&
                    burn.NumberOfHits == 2 &&
                    Math.Abs(burn.HitIntervalDuration - 0.5f) < 0.0001f,
                "OnHitBurnModifier parameters are invalid.");
        }

        private static void ValidatePerks(PerkModifierSet perks)
        {
            Require(perks.Modifiers.Count == 1,
                $"Expected 1 perk entry, found {perks.Modifiers.Count}.");
            Require(perks.Modifiers[0].ModifierIdValue == WeaponSpeedPerkModifier.ModifierIdValue,
                "The perk entry must be WeaponSpeedPerkModifier.");
            Require(perks.Modifiers[0].Parameters is WeaponSpeedPerkModifierParameters speed &&
                    Math.Abs(speed.MultiplierIncrement - 0.25f) < 0.0001f,
                "WeaponSpeedPerkModifier parameters are invalid.");
        }

        private static void SetEquipmentEntry(
            SerializedProperty entry,
            uint id,
            EquipmentModifierParameters parameters)
        {
            RequireRelative(entry, "modifierId").uintValue = id;
            RequireRelative(entry, "parameters").managedReferenceValue = parameters;
        }

        private static SerializedProperty RequireProperty(
            SerializedObject serialized,
            string propertyName)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"{serialized.targetObject.GetType().FullName} has no serialized field '{propertyName}'.");
            }

            return property;
        }

        private static SerializedProperty RequireRelative(
            SerializedProperty property,
            string relativeName)
        {
            SerializedProperty relative = property.FindPropertyRelative(relativeName);
            if (relative == null)
            {
                throw new InvalidOperationException(
                    $"Serialized property '{property.propertyPath}' has no child '{relativeName}'.");
            }

            return relative;
        }

        private static void EnsureProjectFolders()
        {
            EnsureFolder("Assets/_Project/Content");
            EnsureFolder("Assets/_Project/Content/Development");
            EnsureFolder("Assets/_Project/Scenes");
            EnsureFolder("Assets/_Project/Scenes/Development");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || !AssetDatabase.IsValidFolder(parent))
            {
                throw new InvalidOperationException($"Cannot create folder '{path}': parent is missing.");
            }

            AssetDatabase.CreateFolder(parent, name);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
