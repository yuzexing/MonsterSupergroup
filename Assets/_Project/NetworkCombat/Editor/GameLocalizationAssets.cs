using System;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Data.Perks;
using MonsterSupergroup.Gameplay.Options;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Localization;
using UnityEditor.Localization.Plugins.CSV;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class GameLocalizationAssets
    {
        public const string Root = "Assets/_Project/Localization";
        private static void Bind(LocalizedString text, string key) { text.TableReference = GameLocalization.ContentTable; text.TableEntryReference = key; }


        public static void CreateSelectedEntries()
        {
            Debug.LogWarning("[ProjectTools] Use create.localization-entries -AssetPath ... -Apply.");
            CreateEntries(AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        public static void CreateEntries(string path)
        {
            MonsterSupergroup.EditorTools.ProjectToolRunner.CheckLegacyMaintenance("create.localization-entries", "MonsterSupergroup.NetworkCombat.Editor.GameLocalizationAssets.CreateEntries");
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null || !(asset is CardData || asset is PerkData || asset is UltimateData))
                throw new ArgumentException("Please specify a content asset: " + path);
            CreateEntriesForAssets(new[] { asset });
        }

        private static void CreateEntriesForAssets(UnityEngine.Object[] assets)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(GameLocalization.ContentTable);
            foreach (var asset in assets)
            {
                LocalizedString title, description; LocalizedContentKind kind; uint id;
                if (asset is CardData card) { kind = card is WeaponData ? LocalizedContentKind.Weapon : LocalizedContentKind.Equipment; id = card.ID; title = card.LocalizedTitle; description = card.LocalizedDescription; }
                else if (asset is PerkData perk) { kind = LocalizedContentKind.Perk; id = perk.ID; title = perk.LocalizedTitle; description = perk.LocalizedDescription; }
                else if (asset is UltimateData ultimate) { kind = LocalizedContentKind.Ultimate; id = ultimate.Id; title = ultimate.LocalizedTitle; description = ultimate.LocalizedDescription; }
                else continue;
                string nameKey = ContentText.Key(kind, id, "name"), descriptionKey = ContentText.Key(kind, id, "description");
                Bind(title, nameKey); Bind(description, descriptionKey);
                foreach (StringTable table in collection.StringTables)
                {
                    if (table.GetEntry(nameKey) == null) table.AddEntry(nameKey, "");
                    if (table.GetEntry(descriptionKey) == null) table.AddEntry(descriptionKey, "").IsSmart = kind == LocalizedContentKind.Equipment || kind == LocalizedContentKind.Perk;
                    EditorUtility.SetDirty(table);
                }
                EditorUtility.SetDirty(asset);
            }
            EditorUtility.SetDirty(collection.SharedData); AssetDatabase.SaveAssets();
        }

        public static void Validate()
        {
            var locales = LocalizationEditorSettings.GetLocales();
            foreach (string required in new[] { "zh-CN", "en" })
                if (!locales.Any(l => l.Identifier.Code == required)) throw new BuildFailedException("Missing required locale " + required);
            foreach (string name in new[] { GameLocalization.MenuTable, GameLocalization.ContentTable })
            {
                var collection = LocalizationEditorSettings.GetStringTableCollection(name);
                if (collection == null) throw new BuildFailedException("Missing table " + name);
                var keys = collection.StringTables.SelectMany(t => t.Values.Select(e => e.Key)).Distinct().ToArray();
                foreach (var locale in locales)
                {
                    string code = locale.Identifier.Code;
                    var table = collection.GetTable(code) as StringTable;
                    if (table == null) throw new BuildFailedException("Missing locale table " + name + "/" + code);
                    foreach (string key in keys)
                        if (string.IsNullOrWhiteSpace(table.GetEntry(key)?.LocalizedValue)) throw new BuildFailedException("Empty translation: " + name + "/" + code + "/" + key);
                }
            }
            foreach (string filter in new[] { "t:WeaponData", "t:EquipmentData", "t:PerkData", "t:UltimateData" })
                foreach (string guid in AssetDatabase.FindAssets(filter))
                {
                    var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                    if (asset is CardData card) { ValidateReference(card.LocalizedTitle); ValidateReference(card.LocalizedDescription); if (!card.LocalizedQuote.IsEmpty) ValidateReference(card.LocalizedQuote); }
                    else if (asset is PerkData perk) { ValidateReference(perk.LocalizedTitle); ValidateReference(perk.LocalizedDescription); }
                    else if (asset is UltimateData ultimate) { ValidateReference(ultimate.LocalizedTitle); ValidateReference(ultimate.LocalizedDescription); }
                    if (asset is EquipmentData equipment)
                        foreach (var level in equipment.Levels)
                            ValidateFormat(level.LocalizedDescription.IsEmpty ? equipment.LocalizedDescription : level.LocalizedDescription, ContentText.Arguments(level.Modifiers));
                    if (asset is PerkData p)
                        foreach (var rarity in p.GetAllRarities()) ValidateFormat(p.LocalizedDescription, ContentText.Arguments(rarity.Modifiers));
                }
            var fonts = LocalizationEditorSettings.GetAssetTableCollection(GameLocalization.FontTable);
            foreach (var locale in locales)
            {
                var table = fonts?.GetTable(locale.Identifier) as AssetTable;
                foreach (string key in new[] { "ui.font", "ui.tmp" })
                {
                    var entry = table?.GetEntry(key);
                    if (entry == null || AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(entry.Guid)) == null)
                        throw new BuildFailedException("Missing font " + locale.Identifier + "/" + key);
                }
                var tmp = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(table.GetEntry("ui.tmp").Guid));
                if (tmp == null || tmp.material == null || tmp.atlasTextures == null || tmp.atlasTextures.Any(t => t == null))
                    throw new BuildFailedException("Incomplete TMP font " + locale.Identifier);
            }
            Debug.Log("[Localization] Tables and content references validated.");
        }
        private static void ValidateReference(LocalizedString reference)
        {
            if (reference == null || reference.IsEmpty) throw new BuildFailedException("Content has an unbound localized string.");
            var collection = LocalizationEditorSettings.GetStringTableCollection(reference.TableReference);
            foreach (var locale in LocalizationEditorSettings.GetLocales())
            {
                string code = locale.Identifier.Code;
                var table = collection?.GetTable(code) as StringTable;
                var key = reference.TableEntryReference;
                var entry = key.ReferenceType == TableEntryReference.Type.Id ? table?.GetEntry(key.KeyId) : table?.GetEntry(key.Key);
                if (entry == null || string.IsNullOrWhiteSpace(entry.LocalizedValue)) throw new BuildFailedException("Missing content translation: " + code + "/" + key);
            }
        }
        private static void ValidateFormat(LocalizedString reference, System.Collections.Generic.Dictionary<string, object> arguments)
        {
            ValidateReference(reference);
            var collection = LocalizationEditorSettings.GetStringTableCollection(reference.TableReference);
            foreach (var locale in LocalizationEditorSettings.GetLocales())
            {
                var table = (StringTable)collection.GetTable(locale.Identifier);
                var key = reference.TableEntryReference;
                var entry = key.ReferenceType == TableEntryReference.Type.Id ? table.GetEntry(key.KeyId) : table.GetEntry(key.Key);
                if (!entry.IsSmart) throw new BuildFailedException("Content description must be Smart: " + key);
                string text;
                try { text = entry.GetLocalizedString(locale.Identifier.CultureInfo, new object[] { arguments }); }
                catch (Exception error) { throw new BuildFailedException("Invalid content parameters " + key + ": " + error.Message); }
                if (text.Contains('{') || text.Contains('}')) throw new BuildFailedException("Unresolved content parameters: " + key);
            }
        }

        public static void ExportCsv()
        {
            Directory.CreateDirectory("docs/localization");
            foreach (string name in new[] { GameLocalization.MenuTable, GameLocalization.ContentTable })
                using (var writer = new StreamWriter("docs/localization/" + name + ".csv", false, new System.Text.UTF8Encoding(true)))
                    Csv.Export(writer, LocalizationEditorSettings.GetStringTableCollection(name));
        }
    }
}
