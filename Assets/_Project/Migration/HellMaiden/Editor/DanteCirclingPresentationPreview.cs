using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.HellMaidenMigration.Editor
{
    public static class DanteCirclingPresentationPreview
    {
        [MenuItem("Tools/HellMaiden Migration/Capture Dante Circling Presentation Preview")]
        public static void Capture()
        {
            AnimationClip[] clips =
            {
                LoadClip(DanteCirclingNativeGasMigration.StartClipPath),
                LoadClip(DanteCirclingNativeGasMigration.MainClipPath),
                LoadClip(DanteCirclingNativeGasMigration.EndClipPath)
            };
            string output = Environment.GetEnvironmentVariable("DANTE_CIRCLING_PREVIEW_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) output = "Logs/Phase02/Circling-Preview.png";
            // Orbit Duration includes Show. The zero-length looping Idle is a static animated pose,
            // with particles simulated throughout the externally held phase. No directional rotation.
            float[] phases = { clips[0].length, 3.14f - clips[0].length, clips[2].length };
            DanteAttackPresentationPreview.Capture(DanteCirclingNativeGasMigration.AttackPath,
                clips, phases, 0.8f, output, "Dante circling", rootRotation: Quaternion.Euler(45f, 0f, 0f));
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve Unity project root.");
            string metrics = Path.ChangeExtension(Path.IsPathRooted(output) ? output : Path.Combine(projectRoot, output), ".txt");
            File.AppendAllText(metrics, "Single sphere, inheriting the authored emitter's +45 degree orbit plane. " +
                "Orbit translation is exercised by the native runtime tests, not by this static visual capture.\n" +
                DanteCirclingNativeGasMigration.RestorationLimits + "\n");
        }

        private static AnimationClip LoadClip(string path) => AssetDatabase.LoadAssetAtPath<AnimationClip>(path)
            ?? throw new InvalidDataException("Import the Dante circling source clips first: " + path);
    }
}
