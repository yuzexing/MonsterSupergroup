using System;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.Data.Cards;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class PreparationMenuAssets
    {

        public static void CreateCatalog()
        {
            MonsterSupergroup.EditorTools.ProjectToolRunner.CheckLegacyMaintenance("create.preparation-catalog", "MonsterSupergroup.NetworkCombat.Editor.PreparationMenuAssets.CreateCatalog");
            const string path = "Assets/Resources/PreparationMenuCatalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<PreparationMenuCatalog>(path);
            if (catalog != null) return;
            var database = AssetDatabase.LoadAssetAtPath<WeaponDB>("Assets/_Project/Content/HellMaiden/NativeGAS/NativeGasWeaponDB.asset");
            if (database == null) throw new InvalidOperationException("Native weapon database is missing.");
            catalog = ScriptableObject.CreateInstance<PreparationMenuCatalog>();
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Content/NetworkCombat/NetworkPlayer.prefab");
            catalog.CharacterPortrait = player.GetComponentsInChildren<SpriteRenderer>(true).First(r => r.sprite != null).sprite;
            catalog.UseCharacterMonogram = true;
            uint[] ids = { 1, 2, 3, 6, 8, 402 };
            string[] marks = { "斩", "焰", "息", "环", "径", "召" };
            catalog.Weapons = ids.Select((id, index) =>
            {
                var data = database.Weapons.Single(w => w.ID == id);
                var icon = FindIcon(data);
                return new PreparationMenuCatalog.WeaponOption { Weapon = data, Icon = icon, IconMark = marks[index] };
            }).ToArray();
            catalog.LocalizedCharacterName.TableReference = catalog.LocalizedMapName.TableReference =
                catalog.LocalizedMapDescription.TableReference = MonsterSupergroup.Gameplay.Options.GameLocalization.ContentTable;
            catalog.LocalizedCharacterName.TableEntryReference = "character.1.name";
            catalog.LocalizedMapName.TableEntryReference = "map.1.name";
            catalog.LocalizedMapDescription.TableEntryReference = "map.1.description";
            Directory.CreateDirectory("Assets/Resources");
            AssetDatabase.CreateAsset(catalog, path); AssetDatabase.SaveAssets();
            Debug.Log("[Preparation] Created catalog with six existing weapons and presentation sprites.");
        }
        private static Sprite FindIcon(WeaponData weapon)
        {
            string visualPath = AssetDatabase.GUIDToAssetPath(new SerializedObject(weapon)
                .FindProperty("visualDataReference").FindPropertyRelative("m_AssetGUID").stringValue);
            var visual = AssetDatabase.LoadAssetAtPath<CardVisualData>(visualPath);
            if (visual?.Illustration?.Sprite != null) return visual.Illustration.Sprite;
            // Attack textures may be atlases, masks or shader inputs, not UI illustrations.
            // Missing card art is represented by a configurable typographic emblem.
            return null;
        }
    }
}
