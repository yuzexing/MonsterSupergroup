using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.HellMaidenMigration.Editor
{
    /// <summary>Renders the actual trail particle prefabs at fixed source world samples; no gameplay is executed.</summary>
    public static class DanteDashPresentationPreview
    {
        private const int Resolution = 1024;
        private const float SampleTime = 1f;
        private const float SegmentLife = 0.625f;

        [MenuItem("Tools/HellMaiden Migration/Capture Dante Dash Presentation Preview")]
        public static void Capture()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Trail preview requires a graphics device; omit -nographics.");
            string output = Environment.GetEnvironmentVariable("DANTE_DASH_PREVIEW_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) output = "Logs/Phase02/Dash-Preview.png";
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve Unity project root.");
            output = Path.GetFullPath(Path.IsPathRooted(output) ? output : Path.Combine(projectRoot, output));
            string folder = Path.GetDirectoryName(output);
            Directory.CreateDirectory(folder);
            string stem = Path.GetFileNameWithoutExtension(output);
            foreach (bool poison in new[] { false, true })
            {
                string variant = poison ? "Poison" : "Fire";
                CaptureVariant(poison ? DanteDashNativeGasMigration.PoisonAttackPath : DanteDashNativeGasMigration.FireAttackPath,
                    Path.Combine(folder, stem + "-" + variant + ".png"), variant);
            }
        }

        private static void CaptureVariant(string prefabPath, string output, string variant)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)
                ?? throw new InvalidDataException("Import the real Dash prefab first: " + prefabPath);
            var definition = new SerializedObject(prefab.GetComponent<MultiParticlePlayerTrailAttack>());
            var segmentPrefab = definition.FindProperty("trailParticles").objectReferenceValue as ParticleSystem;
            if (segmentPrefab == null) throw new InvalidDataException("The original trail particle reference is missing.");
            float delta = definition.FindProperty("trailDelta").floatValue;
            if (!Mathf.Approximately(delta, 1f)) throw new InvalidDataException("This source trace expects the authored trailDelta=1.");

            var preview = new PreviewRenderUtility();
            var copies = new List<Material>();
            GameObject stage = new GameObject("Dante Dash " + variant + " Preview") { hideFlags = HideFlags.HideAndDontSave };
            stage.SetActive(false);
            Texture2D image = null;
            try
            {
                preview.AddSingleGO(stage);
                GameObject attack = Object.Instantiate(prefab, stage.transform);
                attack.name = "Original " + variant + " FireTrailAttack";
                attack.transform.position = new Vector3(-3f, 0f, 0f);
                foreach (MonoBehaviour script in attack.GetComponentsInChildren<MonoBehaviour>(true)) if (script != null) script.enabled = false;
                foreach (Collider2D collider in attack.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
                var step = (ParticleSystem)new SerializedObject(attack.GetComponent<MultiParticlePlayerTrailAttack>())
                    .FindProperty("trailStepParticles").objectReferenceValue;
                if (step == null) throw new InvalidDataException("The original moving step particles are missing.");
                step.gameObject.SetActive(true);

                // A source-compatible straight movement trace: each observed owner position is
                // 1.2 world units beyond the last point, so the original projected threshold is
                // exceeded and exactly one trailDelta=1 point is added. No prefab curve is edited.
                var samples = new List<(Vector2 point, Vector2 owner, float time, ParticleSystem particles)>();
                Vector2 last = new Vector2(-3f, 0f);
                for (int index = 0; index < 6; index++)
                {
                    Vector2 owner = last + Vector2.right * 1.2f;
                    Vector2 offset = owner - last;
                    if (new Vector2(offset.x - offset.y, (offset.y + offset.x) * 0.5f).magnitude <= delta)
                        throw new InvalidOperationException("The fixed source trace did not exceed the sampling threshold.");
                    offset.y *= 0.5f;
                    last += offset.normalized * delta;
                    ParticleSystem particles = Object.Instantiate(segmentPrefab, stage.transform);
                    particles.name = "World sample " + index;
                    particles.transform.position = last;
                    foreach (MonoBehaviour script in particles.GetComponentsInChildren<MonoBehaviour>(true)) if (script != null) script.enabled = false;
                    samples.Add((last, owner, 0.1f + index * 0.15f, particles));
                }

                Renderer[] renderers = stage.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (int index = 0; index < materials.Length; index++)
                    {
                        if (materials[index] == null) continue;
                        materials[index] = new Material(materials[index]) { hideFlags = HideFlags.HideAndDontSave };
                        copies.Add(materials[index]);
                    }
                    renderer.sharedMaterials = materials;
                }
                ParticleSystem[] allParticles = stage.GetComponentsInChildren<ParticleSystem>(true);
                for (int index = 0; index < allParticles.Length; index++)
                {
                    allParticles[index].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    allParticles[index].useAutoRandomSeed = false;
                    allParticles[index].randomSeed = (uint)(1729 + index);
                }
                stage.SetActive(true);
                foreach (var sample in samples)
                {
                    float age = SampleTime - sample.time;
                    sample.particles.Simulate(Mathf.Min(age, SegmentLife), true, true, false);
                    if (age > SegmentLife)
                    {
                        sample.particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                        sample.particles.Simulate(age - SegmentLife, true, false, false);
                    }
                    sample.particles.Pause(true);
                }
                step.transform.position = new Vector3(-3f, 0f, 0f);
                step.Simulate(0f, true, true, false);
                for (float time = 0f; time < SampleTime;)
                {
                    float dt = Mathf.Min(1f / 120f, SampleTime - time);
                    time += dt;
                    foreach (var sample in samples) if (sample.time <= time) step.transform.position = sample.owner;
                    step.Simulate(dt, true, false, false);
                }
                step.Pause(true);

                Bounds bounds = BoundsOf(renderers);
                Camera camera = preview.camera;
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(1.5f, Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.15f);
                camera.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.center.y, bounds.min.z - 10f), Quaternion.identity);
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = Mathf.Max(100f, bounds.size.z + 20f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.025f, 0.03f, 0.05f, 1f);
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.cullingMask = -1;
                preview.ambientColor = Color.white;
                preview.lights[0].intensity = 1f;
                preview.lights[1].intensity = 1f;
                preview.BeginStaticPreview(new Rect(0, 0, Resolution, Resolution));
                preview.Render(true, true);
                image = preview.EndStaticPreview();
                File.WriteAllBytes(output, image.EncodeToPNG());
                int coloredPixels = image.GetPixels32().Count(pixel =>
                    Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)) - Math.Min(pixel.r, Math.Min(pixel.g, pixel.b)) > 20 &&
                    Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)) > 65);
                var evidence = new StringBuilder();
                evidence.AppendLine("Dante FireTrail original " + variant + " particle resources, sampled at 1.00 seconds.");
                evidence.AppendLine("Sampling duration=1.25; segment emission life=0.625; oldest segments retain only their original particle tails.");
                foreach (var sample in samples)
                    evidence.AppendLine($"World point={sample.point}; source elapsed={sample.time:F2}; age={SampleTime - sample.time:F2}; owner observed={sample.owner}");
                evidence.AppendLine($"Particle systems={allParticles.Length}; live particles={allParticles.Sum(value => value.particleCount)}");
                evidence.AppendLine($"Renderers={renderers.Length}; enabled={renderers.Count(value => value.enabled)}; colored pixels={coloredPixels}/{Resolution * Resolution}");
                evidence.AppendLine($"Bounds={bounds}; camera size={camera.orthographicSize}; graphics={SystemInfo.graphicsDeviceType}");
                foreach (Renderer renderer in renderers)
                {
                    string path = AnimationUtility.CalculateTransformPath(renderer.transform, stage.transform);
                    ParticleSystem particles = renderer.GetComponent<ParticleSystem>();
                    for (int slot = 0; slot < renderer.sharedMaterials.Length; slot++)
                    {
                        Material material = renderer.sharedMaterials[slot];
                        evidence.AppendLine($"Renderer={path}; enabled={renderer.enabled}; particles={(particles == null ? 0 : particles.particleCount)}; " +
                            $"slot={slot}; material={(material == null ? "<authored null container>" : material.name)}; " +
                            $"shader={(material == null || material.shader == null ? "<none>" : material.shader.name)}");
                    }
                }
                evidence.AppendLine("This deterministic presentation trace does not run Native GAS, input, networking or FMOD; those use separate integration tests.");
                evidence.AppendLine(DanteDashNativeGasMigration.RestorationLimits);
                File.WriteAllText(Path.ChangeExtension(output, ".txt"), evidence.ToString());
                Debug.Log("Dante Dash " + variant + " preview: " + output + "; colored pixels=" + coloredPixels);
                if (coloredPixels < 100) throw new InvalidOperationException("Dash preview is not visibly rendered; inspect the image and per-renderer diagnostics.");
            }
            finally
            {
                preview.Cleanup();
                if (stage != null) Object.DestroyImmediate(stage);
                foreach (Material material in copies) if (material != null) Object.DestroyImmediate(material);
                if (image != null) Object.DestroyImmediate(image);
            }
        }

        private static Bounds BoundsOf(Renderer[] renderers)
        {
            Renderer[] visible = renderers.Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.sharedMaterial != null).ToArray();
            if (visible.Length == 0) throw new InvalidOperationException("Dash preview has no visible renderer.");
            Bounds bounds = visible[0].bounds;
            foreach (Renderer renderer in visible.Skip(1)) bounds.Encapsulate(renderer.bounds);
            if (!float.IsFinite(bounds.extents.magnitude)) throw new InvalidOperationException("Dash preview bounds are invalid.");
            return bounds;
        }
    }
}
