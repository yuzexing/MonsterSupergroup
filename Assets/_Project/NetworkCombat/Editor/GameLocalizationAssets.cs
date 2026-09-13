using System;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Data.Perks;
using MonsterSupergroup.Gameplay.Options;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Localization;
using UnityEditor.Localization.Plugins.CSV;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.TextCore.LowLevel;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class GameLocalizationAssets
    {
        public const string Root = "Assets/_Project/Localization";
        [Serializable] private sealed class Entry { public string table, key, zh, en; public bool smart; }
        [Serializable] private sealed class LevelOverride { public int index; public string key; }
        [Serializable] private sealed class Binding { public string path, kind, baseKey; public uint id; public LevelOverride[] overrides; }
        [Serializable] private sealed class Manifest { public Entry[] entries; public Binding[] assets; }

        // Explicit one-time migration. Normal builds only Validate; they never import or overwrite translations.
        public static void Import()
        {
            string path = Environment.GetEnvironmentVariable("MONSTER_LOCALIZATION_MANIFEST") ?? "Logs/LocalizationMigration/manifest.json";
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
            foreach (string name in new[] { GameLocalization.MenuTable, GameLocalization.ContentTable })
            {
                var collection = LocalizationEditorSettings.GetStringTableCollection(name) ?? LocalizationEditorSettings.CreateStringTableCollection(name, Root + "/Tables");
                foreach (string code in new[] { "zh-CN", "en" })
                {
                    var table = (StringTable)(collection.GetTable(code) ?? collection.AddNewTable(code));
                    foreach (var entry in manifest.entries.Where(e => e.table == name))
                    {
                        var value = table.AddEntry(entry.key, code == "en" ? entry.en : entry.zh);
                        value.IsSmart = entry.smart;
                    }
                    // Names now have one source in MonsterContent.
                    if (name == GameLocalization.MenuTable)
                        foreach (var old in table.Values.Where(e => e.Key.StartsWith("weapon.") || e.Key.StartsWith("equipment.")).ToArray()) table.RemoveEntry(old.KeyId);
                    EditorUtility.SetDirty(table);
                }
                collection.RefreshAddressables();
                collection.SetPreloadTableFlag(true);
                if (name == GameLocalization.MenuTable)
                    foreach (var entry in collection.SharedData.Entries.Where(e => e.Key.StartsWith("weapon.") || e.Key.StartsWith("equipment.")).ToArray())
                        collection.SharedData.RemoveKey(entry.Id);
                EditorUtility.SetDirty(collection.SharedData); EditorUtility.SetDirty(collection);
            }
            foreach (var item in manifest.assets)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(item.path);
                if (asset is CardData card)
                {
                    Bind(card.LocalizedTitle, item.baseKey + ".name"); Bind(card.LocalizedDescription, item.baseKey + ".description");
                    if (manifest.entries.Any(e => e.key == item.baseKey + ".quote")) Bind(card.LocalizedQuote, item.baseKey + ".quote");
                    if (card is EquipmentData equipment)
                        foreach (var level in item.overrides) Bind(equipment.Levels[level.index].LocalizedDescription, level.key);
                }
                else if (asset is PerkData perk) { Bind(perk.LocalizedTitle, item.baseKey + ".name"); Bind(perk.LocalizedDescription, item.baseKey + ".description"); }
                else if (asset is UltimateData ultimate) { Bind(ultimate.LocalizedTitle, item.baseKey + ".name"); Bind(ultimate.LocalizedDescription, item.baseKey + ".description"); }
                else throw new BuildFailedException("Unresolved migrated content: " + item.path);
                EditorUtility.SetDirty(asset);
            }
            var catalog = PreparationMenuCatalog.Load();
            Bind(catalog.LocalizedCharacterName, "character.1.name"); Bind(catalog.LocalizedMapName, "map.1.name"); Bind(catalog.LocalizedMapDescription, "map.1.description");
            EditorUtility.SetDirty(catalog);
            BuildFonts();
            AssetDatabase.SaveAssets();
            AssetDatabase.ForceReserializeAssets(manifest.assets.Select(a => a.path).Append(AssetDatabase.GetAssetPath(catalog)));
            Validate(); ExportCsv();
            Debug.Log("[Localization] Migrated " + manifest.assets.Length + " content assets and " + manifest.entries.Length + " translations.");
        }

        private static void Bind(LocalizedString text, string key) { text.TableReference = GameLocalization.ContentTable; text.TableEntryReference = key; }

        [MenuItem("MonsterSupergroup/Localization/Create entries for selected content")]
        public static void CreateSelectedEntries()
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(GameLocalization.ContentTable);
            foreach (var asset in Selection.objects)
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

        private static void BuildFonts()
        {
            var collection = LocalizationEditorSettings.GetAssetTableCollection(GameLocalization.FontTable) ?? LocalizationEditorSettings.CreateAssetTableCollection(GameLocalization.FontTable, Root + "/Tables");
            var zh = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/Chinese.otf");
            var en = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
            if (zh == null || en == null) throw new BuildFailedException("Bundled localization fonts are missing.");
            foreach (var pair in new[] { ("zh-CN", zh), ("en", en) })
            {
                var fontPath = Root + "/Fonts/" + pair.Item1 + " SDF.asset";
                var tmp = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
                if (tmp != null && (tmp.material == null || tmp.atlasTextures == null || tmp.atlasTextures.Any(t => t == null)))
                { AssetDatabase.DeleteAsset(fontPath); tmp = null; }
                if (tmp == null)
                {
                    tmp = TMP_FontAsset.CreateFontAsset(pair.Item2, 32, 5, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                    var textures = tmp.atlasTextures;
                    var material = tmp.material;
                    AssetDatabase.CreateAsset(tmp, fontPath);
                    foreach (var texture in textures) AssetDatabase.AddObjectToAsset(texture, tmp);
                    AssetDatabase.AddObjectToAsset(material, tmp);
                    tmp.atlasTextures = textures; tmp.material = material;
                    EditorUtility.SetDirty(tmp);
                    AssetDatabase.SaveAssets();
                }
                // Preserve language-neutral symbols as fallback glyphs.
                var latin = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
                tmp.fallbackFontAssetTable ??= new System.Collections.Generic.List<TMP_FontAsset>();
                if (latin != null && !tmp.fallbackFontAssetTable.Contains(latin)) tmp.fallbackFontAssetTable.Add(latin);
                // UGUI has no per-character asset fallback; this bundled font covers Latin and Chinese nicknames.
                collection.AddAssetToTable(pair.Item1, "ui.font", zh);
                collection.AddAssetToTable(pair.Item1, "ui.tmp", tmp);
                EditorUtility.SetDirty(tmp);
            }
            var chineseTmp = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "/Fonts/zh-CN SDF.asset");
            var englishTmp = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "/Fonts/en SDF.asset");
            if (!englishTmp.fallbackFontAssetTable.Contains(chineseTmp)) englishTmp.fallbackFontAssetTable.Add(chineseTmp);
            EditorUtility.SetDirty(englishTmp);
            collection.RefreshAddressables();
            // GameLocalization preloads typed font assets before publishing a locale change.
            // Do not start a second untyped font preload during SelectedLocale transitions.
            collection.SetPreloadTableFlag(false);
            var addressables = AddressableAssetSettingsDefaultObject.GetSettings(true);
            addressables.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
            EditorUtility.SetDirty(addressables); EditorUtility.SetDirty(collection.SharedData); EditorUtility.SetDirty(collection);
        }

        [MenuItem("MonsterSupergroup/Localization/Validate tables and content")]
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
        [MenuItem("MonsterSupergroup/Localization/Export translation CSV")]
        public static void ExportCsv()
        {
            Directory.CreateDirectory("docs/localization");
            foreach (string name in new[] { GameLocalization.MenuTable, GameLocalization.ContentTable })
                using (var writer = new StreamWriter("docs/localization/" + name + ".csv", false, new System.Text.UTF8Encoding(true)))
                    Csv.Export(writer, LocalizationEditorSettings.GetStringTableCollection(name));
        }
    }
}
