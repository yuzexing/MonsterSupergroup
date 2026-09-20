using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mirror;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class GameplayPickupSetup
    {
        public const string Root = "Assets/_Project/Content/NetworkCombat/";
        public const string RulesPath = Root + "GameplayPickupRules.asset";
        public const string HealthPath = Root + "NetworkHealthPickup.prefab";
        public static void Apply()
        {
            string visualPath = GameplayExperienceSetup.ImportHealthVisual();
            string sourcePath = MonsterSupergroup.EditorTools.ProjectToolPaths.HellMaiden() + "/Assets/GameObject/WorldItem_Health.prefab";
            string source = File.ReadAllText(sourcePath);
            if (!File.Exists(HealthPath))
            {
                var root = new GameObject("NetworkHealthPickup", typeof(NetworkIdentity), typeof(NetworkExperienceGem));
                try
                {
                    var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(visualPath));
                    visual.transform.SetParent(root.transform, false); visual.transform.localPosition = Vector3.zero;
                    var so = new SerializedObject(root.GetComponent<NetworkExperienceGem>());
                    so.FindProperty("visual").objectReferenceValue = visual.transform;
                    AssignSound(so.FindProperty("pullSound"), source, "soundEventPull");
                    AssignSound(so.FindProperty("consumeSound"), source, "soundEventConsume");
                    so.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, HealthPath);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            AdaptHealthPresentation();
            var xp = Definition("ExperiencePickup", 1, PickupEffect.Experience, GameplayExperienceSetup.GemPath, 0, 0, 500);
            var health = Definition("HealthPickup", 2, PickupEffect.RestoreHealth, HealthPath, 200, 4, 100);
            var rules = AssetDatabase.LoadAssetAtPath<GameplayPickupRules>(RulesPath);
            if (rules == null)
            {
                rules = ScriptableObject.CreateInstance<GameplayPickupRules>();
                rules.experience = AssetDatabase.LoadAssetAtPath<GameplayExperienceRules>(GameplayExperienceSetup.RulesPath);
                rules.experienceItem = xp; rules.healthItem = health;
                AssetDatabase.CreateAsset(rules, RulesPath);
            }
            var world = PrefabUtility.LoadPrefabContents(Root + "NetworkCombatWorld.prefab");
            try
            {
                var so = new SerializedObject(world.GetComponent<NetworkExperienceWorld>());
                so.FindProperty("pickupRules").objectReferenceValue = rules; so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(world, Root + "NetworkCombatWorld.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(world); }
            string observation = Root + "Limbo/Resources/LimboReference/PickupObservation.asset";
            if (!File.Exists(observation))
                AssetDatabase.CopyAsset(Root + "Limbo/Resources/LimboReference/AudioObservation.asset", observation);
            AssetDatabase.SaveAssets();
            Debug.Log("[PickupSetup] XP and Health definitions connected; existing adapted assets preserved.");
        }
        private static void AdaptHealthPresentation()
        {
            const string folder = "Assets/_Project/Content/Rendering/Planar/Materials/";
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var root = PrefabUtility.LoadPrefabContents(HealthPath);
            try
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        var source = materials[i];
                        if (source == null || source.HasProperty("_GameplayPlanar")) continue;
                        string path = folder + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)) + ".mat";
                        var adapted = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (adapted == null)
                        {
                            adapted = new Material(source) { name = source.name + " (Pickup XY)", shader = Shader.Find("MonsterSupergroup/PlanarSprite") };
                            if (source.shader.name == "Universal Render Pipeline/2D/Sprite-Lit-Default")
                            {
                                // Exported URP shader is a stub. Preserve its sprite/texture and alpha blend via the installed shader.
                                adapted.shaderKeywords = Array.Empty<string>();
                                adapted.SetFloat("_Alpha", 1); adapted.SetFloat("_Contrast", 1);
                                adapted.SetFloat("_MySrcMode", 5); adapted.SetFloat("_MyDstMode", 10); adapted.SetFloat("_ZWrite", 0);
                            }
                            AssetDatabase.CreateAsset(adapted, path);
                        }
                        materials[i] = adapted;
                    }
                    renderer.sharedMaterials = materials;
                }
                MonsterSupergroup.Gameplay.Combat.GameplayPlanarEffect.Attach(root);
                PrefabUtility.SaveAsPrefabAsset(root, HealthPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        private static GameplayPickupDefinition Definition(string name, uint id, PickupEffect effect, string prefab,
            float value, int limit, int capacity)
        {
            string path = Root + name + ".asset";
            var data = AssetDatabase.LoadAssetAtPath<GameplayPickupDefinition>(path);
            if (data != null) return data;
            data = ScriptableObject.CreateInstance<GameplayPickupDefinition>(); data.id = id; data.effect = effect;
            data.value = value; data.worldLimit = limit; data.idleCapacity = capacity;
            data.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefab).GetComponent<NetworkExperienceGem>();
            AssetDatabase.CreateAsset(data, path); return data;
        }
        private static void AssignSound(SerializedProperty property, string yaml, string name)
        {
            string block = Regex.Match(yaml, @"(?ms)^  " + name + @":\r?\n(.*?)(?=^  [A-Za-z_])").Groups[1].Value;
            var guid = property.FindPropertyRelative("Guid");
            for (int i = 1; i <= 4; i++)
                guid.FindPropertyRelative("Data" + i).intValue = int.Parse(Regex.Match(block, "Data" + i + @": (-?\d+)").Groups[1].Value);
        }
    }
}
