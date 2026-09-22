using System.Collections.Generic;
using System.IO;
using System.Linq;
using MonsterSupergroup.NetworkCombat.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class LegacyBuildIsolationTests
    {
        [Test]
        public void LimboBuildValidationPreservesExistingTimelineRulesAndPrefabs()
        {
            string[] paths = AssetDatabase.FindAssets("", new[] { LimboReferenceAssets.Root })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".asset") || p.EndsWith(".prefab") || p.EndsWith(".playable"))
                .OrderBy(p => p).ToArray();
            Assert.That(paths, Is.Not.Empty);
            var bytes = paths.ToDictionary(p => p, File.ReadAllBytes);
            var loaded = new Dictionary<Object, string>();
            foreach (string path in paths)
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset != null && !loaded.ContainsKey(asset))
                        loaded.Add(asset, EditorJsonUtility.ToJson(asset));

            LimboReferenceAssets.ValidateBuildInputs();

            foreach (string path in paths) Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes[path]), path);
            foreach (var pair in loaded) Assert.That(EditorJsonUtility.ToJson(pair.Key), Is.EqualTo(pair.Value), AssetDatabase.GetAssetPath(pair.Key));
        }
    }
}
