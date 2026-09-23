using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace MonsterSupergroup.EditorTools
{
    /// <summary>TMP's package callback changes persistent fonts, including atlas sub-assets.</summary>
    public sealed class ProjectBuildTransaction : IDisposable
    {
        private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> fontStates = new(StringComparer.Ordinal);
        private bool restored;
        public ProjectBuildTransaction()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font == null || font.atlasPopulationMode == AtlasPopulationMode.Static ||
                    !NativeBuildProfileSettings.Required(new SerializedObject(font), "m_ClearDynamicDataOnBuild").boolValue) continue;
                // Never overwrite unsaved user state with a disk backup.
                if (AssetDatabase.LoadAllAssetsAtPath(path).Any(EditorUtility.IsDirty))
                    throw new BuildFailedException("动态字体有未保存状态，请先保存后构建：" + path);
                fontStates[path] = EditorJsonUtility.ToJson(font);
                Capture(path);
                foreach (var atlas in font.atlasTextures ?? Array.Empty<Texture2D>())
                {
                    string atlasPath = AssetDatabase.GetAssetPath(atlas);
                    if (!string.IsNullOrEmpty(atlasPath)) Capture(atlasPath);
                }
            }
        }
        private void Capture(string path)
        {
            foreach (string file in new[] { path, path + ".meta" })
                if (File.Exists(file) && !files.ContainsKey(file)) files.Add(file, File.ReadAllBytes(file));
        }
        public void Dispose()
        {
            if (restored) return;
            var errors = new List<Exception>();
            // Flush only the controlled fonts, then restore their original bytes and reimport.
            foreach (var pair in fontStates)
                try { var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(pair.Key); if (font != null) AssetDatabase.SaveAssetIfDirty(font); }
                catch (Exception e) { errors.Add(e); }
            foreach (var pair in files)
                try { if (!File.Exists(pair.Key) || !File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)) File.WriteAllBytes(pair.Key, pair.Value); }
                catch (Exception e) { errors.Add(e); }
            foreach (string path in files.Keys.Where(p => !p.EndsWith(".meta", StringComparison.Ordinal)))
                try { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate); }
                catch (Exception e) { errors.Add(e); }
            foreach (var pair in fontStates)
                try
                {
                    if (EditorJsonUtility.ToJson(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(pair.Key)) != pair.Value)
                        throw new BuildFailedException("TMP 内存状态恢复不一致：" + pair.Key);
                }
                catch (Exception e) { errors.Add(e); }
            restored = errors.Count == 0;
            if (errors.Count != 0) throw new AggregateException("TMP 恢复失败；本次产物不可交付。", errors);
        }
    }
}
