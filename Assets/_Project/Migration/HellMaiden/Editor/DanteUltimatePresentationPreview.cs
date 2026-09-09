using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.HellMaidenMigration.Editor
{
    /// <summary>Runs one real remote Ultimate, including both source animation-driven waves, in an isolated preview.</summary>
    [InitializeOnLoad]
    public static class DanteUltimatePresentationPreview
    {
        private const int Resolution = 1024;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string PendingCaptureKey = "HellMaiden.DanteUltimate.PendingPlayModePreview";

        static DanteUltimatePresentationPreview()
        {
            if (SessionState.GetBool(PendingCaptureKey, false))
                EditorApplication.playModeStateChanged += CaptureAfterPlayMode;
        }

        [MenuItem("Tools/HellMaiden Migration/Capture Dante Ultimate Presentation Preview")]
        public static void Capture()
        {
            if (Application.isPlaying) { CaptureInPlayMode(); return; }
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Ultimate preview must run in Play Mode so original animation events are delivered. Enter Play Mode in an empty scene first.");
            if (Environment.GetCommandLineArgs().Any(argument => string.Equals(argument, "-quit", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Omit -quit: Ultimate preview enters Play Mode and exits Unity itself after capture.");
            // A batch validation clone has no user scene to preserve. An empty scene avoids Boot, UI and old global managers.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.playModeStartScene = null;
            SessionState.SetBool(PendingCaptureKey, true);
            EditorApplication.playModeStateChanged -= CaptureAfterPlayMode;
            EditorApplication.playModeStateChanged += CaptureAfterPlayMode;
            EditorApplication.EnterPlaymode();
        }

        private static void CaptureAfterPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingCaptureKey, false)) return;
            EditorApplication.delayCall += CapturePending;
        }

        private static void CapturePending()
        {
            if (!SessionState.GetBool(PendingCaptureKey, false)) return;
            SessionState.EraseBool(PendingCaptureKey);
            EditorApplication.playModeStateChanged -= CaptureAfterPlayMode;
            int exitCode = 0;
            try { CaptureInPlayMode(); }
            catch (Exception exception) { Debug.LogException(exception); exitCode = 1; }
            finally { EditorApplication.Exit(exitCode); }
        }

        private static void CaptureInPlayMode()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Original Ultimate animation events require Play Mode.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Ultimate visual capture requires a graphics device; omit -nographics.");
            var definition = AssetDatabase.LoadAssetAtPath<UltimateData>(DanteUltimateAssetMigration.DefinitionPath)
                ?? throw new InvalidDataException("Run DanteUltimateAssetMigration.Import before capturing the Ultimate.");
            var prefab = definition.ultimateAttackWeaponBehaviour as DanteUltimateAttack;
            if (prefab == null) throw new InvalidDataException("The imported Dante Ultimate attack reference is missing.");
            if (prefab.GetComponentsInChildren<Transform>(true).Length != 20 ||
                prefab.GetComponentsInChildren<ParticleSystem>(true).Length != 10 ||
                prefab.DanteUltimateWavePrefab.GetComponentsInChildren<ParticleSystem>(true).Length != 428)
                throw new InvalidDataException("Ultimate preview requires the complete original main and wave hierarchies.");
            MethodInfo advance = typeof(DanteUltimateAttack).GetMethod("Advance", Private, null,
                new[] { typeof(float), typeof(bool) }, null)
                ?? throw new MissingMethodException("DanteUltimateAttack.Advance(float, bool)");
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve the Unity project root.");
            string output = Environment.GetEnvironmentVariable("DANTE_ULTIMATE_PREVIEW_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) output = "Logs/Phase02/Ultimate-Preview.png";
            output = Path.GetFullPath(Path.IsPathRooted(output) ? output : Path.Combine(projectRoot, output));
            string folder = Path.GetDirectoryName(output);
            Directory.CreateDirectory(folder);
            string stem = Path.GetFileNameWithoutExtension(output);

            var cameraObject = new GameObject("Ultimate Capture Camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            var materialCopies = new List<Material>();
            var preparedRenderers = new HashSet<Renderer>();
            var events = new List<string>();
            var stage = new GameObject("Dante Ultimate Presentation Preview") { hideFlags = HideFlags.HideAndDontSave };
            stage.SetActive(false);
            DanteUltimateAttack root = null;
            try
            {
                // Keep the actual attack in this ordinary Play Mode scene. Preview scenes suppress
                // the later Animator events even when Animator.Update advances the original clips.
                var ownerObject = new GameObject("Inactive presentation owner");
                ownerObject.SetActive(false);
                ownerObject.transform.SetParent(stage.transform, false);
                var owner = ownerObject.AddComponent<PlayerMovement>();
                var attacks = new GameObject("Player attacks");
                attacks.transform.SetParent(stage.transform, false);
                owner.AttacksParent = attacks.transform;
                root = Object.Instantiate(prefab, attacks.transform);
                root.gameObject.SetActive(false);
                CloneMaterials(root, materialCopies, preparedRenderers);
                root.InitializePresentationReplica(owner);
                root.WaveStarted += ordinal =>
                {
                    // This notification precedes wave playback, so material animation cannot write source assets.
                    CloneMaterials(root, materialCopies, preparedRenderers);
                    events.Add($"WaveStarted ordinal={ordinal}; root elapsed={root.ElapsedSeconds:F6}");
                };
                root.WaveEnded += ordinal => events.Add($"WaveEnded ordinal={ordinal}; root elapsed={root.ElapsedSeconds:F6}");
                stage.SetActive(true);
                var stats = new ProjectilePresentationStats
                {
                    EffectiveSpeed = definition.BaseStats.speed,
                    Duration = definition.BaseStats.duration,
                    ProjectileCount = definition.BaseStats.projectileCount,
                    BaseProjectileCount = definition.BaseStats.projectileCount
                };
                // A non-zero starting age skips historical audio and uses the real replica's particle seek.
                if (!root.PlayPresentation(new UltimatePresentationSpawn(definition.Id, 1, stats), 0.5f))
                    throw new InvalidOperationException("The real Ultimate presentation replica rejected its initial spawn.");
                CaptureFrame(camera, root, 0.5f, 1, "Start", Path.Combine(folder, stem + "-Start.png"), events);
                Advance(advance, root, 2.3f - root.ElapsedSeconds);
                CaptureFrame(camera, root, 2.3f, 2, "SecondWave", Path.Combine(folder, stem + "-SecondWave.png"), events);
                Advance(advance, root, 4.4f - root.ElapsedSeconds);
                CaptureFrame(camera, root, 4.4f, 1, "Tail", Path.Combine(folder, stem + "-Tail.png"), events);
                Advance(advance, root, root.SequenceDuration - root.ElapsedSeconds + 0.02f);
                if (root.ActiveUseId != 0 || root.GetComponentsInChildren<UltimateDamageAttack>(true).Any(wave => wave.IsWavePlaying))
                    throw new InvalidOperationException("The preview's real Ultimate sequence did not release both waves.");
                Debug.Log("Dante Ultimate preview captured three stages and completed at " + root.ElapsedSeconds.ToString("F6") + " seconds.");
            }
            finally
            {
                if (root != null) root.DisposeNativeUltimate();
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                if (stage != null) Object.DestroyImmediate(stage);
                foreach (Material material in materialCopies) if (material != null) Object.DestroyImmediate(material);
            }
        }

        private static void Advance(MethodInfo advance, DanteUltimateAttack root, float seconds)
        {
            // Use the production scheduler and its particle seek; never sample a clip independently of its events.
            try { advance.Invoke(root, new object[] { seconds, true }); }
            catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
        }

        private static void CloneMaterials(DanteUltimateAttack root, ICollection<Material> copies,
            ISet<Renderer> prepared)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!prepared.Add(renderer)) continue;
                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material original = materials[slot];
                    if (original == null) continue;
                    var copy = new Material(original) { hideFlags = HideFlags.HideAndDontSave };
                    copies.Add(copy);
                    materials[slot] = copy;
                }
                renderer.sharedMaterials = materials;
            }
        }

        private static void CaptureFrame(Camera camera, DanteUltimateAttack root, float targetTime,
            int expectedActiveWaves, string label, string output, IList<string> events)
        {
            UltimateDamageAttack[] waves = root.GetComponentsInChildren<UltimateDamageAttack>(true);
            ParticleSystem[] particles = root.GetComponentsInChildren<ParticleSystem>(true);
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (!root.IsPresentationActive || Mathf.Abs(root.ElapsedSeconds - targetTime) > 0.001f ||
                waves.Count(wave => wave.IsWavePlaying) != expectedActiveWaves ||
                particles.Length != 10 + waves.Length * 428 ||
                typeof(DanteUltimateAttack).GetProperty("NativeRuntime")?.GetValue(root) != null || root.WeaponData != null)
                throw new InvalidOperationException($"Ultimate preview invalid at {label}: presentation={root.IsPresentationActive}, rootId={root.ActiveUseId}, " +
                    $"elapsed={root.ElapsedSeconds:F6}, target={targetTime:F6}, waves={waves.Length}, active={waves.Count(wave => wave.IsWavePlaying)}/{expectedActiveWaves}, " +
                    $"particles={particles.Length}/{10 + waves.Length * 428}, animatorEnabled={root.animator.enabled}, fireEvents={root.animator.fireEvents}, " +
                    $"currentState={root.animator.GetCurrentAnimatorStateInfo(0).shortNameHash}, normalizedTime={root.animator.GetCurrentAnimatorStateInfo(0).normalizedTime:F6}, " +
                    $"events=[{string.Join(" | ", events)}].");
            PropertyInfo snapshot = typeof(BasePlayerAttack).GetProperty("NativeAttackSnapshot", Private);
            foreach (UltimateDamageAttack wave in waves)
                if (snapshot == null || snapshot.GetValue(wave) != null || wave.hitbox.collider.enabled)
                    throw new InvalidOperationException("A presentation wave owns a GAS snapshot or an enabled damage collider.");

            // Freeze only these temporary instances for rendering. Original simulation speeds and scaled/unscaled clocks stay intact.
            foreach (ParticleSystem particle in particles)
                if (particle.gameObject.activeInHierarchy) particle.Pause(false);
            Renderer[] visible = renderers.Where(IsVisible).ToArray();
            if (visible.Length == 0) throw new InvalidOperationException("Ultimate preview has no visible source renderer at " + label + ".");
            Bounds bounds = visible[0].bounds;
            foreach (Renderer renderer in visible.Skip(1)) bounds.Encapsulate(renderer.bounds);
            if (!float.IsFinite(bounds.extents.magnitude)) throw new InvalidOperationException("Ultimate preview bounds are not finite.");
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
            Texture2D image = null;
            RenderTexture target = RenderTexture.GetTemporary(Resolution, Resolution, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previousTarget = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                if (GraphicsSettings.currentRenderPipeline != null)
                    RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                else camera.Render();
                RenderTexture.active = target;
                image = new Texture2D(Resolution, Resolution, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
                image.Apply();
                File.WriteAllBytes(output, image.EncodeToPNG());
                int coloredPixels = image.GetPixels32().Count(pixel =>
                    Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)) - Math.Min(pixel.r, Math.Min(pixel.g, pixel.b)) > 20 &&
                    Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)) > 65);
                int liveParticles = particles.Where(particle => particle.gameObject.activeInHierarchy).Sum(particle => particle.particleCount);
                var evidence = new StringBuilder();
                evidence.AppendLine($"Dante Ultimate real presentation replica: {label}; root elapsed={root.ElapsedSeconds:F6}; source ID={root.ultimateData.Id}");
                evidence.AppendLine($"Source main=20 transforms / 10 particle systems; each original wave=428 particle systems; instantiated waves={waves.Length}");
                evidence.AppendLine($"Actual transforms={root.GetComponentsInChildren<Transform>(true).Length}; particle systems={particles.Length}; live active particles={liveParticles}");
                evidence.AppendLine($"Active waves={expectedActiveWaves}; renderers={renderers.Length}; visible nonempty renderers={visible.Length}; colored pixels={coloredPixels}/{Resolution * Resolution}");
                evidence.AppendLine($"Source entry blend={root.EntryTransitionDuration:F6}; sequence duration={root.SequenceDuration:F6}; Native runtime=null; WeaponData=null; all wave GAS snapshots=null; damage colliders disabled");
                evidence.AppendLine($"Bounds={bounds}; orthographic size={camera.orthographicSize}; graphics={SystemInfo.graphicsDeviceType}");
                foreach (string value in events) evidence.AppendLine(value);
                for (int index = 0; index < waves.Length; index++)
                    evidence.AppendLine($"Wave[{index}] active={waves[index].IsWavePlaying}; elapsed={waves[index].WaveElapsed:F6}; particle systems={waves[index].GetComponentsInChildren<ParticleSystem>(true).Length}");
                foreach (Renderer renderer in renderers)
                {
                    string path = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
                    ParticleSystem particle = renderer.GetComponent<ParticleSystem>();
                    string clock = particle == null ? "n/a" : $"time={particle.time:F6}; speed={particle.main.simulationSpeed}; unscaled={particle.main.useUnscaledTime}; emission={particle.emission.enabled}";
                    Material[] materials = renderer.sharedMaterials;
                    for (int slot = 0; slot < materials.Length; slot++)
                    {
                        Material material = materials[slot];
                        evidence.AppendLine($"Renderer={path}; active={renderer.gameObject.activeInHierarchy}; enabled={renderer.enabled}; particles={(particle == null ? 0 : particle.particleCount)}; {clock}; " +
                            $"slot={slot}; material={(material == null ? "<authored null container>" : material.name)}; shader={(material == null || material.shader == null ? "<none>" : material.shader.name)}");
                    }
                }
                evidence.AppendLine("The actual replica APIs and source controller events drive these three captures. Temporary material copies prevent animation from modifying shared assets; no source transform, particle parameter, clock, or animation curve is changed.");
                evidence.AppendLine("This preview verifies visible resources and presentation lifecycle; the separate Native and network tests verify authority and damage. Shader compatibility and unresolved exporter properties limit exact visual restoration.");
                evidence.AppendLine(DanteUltimateAssetMigration.RestorationLimits);
                File.WriteAllText(Path.ChangeExtension(output, ".txt"), evidence.ToString());
                Debug.Log($"Dante Ultimate {label} preview: {output}; live particles={liveParticles}; colored pixels={coloredPixels}");
                if (liveParticles == 0 || coloredPixels < 100)
                    throw new InvalidOperationException("Ultimate preview is empty or nearly uncolored; inspect its image and renderer diagnostics.");
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previousTarget;
                RenderTexture.ReleaseTemporary(target);
                if (image != null) Object.DestroyImmediate(image);
            }
        }

        private static bool IsVisible(Renderer renderer)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sharedMaterial == null) return false;
            if (renderer is SpriteRenderer sprite && (sprite.sprite == null || sprite.color.a <= 0f)) return false;
            ParticleSystem particles = renderer.GetComponent<ParticleSystem>();
            return particles == null || particles.particleCount > 0;
        }
    }
}
