using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static partial class ManualPlayFixAssets
    {
        // Follow the production database and Ultimate definition, including pooled hit/end sub-prefabs.
        public static string[] WeaponPrefabPaths() => AssetDatabase.GetDependencies(new[] {
                "Assets/_Project/Content/HellMaiden/NativeGAS/NativeGasWeaponDB.asset",
                "Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Ultimate/MonoBehaviour/UltimateData_Dante.asset" }, true)
            .Where(p => p.EndsWith(".prefab", StringComparison.Ordinal) && p.StartsWith("Assets/_Project/", StringComparison.Ordinal))
            .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();

        public static bool IsEffectRenderer(GameObject root, Renderer renderer)
        {
            if (root.GetComponent<SummonAIBehaviour>() == null) return true;
            string path = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
            // Source hierarchy verified for all three variants. Cocoon/body/feet shadow remain unchanged.
            const string pivot = "Iso Rotation/Self Rotation/";
            return path.StartsWith(pivot + "Power_Burst_V3", StringComparison.Ordinal) ||
                   path.StartsWith(pivot + "Buterfly_Attack Laser", StringComparison.Ordinal);
        }

        public static string PlanarShader(string source) => source switch {
            "AllIn1SpriteShader/AllIn1SpriteShader" => "MonsterSupergroup/PlanarSprite",
            "AllIn1Vfx/AllIn1VfxURPCompat" => "MonsterSupergroup/PlanarVfx",
            "HellMaiden/Presentation/Ovid Summon Shadow" => "MonsterSupergroup/PlanarSummonShadow",
            _ => null
        };

        public static string GameplayFingerprint(GameObject root)
        {
            var text = new StringBuilder();
            foreach (var component in root.GetComponentsInChildren<Component>(true))
                if (component != null && !(component is Renderer) && !(component is GameplayPlanarEffect) && !(component is Transform))
                    text.Append(component.GetType().FullName).Append(EditorJsonUtility.ToJson(component));
            // Include every transform explicitly; the component list changes when attaching the bounds handler.
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                text.Append(AnimationUtility.CalculateTransformPath(t, root.transform)).Append(t.localPosition.ToString("R"))
                    .Append(t.localRotation.ToString("R")).Append(t.localScale.ToString("R"));
            using var hash = SHA256.Create();
            return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
        }

        [Serializable] private class Coverage { public CoverageRow[] rows; }
        [Serializable] private class CoverageRow { public string prefab, renderer, status, material, source, shader, depthFeatures; public int slot; }
    }
}
