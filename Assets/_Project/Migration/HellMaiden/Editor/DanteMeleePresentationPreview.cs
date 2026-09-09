using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.HellMaidenMigration.Editor
{
    /// <summary>Renders the imported clip and particles without starting gameplay or networking.</summary>
    public static class DanteMeleePresentationPreview
    {
        [MenuItem("Tools/HellMaiden Migration/Capture Dante Melee Presentation Preview")]
        public static void Capture()
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(DanteMeleeNativeGasMigration.ClipPath)
                ?? throw new InvalidDataException("The original Dante slash clip is missing.");
            string output = Environment.GetEnvironmentVariable("DANTE_MELEE_PREVIEW_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) output = "Logs/Phase02/Melee-Preview.png";
            DanteAttackPresentationPreview.Capture(DanteMeleeNativeGasMigration.AttackPath,
                new[] { clip }, new[] { clip.length }, 0.12f, output, "Dante melee", simulateInactiveParticles: true);
        }
    }
}