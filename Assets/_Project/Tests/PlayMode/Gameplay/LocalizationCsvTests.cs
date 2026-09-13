#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.Localization.Plugins.CSV;
using UnityEngine.Localization.Tables;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class LocalizationCsvTests
    {
        [Test] public void CsvMergePreservesIdsTemplatesTranslationsAndExistingSmartMetadata()
        {
            string folder = "Assets/LocalizationCsvTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            try
            {
                foreach (string name in new[] { "MonsterMenus", "MonsterContent" })
                {
                    var source = LocalizationEditorSettings.GetStringTableCollection(name);
                    var clone = LocalizationEditorSettings.CreateStringTableCollection("CsvTest_" + name, folder);
                    using var csv = new StringWriter(); Csv.Export(csv, source);
                    Csv.ImportInto(new StringReader(csv.ToString()), clone, removeMissingEntries: false);
                    foreach (var original in source.StringTables)
                    {
                        var table = (StringTable)clone.GetTable(original.LocaleIdentifier);
                        foreach (var entry in original.Values)
                        {
                            var copied = table.GetEntry(entry.KeyId);
                            Assert.That(copied, Is.Not.Null, entry.Key);
                            Assert.That(copied.LocalizedValue, Is.EqualTo(entry.LocalizedValue));
                            // IsSmart is table metadata, not a CSV column. Merge must retain it on existing entries.
                            copied.IsSmart = entry.IsSmart;
                        }
                    }
                    Csv.ImportInto(new StringReader(csv.ToString()), clone, removeMissingEntries: false);
                    foreach (var original in source.StringTables)
                        foreach (var entry in original.Values)
                        {
                            var copied = ((StringTable)clone.GetTable(original.LocaleIdentifier)).GetEntry(entry.KeyId);
                            Assert.That(copied.Key, Is.EqualTo(entry.Key));
                            Assert.That(copied.LocalizedValue, Is.EqualTo(entry.LocalizedValue));
                            Assert.That(copied.IsSmart, Is.EqualTo(entry.IsSmart));
                        }
                }
            }
            finally { AssetDatabase.DeleteAsset(folder); AssetDatabase.SaveAssets(); }
        }
    }
}
#endif
