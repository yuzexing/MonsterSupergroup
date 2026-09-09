using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.HellMaidenMigration.Editor
{
    public static class OvidSummonPresentationPreview
    {
        [MenuItem("Tools/HellMaiden Migration/Capture Ovid Summon Shadow Evidence")]
        public static void CaptureShadowEvidence()
        {
            string output = Environment.GetEnvironmentVariable("OVID_SUMMON_SHADOW_PREVIEW_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) output = "Logs/Phase02/Summon-Shadow-Evidence.png";
            string folder = Path.GetDirectoryName(output) ?? "";
            string stem = Path.GetFileNameWithoutExtension(output);
            AnimationClip[] clips = { Clip(OvidSummonNativeGasMigration.EnterClipPath),
                Clip(OvidSummonNativeGasMigration.MainClipPath), Clip(OvidSummonNativeGasMigration.ExitClipPath) };
            float[] phases = { clips[0].length, 1f, clips[2].length };
            var gray = new Color(0.5f, 0.5f, 0.5f, 1f);
            DanteAttackPresentationPreview.Capture(OvidSummonNativeGasMigration.DefaultAttackPath, clips, phases, 1.5f,
                Path.Combine(folder, stem + "-Main-Gray.png"), "Ovid Summon Main on gray",
                animationRootPath: OvidSummonNativeGasMigration.VisualRootPath, backgroundColor: gray);
            string isolatedOutput = Path.Combine(folder, stem + "-Isolated-Gray.png");
            DanteAttackPresentationPreview.Capture(OvidSummonNativeGasMigration.DefaultAttackPath, clips, phases, 1.5f,
                isolatedOutput, "Ovid Summon original Shadow on gray",
                animationRootPath: OvidSummonNativeGasMigration.VisualRootPath, backgroundColor: gray,
                isolatedRendererPath: OvidSummonNativeGasMigration.ShadowPath, minimumColoredPixels: 0,
                inspectImage: (image, camera, renderers) => InspectShadow(image, camera,
                    renderers.OfType<SpriteRenderer>().Single(renderer => renderer.enabled), isolatedOutput));
        }

        private static void InspectShadow(Texture2D image, Camera camera, SpriteRenderer shadow, string output)
        {
            Color32[] pixels = image.GetPixels32();
            Color32 background = pixels[5 + 5 * image.width];
            int brighter = pixels.Count(pixel => pixel.r > background.r + 2 || pixel.g > background.g + 2 || pixel.b > background.b + 2);
            var evidence = new StringBuilder();
            evidence.AppendLine($"Isolated original Shadow on middle gray: background RGB={background.r},{background.g},{background.b}; brighter pixels (2-byte tolerance)={brighter}");
            evidence.AppendLine("All other renderers are hidden only on the temporary diagnostic instance. Original Shadow geometry, texture, material, and animation alpha remain unchanged.");
            Bounds local = shadow.sprite.bounds;
            float[] along = { 0.05f, 0.25f, 0.5f, 0.75f, 0.9f, 0.98f };
            var darkness = new float[along.Length];
            for (int index = 0; index < along.Length; index++)
            {
                Vector3 point = shadow.transform.TransformPoint(new Vector3(Mathf.Lerp(local.min.x, local.max.x, along[index]), local.center.y, 0f));
                Vector3 viewport = camera.WorldToViewportPoint(point);
                int x = Mathf.Clamp(Mathf.RoundToInt(viewport.x * (image.width - 1)), 0, image.width - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(viewport.y * (image.height - 1)), 0, image.height - 1);
                Color32 pixel = pixels[x + y * image.width];
                darkness[index] = (background.r + background.g + background.b - pixel.r - pixel.g - pixel.b) / 3f;
                evidence.AppendLine($"Long-axis fraction={along[index]:F2}; image=({x},{y}); RGB={pixel.r},{pixel.g},{pixel.b}; darkening={darkness[index]:F3}/255");
            }
            evidence.AppendLine("A multiply shadow should only darken, with the distant source sprite tail returning continuously toward the gray background.");
            evidence.AppendLine(OvidSummonNativeGasMigration.RestorationLimits);
            string project = Directory.GetParent(Application.dataPath)?.FullName ?? throw new InvalidOperationException("Missing project root.");
            string absolute = Path.IsPathRooted(output) ? output : Path.Combine(project, output);
            File.AppendAllText(Path.ChangeExtension(absolute, ".txt"), evidence.ToString());
            Debug.Log(evidence.ToString());
            if (brighter != 0 || darkness[0] < 15f || darkness[darkness.Length - 1] > 4f ||
                darkness[1] <= darkness[3] || darkness[3] <= darkness[4])
                throw new InvalidOperationException("Shadow gray-background evidence did not confirm darkening, smooth fade and a transparent non-brightening tail.");
        }

        [MenuItem("Tools/HellMaiden Migration/Capture Ovid Summon Presentation Preview")]
        public static void Capture()
        {
            string output = Environment.GetEnvironmentVariable("OVID_SUMMON_PREVIEW_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) output = "Logs/Phase02/Summon-Preview.png";
            string folder = Path.GetDirectoryName(output) ?? "";
            string stem = Path.GetFileNameWithoutExtension(output);
            AnimationClip cocoon = Clip(OvidSummonNativeGasMigration.CacoonClipPath);
            AnimationClip birth = Clip(OvidSummonNativeGasMigration.BirthClipPath);
            AnimationClip[] attack =
            {
                Clip(OvidSummonNativeGasMigration.EnterClipPath),
                Clip(OvidSummonNativeGasMigration.MainClipPath),
                Clip(OvidSummonNativeGasMigration.ExitClipPath)
            };
            string[] names = { "Default", "Fire", "Poison" };
            string[] paths =
            {
                OvidSummonNativeGasMigration.DefaultAttackPath,
                OvidSummonNativeGasMigration.FireAttackPath,
                OvidSummonNativeGasMigration.PoisonAttackPath
            };
            for (int index = 0; index < paths.Length; index++)
            {
                CaptureOne(paths[index], new[] { cocoon }, new[] { cocoon.length }, 0.8f,
                    Path.Combine(folder, stem + "-" + names[index] + "-Cocoon.png"), names[index] + " Cocoon");
                CaptureOne(paths[index], new[] { birth }, new[] { birth.length }, 1.4f,
                    Path.Combine(folder, stem + "-" + names[index] + "-Birth.png"), names[index] + " Birth");
                float[] duration = { attack[0].length, 1f, attack[2].length };
                CaptureOne(paths[index], attack, duration, 1.5f,
                    Path.Combine(folder, stem + "-" + names[index] + "-Main.png"), names[index] + " Main");
                CaptureOne(paths[index], attack, duration, 2.45f,
                    Path.Combine(folder, stem + "-" + names[index] + "-Exit.png"), names[index] + " Exit");
            }
        }

        private static void CaptureOne(string path, AnimationClip[] clips, float[] phases, float age, string output, string label)
        {
            DanteAttackPresentationPreview.Capture(path, clips, phases, age, output, "Ovid Summon " + label,
                animationRootPath: OvidSummonNativeGasMigration.VisualRootPath);
            string project = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve Unity project root.");
            string absolute = Path.IsPathRooted(output) ? output : Path.Combine(project, output);
            File.AppendAllText(Path.ChangeExtension(absolute, ".txt"),
                "Source Animator is Iso Rotation/Self Rotation; sampling preserves the authored -45 degree Iso Rotation.\n" +
                "Cocoon gameplay lasts 60 session seconds. Main is held for Duration=1 even though its nonloop clip is 0.33333334s.\n" +
                "Exit disables the collider at .33333334s and its Laser ancestor at .6333333s; no extra hit tail exists after Laser deactivation.\n" +
                "The source WingFlap/Transform emitters use Play/Stop=None; no missing beam audio or inferred playback is injected.\n" +
                "This capture runs source animation and particles only; native and network tests validate ownership and hits.\n" +
                OvidSummonNativeGasMigration.RestorationLimits + "\n");
            SpriteRenderer shadow = AssetDatabase.LoadAssetAtPath<GameObject>(path).transform
                .Find(OvidSummonNativeGasMigration.ShadowPath).GetComponent<SpriteRenderer>();
            Material material = shadow.sharedMaterial;
            File.AppendAllText(Path.ChangeExtension(absolute, ".txt"),
                "Shadow evidence: path=" + OvidSummonNativeGasMigration.ShadowPath +
                "; source local position=" + shadow.transform.localPosition + "; source local scale=" + shadow.transform.localScale +
                "; source sprite=" + AssetDatabase.GetAssetPath(shadow.sprite) + "; source texture=" + AssetDatabase.GetAssetPath(shadow.sprite.texture) +
                "; mapped shader=" + material.shader.name + "; original tint=" + material.GetColor("_Color") +
                "; approximate long-axis fade=" + material.GetFloat("_ShadowFadeStart").ToString("F6", CultureInfo.InvariantCulture) +
                ".." + material.GetFloat("_ShadowFadeEnd").ToString("F6", CultureInfo.InvariantCulture) +
                "; source SSU fade/rotation/width preserved=" + material.GetFloat("_DirectionalAlphaFadeFade") + "/" +
                material.GetFloat("_DirectionalAlphaFadeRotation") + "/" + material.GetFloat("_DirectionalAlphaFadeWidth") + ".\n" +
                "Inspect the actual shadow tail, not just colored-pixel coverage: no opaque rectangle should end abruptly; this remains an approximation of the unavailable SSU directional/noise equation.\n");
        }

        private static AnimationClip Clip(string path) => AssetDatabase.LoadAssetAtPath<AnimationClip>(path)
            ?? throw new InvalidDataException("Import Ovid Summon source clips before capturing: " + path);
    }
}
