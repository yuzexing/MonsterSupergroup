using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.HellMaidenMigration.Editor
{
    /// <summary>Shared isolated rendering of source clips; never constructs an attack or network runtime.</summary>
    internal static class DanteAttackPresentationPreview
    {
        private const int Resolution = 1024;

        internal static void Capture(string prefabPath, AnimationClip[] clips, float[] phaseDurations,
            float sampleTime, string output, string label, bool simulateInactiveParticles = false,
            Vector2? direction = null, Quaternion? rootRotation = null, string animationRootPath = "Root",
            Color? backgroundColor = null, string isolatedRendererPath = null, int minimumColoredPixels = 100,
            Action<Texture2D, Camera, Renderer[]> inspectImage = null)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Attack visual capture needs a graphics device; omit -nographics.");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)
                ?? throw new InvalidDataException("Import the attack prefab before rendering a preview: " + prefabPath);
            if (clips == null || clips.Length == 0 || phaseDurations == null || clips.Length != phaseDurations.Length ||
                clips.Any(clip => clip == null) || phaseDurations.Any(duration => !float.IsFinite(duration) || duration <= 0f) ||
                !float.IsFinite(sampleTime) || sampleTime < 0f || sampleTime >= phaseDurations.Sum())
                throw new ArgumentException("Preview requires valid source clips, phase durations and an in-range sample time.");
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve the Unity project root.");
            output = Path.GetFullPath(Path.IsPathRooted(output) ? output : Path.Combine(projectRoot, output));
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            var preview = new PreviewRenderUtility();
            var materials = new List<Material>();
            Texture2D image = null;
            GameObject instance = null;
            try
            {
                instance = Object.Instantiate(prefab);
                instance.name = label + " Preview (temporary)";
                instance.hideFlags = HideFlags.HideAndDontSave;
                if (rootRotation.HasValue) instance.transform.rotation = rootRotation.Value;
                preview.AddSingleGO(instance);
                foreach (MonoBehaviour script in instance.GetComponentsInChildren<MonoBehaviour>(true))
                    if (script != null) script.enabled = false;
                foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                foreach (Collider2D collider in instance.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;

                // Sampling authored material curves must never modify any imported/shared material.
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    Material[] copies = renderer.sharedMaterials;
                    for (int i = 0; i < copies.Length; i++)
                    {
                        if (copies[i] == null) continue;
                        copies[i] = new Material(copies[i]) { hideFlags = HideFlags.HideAndDontSave };
                        materials.Add(copies[i]);
                    }
                    renderer.sharedMaterials = copies;
                }
                AttackProgressionScaler scaler = instance.GetComponent<AttackProgressionScaler>();
                if (scaler == null) scaler = instance.GetComponent<SummonAIBehaviour>()?.ProgressionScaler;
                if (scaler == null) throw new InvalidDataException("Missing source progression scaler reference: " + prefabPath);
                scaler.SetDefaultValues();
                if (direction.HasValue) instance.GetComponent<AnimatedAttack>().UpdateRotation(direction.Value);
                Transform animationRoot = string.IsNullOrEmpty(animationRootPath) ? instance.transform :
                    instance.transform.Find(animationRootPath);
                if (animationRoot == null) throw new InvalidDataException("Missing source animation root: " + animationRootPath);
                GameObject animatedRoot = animationRoot.gameObject;
                ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
                var previouslyActive = new bool[particles.Length];
                for (int i = 0; i < particles.Length; i++)
                {
                    particles[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    particles[i].useAutoRandomSeed = false;
                    particles[i].randomSeed = (uint)(1729 + i);
                    particles[i].Simulate(0f, false, true, false);
                }
                Sample(clips, phaseDurations, animatedRoot, 0f);
                float elapsed = 0f;
                while (elapsed < sampleTime)
                {
                    float delta = Mathf.Min(1f / 120f, sampleTime - elapsed);
                    elapsed += delta;
                    Sample(clips, phaseDurations, animatedRoot, elapsed);
                    for (int i = 0; i < particles.Length; i++)
                    {
                        bool active = particles[i].gameObject.activeInHierarchy;
                        if (simulateInactiveParticles || active)
                        {
                            // A phase that newly enables the authored emitter starts its burst at zero.
                            if (!simulateInactiveParticles && !previouslyActive[i]) particles[i].Simulate(0f, false, true, false);
                            particles[i].Simulate(delta, false, false, false);
                        }
                        previouslyActive[i] = active;
                    }
                }

                if (!string.IsNullOrEmpty(isolatedRendererPath))
                {
                    Renderer isolated = instance.transform.Find(isolatedRendererPath)?.GetComponent<Renderer>()
                        ?? throw new InvalidDataException("Missing isolated preview renderer: " + isolatedRendererPath);
                    foreach (Renderer renderer in renderers) renderer.enabled = renderer == isolated;
                }
                Bounds bounds = VisibleBounds(renderers);
                Camera camera = preview.camera;
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(1.5f, Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.15f);
                camera.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.center.y, bounds.min.z - 10f), Quaternion.identity);
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = Mathf.Max(100f, bounds.size.z + 20f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = backgroundColor ?? new Color(0.025f, 0.03f, 0.05f, 1f);
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
                string evidence = $"Original clip sample: {sampleTime:F2} seconds\n" +
                    $"Particle systems: {particles.Length}\nLive particles: {particles.Sum(value => value.particleCount)}\n" +
                    $"Enabled renderers: {renderers.Count(value => value.enabled)}\n" +
                    $"Colored pixels: {coloredPixels}/{Resolution * Resolution}\n" +
                    $"Visible bounds: {bounds}\nOrthographic size: {camera.orthographicSize}\n" +
                    $"Graphics device: {SystemInfo.graphicsDeviceType}\n" +
                    "This checks the migrated presentation is visible; incomplete source shaders prevent an exact visual comparison.\n";
                File.WriteAllText(Path.ChangeExtension(output, ".txt"), evidence);
                Debug.Log(label + " presentation preview: " + output + "\n" + evidence);
                inspectImage?.Invoke(image, camera, renderers);
                if (coloredPixels < minimumColoredPixels)
                    throw new InvalidOperationException($"Attack preview contains fewer than {minimumColoredPixels} colored pixels; inspect its materials and camera.");
            }
            finally
            {
                preview.Cleanup();
                if (instance != null) Object.DestroyImmediate(instance);
                foreach (Material material in materials) if (material != null) Object.DestroyImmediate(material);
                if (image != null) Object.DestroyImmediate(image);
            }
        }

        private static void Sample(AnimationClip[] clips, float[] phaseDurations, GameObject animatedRoot, float time)
        {
            for (int i = 0; i < clips.Length; i++)
            {
                if (time < phaseDurations[i] || i == clips.Length - 1)
                {
                    // Non-looping source Idle holds its last authored frame when Duration exceeds four seconds.
                    clips[i].SampleAnimation(animatedRoot, Mathf.Min(time, clips[i].length));
                    return;
                }
                time -= phaseDurations[i];
            }
        }

        private static Bounds VisibleBounds(IEnumerable<Renderer> renderers)
        {
            bool initialized = false;
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 3f);
            foreach (Renderer renderer in renderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sharedMaterial == null) continue;
                if (!initialized) { bounds = renderer.bounds; initialized = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!initialized || !float.IsFinite(bounds.extents.magnitude))
                throw new InvalidOperationException("Attack preview has no finite visible renderer bounds.");
            return bounds;
        }
    }
}
