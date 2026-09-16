using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    // Damage popups use only digits/Latin formatting. Keep the exact glyph atlas,
    // without running DNP's exhaustive dynamic fallback setup on the first hit.
    public static class NumericDamageFontAssets
    {
        private const string Root = "Assets/_Project/Content/DamageNumbers";
        private const string FontPath = Root + "/DamageDigits.asset";
        private const string Source = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        public static void ApplyBatch()
        {
            int exit = 0;
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Source);
                var adapted = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
                if (adapted == null)
                {
                    adapted = UnityEngine.Object.Instantiate(source);
                    adapted.name = "DamageDigits";
                    adapted.atlasPopulationMode = AtlasPopulationMode.Static;
                    adapted.isMultiAtlasTexturesEnabled = false;
                    adapted.fallbackFontAssetTable = new List<TMP_FontAsset>();
                    // References to the source's atlas/material remain shared; no rasterization.
                    AssetDatabase.CreateAsset(adapted, FontPath);
                }
                foreach (char c in "0123456789.,+-KMB")
                    if (!adapted.HasCharacter(c, false, false)) throw new InvalidOperationException("Missing damage digit/format glyph: " + c);
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Root }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        bool changed = false;
                        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                        {
                            if (text.font != source) continue; // Keep explicit/manual font choices.
                            var material = text.fontSharedMaterial;
                            text.font = adapted;
                            text.fontSharedMaterial = material;
                            changed = true;
                        }
                        if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                AssetDatabase.SaveAssets();
            }
            catch (Exception e) { Debug.LogException(e); exit = 1; }
            finally { EditorApplication.Exit(exit); }
        }
    }
}
