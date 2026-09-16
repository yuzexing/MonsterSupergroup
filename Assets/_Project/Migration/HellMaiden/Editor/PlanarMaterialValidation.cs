using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.HellMaidenMigration.Editor
{
    public static class PlanarMaterialValidation
    {
        public static bool IsOriginalOrFaithfulPlanarCopy(Material material, string originalFolder)
        {
            if (material == null || material.shader == null) return false;
            string path = AssetDatabase.GetAssetPath(material);
            if (path.StartsWith(originalFolder + "/", StringComparison.Ordinal))
                return material.shader.name.StartsWith("AllIn1", StringComparison.Ordinal);
            if (!path.StartsWith("Assets/_Project/Content/Rendering/Planar/Materials/", StringComparison.Ordinal)) return false;
            string sourcePath = AssetDatabase.GUIDToAssetPath(Path.GetFileNameWithoutExtension(path));
            if (!sourcePath.StartsWith(originalFolder + "/", StringComparison.Ordinal)) return false;
            var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            if (source == null || source.shader == null) return false;
            string expected = source.shader.name == "AllIn1SpriteShader/AllIn1SpriteShader" ? "MonsterSupergroup/PlanarSprite" :
                source.shader.name == "AllIn1Vfx/AllIn1VfxURPCompat" ? "MonsterSupergroup/PlanarVfx" : null;
            if (expected == null || material.shader.name != expected || material.renderQueue != source.renderQueue) return false;
            for (int i = 0; i < ShaderUtil.GetPropertyCount(source.shader); i++)
            {
                string name = ShaderUtil.GetPropertyName(source.shader, i);
                if (!material.HasProperty(name)) return false;
                switch (ShaderUtil.GetPropertyType(source.shader, i))
                {
                    case ShaderUtil.ShaderPropertyType.Color:
                        if (material.GetColor(name) != source.GetColor(name)) return false; break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        if (material.GetVector(name) != source.GetVector(name)) return false; break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        if (material.GetTexture(name) != source.GetTexture(name) || material.GetTextureScale(name) != source.GetTextureScale(name) || material.GetTextureOffset(name) != source.GetTextureOffset(name)) return false; break;
                    default:
                        if (material.GetFloat(name) != source.GetFloat(name)) return false; break;
                }
            }
            var expectedKeywords = source.shaderKeywords; var actualKeywords = material.shaderKeywords;
            Array.Sort(expectedKeywords); Array.Sort(actualKeywords);
            return string.Join(";", expectedKeywords) == string.Join(";", actualKeywords);
        }
    }
}
