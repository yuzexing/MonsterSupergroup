using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.HellMaidenMigration.Editor
{
    public static class DanteBeamPresentationPreview
    {
        [MenuItem("Tools/HellMaiden Migration/Capture Dante Beam Presentation Preview")]
        public static void Capture()
        {
            AnimationClip[] clips =
            {
                LoadClip(DanteBeamNativeGasMigration.StartClipPath),
                LoadClip(DanteBeamNativeGasMigration.MainClipPath),
                LoadClip(DanteBeamNativeGasMigration.EndClipPath)
            };
            string output = Environment.GetEnvironmentVariable("DANTE_BEAM_PREVIEW_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) output = "Logs/Phase02/Beam-Preview.png";
            string folder = Path.GetDirectoryName(output);
            string stem = Path.GetFileNameWithoutExtension(output);
            string extension = Path.GetExtension(output);
            if (string.IsNullOrEmpty(extension)) extension = ".png";
            // The recovered beam plays its native two-second main phase; its four-second source
            // Idle clip is not converted to a loop. Inspect both authored visual variants.
            float[] phaseDurations = { clips[0].length, 2f, clips[2].length };
            foreach (bool poison in new[] { false, true })
            {
                string variant = poison ? "Poison" : "Fire";
                string prefab = poison ? DanteBeamNativeGasMigration.PoisonAttackPath : DanteBeamNativeGasMigration.FireAttackPath;
                DanteAttackPresentationPreview.Capture(prefab, clips, phaseDurations,
                    clips[0].length + 0.8f, Path.Combine(folder ?? string.Empty, stem + "-" + variant + extension),
                    "Dante beam " + variant, direction: Vector2.right);
            }
        }

        private static AnimationClip LoadClip(string path) => AssetDatabase.LoadAssetAtPath<AnimationClip>(path)
            ?? throw new InvalidDataException("Import the Dante beam source clips first: " + path);
    }
}
