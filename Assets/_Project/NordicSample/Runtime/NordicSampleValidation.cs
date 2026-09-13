using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MonsterSupergroup.NordicSample
{
    /// <summary>Opt-in standalone acceptance run; dormant during normal preview use.</summary>
    public sealed class NordicSampleValidation : MonoBehaviour
    {
        [Serializable] private sealed class Result
        {
            public bool passed;
            public int sprites, props, particleSystems, torchLights;
            public string[] checks;
            public string[] errors;
            public string[] runtimeBehaviours;
        }
        private NordicSamplePreview preview;
        private string output;
        private readonly List<string> checks = new List<string>();
        private readonly List<string> errors = new List<string>();
        private bool active;

        private void Start()
        {
            string arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--nordic-validate="));
            if (arg == null) return;
            output = Path.GetFullPath(arg.Substring("--nordic-validate=".Length));
            Directory.CreateDirectory(output);
            Application.runInBackground = true;
            Debug.Log("[NordicAcceptance] Starting standalone checks: " + output);
            active = true;
            Application.logMessageReceived += OnLog;
            preview = GetComponent<NordicSamplePreview>();
            preview.acceptInput = false; preview.showHelp = false;
            StartCoroutine(Run());
        }

        private void OnLog(string message, string stack, LogType type)
        {
            if (type is LogType.Exception or LogType.Error or LogType.Assert) errors.Add(message);
        }
        private void Check(bool condition, string name)
        {
            if (condition) checks.Add(name);
            else errors.Add(name);
        }

        private IEnumerator Run()
        {
            yield return new WaitForSeconds(1);
            Camera camera = preview.cameraRig.GetComponent<Camera>();
            Bounds bounds = preview.MapBounds;
            if (Environment.GetCommandLineArgs().Contains("--render-regression-only"))
            {
                yield return CheckRenderRegressions(camera);
                File.WriteAllText(Path.Combine(output, "acceptance.json"), JsonUtility.ToJson(new Result
                { passed = errors.Count == 0, checks = checks.ToArray(), errors = errors.ToArray() }, true));
                Application.Quit(errors.Count == 0 ? 0 : 2);
                yield break;
            }
            int originalMask = camera.cullingMask;
            camera.cullingMask = 0;
            yield return new WaitForEndOfFrame();
            Capture(camera, "coverage-control", 16, 16, true);
            camera.cullingMask = originalMask;
            Check(Vector2.Distance(bounds.size, new Vector2(64, 40)) < .001f, "Ground is 64x40");
            Check(QualitySettings.activeColorSpace == ColorSpace.Linear, "Linear color space");
            int spriteCount = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Length;
            int propCount = FindObjectsByType<NordicSortAnchor>(FindObjectsSortMode.None).Length;
            Check(FindObjectsByType<Camera>(FindObjectsSortMode.None).Length == 1, "Single preview camera");
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            Check(behaviours.Where(b => b.gameObject.scene == preview.gameObject.scene).All(b => b.GetType().Namespace != null &&
                (b.GetType().Namespace.StartsWith("MonsterSupergroup.NordicSample") || b.GetType().Namespace.StartsWith("UnityEngine.Rendering") || b.GetType().Namespace.StartsWith("Com.LuisPedroFonseca"))), "Scene has no gameplay/network/generator behaviours");
            Check(!behaviours.Any(b => b.GetType().Name is "NetworkManager" or "BootGameplayNetworkManager" or "NetworkIdentity" or "PlayerController_HMD" or "WorldManager"), "No active network session or formal player");
            foreach (var renderer in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
                if (renderer.sprite == null || renderer.sharedMaterial == null || renderer.sharedMaterial.shader.name.Contains("Error")) errors.Add("Invalid sprite/material: " + renderer.name);

            Vector2[] points = { bounds.center, new Vector2(bounds.min.x + .3f,bounds.center.y),new Vector2(bounds.max.x-.3f,bounds.center.y),
                new Vector2(bounds.center.x,bounds.min.y+.3f),new Vector2(bounds.center.x,bounds.max.y-.3f),
                new Vector2(bounds.min.x+.3f,bounds.min.y+.3f),new Vector2(bounds.max.x-.3f,bounds.min.y+.3f),
                new Vector2(bounds.min.x+.3f,bounds.max.y-.3f),new Vector2(bounds.max.x-.3f,bounds.max.y-.3f) };
            for (int i = 0; i < points.Length; i++)
            {
                preview.Teleport(points[i]);
                yield return new WaitForSeconds(.3f);
                yield return new WaitForEndOfFrame();
                CheckCamera(camera, bounds, "position " + i);
                Capture(camera, "view-" + i, 1920, 1080, true);
            }
            Screen.SetResolution(1440, 1080, FullScreenMode.Windowed);
            yield return new WaitForSeconds(.5f);
            preview.Teleport(points[8]);
            yield return new WaitForSeconds(.3f);
            yield return new WaitForEndOfFrame();
            CheckCamera(camera, bounds, "resized 4:3");
            Capture(camera, "resized-4x3", 1440, 1080, true);
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            yield return new WaitForSeconds(.5f);

            // Verify real movement input normalization, not merely the sorting formula.
            preview.Teleport(bounds.center);
            Vector2 origin = preview.character.position;
            preview.SetPreviewInput(Vector2.right);
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            float straight = Vector2.Distance(origin, preview.character.position);
            Check(preview.characterAnimator.GetCurrentAnimatorStateInfo(0).IsName("Walk"), "Nordic walking animation plays during movement");
            preview.SetPreviewInput(Vector2.zero);
            preview.Teleport(origin);
            preview.SetPreviewInput(Vector2.one);
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            float diagonal = Vector2.Distance(origin, preview.character.position);
            preview.SetPreviewInput(Vector2.zero);
            Check(Mathf.Abs(straight - diagonal) < .08f && straight > .75f && straight < 1.25f, "Movement speed 5 and normalized diagonal");
            yield return new WaitForSeconds(.25f);
            Check(preview.characterAnimator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Nordic idle animation resumes after movement");
            Transform torso = preview.characterAnimator.transform.Find("PlayerSprite/Cuerpo");
            Vector3 initialTorsoPosition = torso.localPosition;
            float poseChange = 0;
            for (int i = 0; i < 4; i++)
            {
                yield return new WaitForSeconds(.13f);
                poseChange = Mathf.Max(poseChange, Vector3.Distance(initialTorsoPosition, torso.localPosition));
            }
            Check(poseChange > .0001f, "Nordic multipart idle pose animates");
            preview.SetPreviewInput(Vector2.left);
            yield return null;
            yield return null;
            Check(preview.characterVisual.localScale.x > 0, "Nordic source pose faces left");
            preview.SetPreviewInput(Vector2.right);
            yield return null;
            yield return null;
            Check(preview.characterVisual.localScale.x < 0, "Nordic mirrored pose faces right");
            preview.SetPreviewInput(Vector2.zero);

            var tree = FindObjectsByType<NordicSortAnchor>(FindObjectsSortMode.None).First(t => t.name == "Tree_1 #0");
            Vector2 root = tree.transform.position;
            var actorSort = preview.character.GetComponent<SortingGroup>();
            preview.Teleport(root + new Vector2(0, 1.2f));
            yield return new WaitForSeconds(.3f);
            Check(actorSort.sortingOrder < tree.GetComponent<SortingGroup>().sortingOrder, "Actor behind tree");
            yield return new WaitForEndOfFrame(); Capture(camera, "tree-behind", 1920, 1080, true);
            preview.Teleport(root + new Vector2(0, -.65f));
            yield return new WaitForSeconds(.3f);
            Check(actorSort.sortingOrder > tree.GetComponent<SortingGroup>().sortingOrder, "Actor in front of tree");
            yield return new WaitForEndOfFrame(); Capture(camera, "tree-front", 1920, 1080, true);
            preview.Teleport(root + new Vector2(0, -1));
            preview.SetPreviewInput(Vector2.up);
            for (int i = 0; i < 35; i++) yield return new WaitForFixedUpdate();
            preview.SetPreviewInput(Vector2.zero);
            var actorCollider = preview.character.GetComponent<Collider2D>();
            var rootCollider = tree.GetComponent<Collider2D>();
            Check(preview.character.position.y < root.y + .5f && preview.character.position.y > root.y - .9f, "Tree root blocks upward movement");
            var separation = actorCollider.Distance(rootCollider);
            Check(!separation.isOverlapped, "Actor stays outside root collider: " + separation.distance.ToString("F6"));
            preview.Teleport(root + new Vector2(0, 1.5f));
            Check(!actorCollider.Distance(rootCollider).isOverlapped, "Canopy does not act as collider");

            preview.Teleport(new Vector2(16, 12) + (Vector2)bounds.center);
            yield return new WaitForSeconds(.4f);
            var torches = FindObjectsByType<NordicTorchAmbient>(FindObjectsSortMode.None);
            float before = torches[0].lights[0].intensity;
            yield return new WaitForSeconds(.17f);
            Check(Mathf.Abs(before - torches[0].lights[0].intensity) > .00001f, "Torch light animates");
            var particles = FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
            Check(particles.Length > 0 && particles.All(p => p.isPlaying && p.main.loop), "Torch particles loop");
            yield return new WaitForEndOfFrame(); Capture(camera, "ruins-torches", 1920, 1080, true);
            Check(spriteCount == FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Length && propCount == FindObjectsByType<NordicSortAnchor>(FindObjectsSortMode.None).Length, "Static map instance counts unchanged");

            yield return CheckRenderRegressions(camera);

            preview.Teleport(bounds.center);
            yield return new WaitForSeconds(.3f);
            yield return new WaitForEndOfFrame(); Capture(camera, "sample-center", 1920, 1080, true);
            var result = new Result { passed = errors.Count == 0, sprites = spriteCount, props = propCount, particleSystems = particles.Length,
                torchLights = torches.Sum(t => t.lights.Length), checks = checks.ToArray(), errors = errors.ToArray(),
                runtimeBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Select(b => b.GetType().FullName + " [" + b.gameObject.scene.name + "]").Distinct().ToArray() };
            File.WriteAllText(Path.Combine(output, "acceptance.json"), JsonUtility.ToJson(result, true));
            Debug.Log("[NordicAcceptance] " + (result.passed ? "PASS" : "FAIL") + ": " + checks.Count + " checks, " + errors.Count + " errors.");
            Application.Quit(result.passed ? 0 : 2);
        }

        private void CheckCamera(Camera camera, Bounds bounds, string context)
        {
            Check(!camera.orthographic && Mathf.Abs(camera.fieldOfView - 80) < .001f && Mathf.Abs(camera.transform.position.z + 10) < .001f, "Camera baseline " + context);
            float height = 2 * -camera.transform.position.z * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            Check(Mathf.Abs(height - 16.781992f) < .002f, "Camera view height " + context);
            bool inside = true;
            foreach (Vector2 corner in new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one })
            {
                Ray ray = camera.ViewportPointToRay(corner);
                Vector3 point = ray.origin + ray.direction * (-ray.origin.z / ray.direction.z);
                inside &= point.x >= bounds.min.x - .01f && point.x <= bounds.max.x + .01f && point.y >= bounds.min.y - .01f && point.y <= bounds.max.y + .01f;
            }
            Check(inside, "Viewport inside Ground " + context + " camera=" + camera.transform.position + " aspect=" + camera.aspect.ToString("F4"));
        }

        private Color32[] Capture(Camera camera, string name, int width, int height, bool testCoverage)
        {
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            float aspect = camera.aspect; camera.aspect = (float)width / height;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
            RenderTexture.active = rt;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            Color32[] pixels = image.GetPixels32();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
            if (testCoverage)
            {
                // Saturated magenta is unaffected by linear/sRGB conversion. The control
                // render with no geometry proves this detector actually recognizes gaps.
                int gaps = pixels.Count(c => c.r >= 250 && c.g <= 5 && c.b >= 250);
                if (name == "coverage-control") Check(gaps == width * height, "Ground coverage detector recognizes empty render");
                else Check(gaps == 0, "No uncovered ground pixels " + name + " (" + gaps + ")");
            }
            Destroy(image); RenderTexture.active = previous; camera.aspect = aspect; RenderTexture.ReleaseTemporary(rt);
            return pixels;
        }

        private IEnumerator CheckRenderRegressions(Camera camera)
        {
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            yield return new WaitForSeconds(.3f);
            preview.SetPreviewInput(Vector2.zero);
            preview.Teleport(preview.MapBounds.center);
            yield return new WaitForEndOfFrame();
            var map = GameObject.Find("NordicStaticMap");
            var statics = map.GetComponentsInChildren<NordicSortAnchor>();
            int rootTies = statics.GroupBy(s => s.GetComponent<SortingGroup>().sortingOrder).Sum(g => g.Count() - 1);
            Check(rootTies == 0, "Static tall roots have unique sorting orders (ties=" + rootTies + ")");
            int childTies = 0;
            foreach (string layer in new[] { "LowDecorations", "TallDecorations", "Landmarks" })
            foreach (Transform composition in map.transform.Find(layer))
                childTies += composition.GetComponentsInChildren<Renderer>(true).GroupBy(r => r.sortingOrder).Sum(g => g.Count() - 1);
            Check(childTies == 0, "Composition parts have unique internal orders (ties=" + childTies + ")");
            var visual = preview.characterVisual.GetComponentsInChildren<SpriteRenderer>();
            Check(visual.Select(r => r.sortingOrder).Distinct().Count() == visual.Length, "Axeldor parts have explicit internal ordering");

            foreach (string name in new[] { "Composition_Grass_1 #", "Composition_Flowers_1 #", "Composition_Destructibles_1 #" })
            {
                Transform composition = map.GetComponentsInChildren<Transform>().First(t => t.name.StartsWith(name));
                var renderers = composition.GetComponentsInChildren<SpriteRenderer>();
                Bounds region = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) region.Encapsulate(renderer.bounds);
                CompareStaticOverlap(camera, region, name.Split(' ')[0]);
            }

            foreach (Vector2 direction in new[] { Vector2.left, Vector2.right })
            {
                preview.Teleport(preview.MapBounds.center);
                preview.SetPreviewInput(direction);
                yield return new WaitForSeconds(.2f);
                yield return new WaitForEndOfFrame();
                bool left = direction.x < 0;
                Check(left ? preview.characterVisual.localScale.x > 0 : preview.characterVisual.localScale.x < 0,
                    "Authored Axeldor facing " + (left ? "left" : "right"));
                Capture(camera, left ? "facing-left" : "facing-right", 1920, 1080, true);
                preview.SetPreviewInput(Vector2.zero);
            }

            int previousVsync = QualitySettings.vSyncCount, previousFps = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0;
            foreach (int fps in new[] { 60, 120 })
            {
                Application.targetFrameRate = fps;
                preview.Teleport((Vector2)preview.MapBounds.center + new Vector2(-6, 0));
                preview.SetPreviewInput(Vector2.right);
                yield return new WaitForSeconds(.65f);
                yield return new WaitForEndOfFrame();
                float previousX = preview.character.position.x;
                float previousScreenX = camera.WorldToScreenPoint(preview.character.transform.position).x;
                float maxSpeedError = 0, maxBackstep = 0;
                int backsteps = 0, stalledFrames = 0;
                var csv = new System.Text.StringBuilder("frame,deltaTime,actorX,cameraX,screenX,speed\n");
                // Stay within the unobstructed central area at both frame rates.
                for (int i = 0; i < fps; i++)
                {
                    yield return new WaitForEndOfFrame();
                    float x = preview.character.position.x;
                    float screenX = camera.WorldToScreenPoint(preview.character.transform.position).x;
                    float speed = (x - previousX) / Time.deltaTime;
                    maxSpeedError = Mathf.Max(maxSpeedError, Mathf.Abs(speed - preview.moveSpeed));
                    if (Mathf.Abs(x - previousX) < .00001f) stalledFrames++;
                    if (screenX < previousScreenX - .2f) backsteps++;
                    maxBackstep = Mathf.Max(maxBackstep, previousScreenX - screenX);
                    csv.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0},{1:F6},{2:F6},{3:F6},{4:F6},{5:F6}\n",
                        i, Time.deltaTime, x, camera.transform.position.x, screenX, speed);
                    previousX = x; previousScreenX = screenX;
                }
                preview.SetPreviewInput(Vector2.zero);
                File.WriteAllText(Path.Combine(output, "motion-" + fps + ".csv"), csv.ToString());
                Check(stalledFrames == 0 && maxSpeedError < .05f,
                    "Continuous per-frame movement at " + fps + " fps: stalled=" + stalledFrames + ", max speed error=" + maxSpeedError.ToString("F4"));
                Check(backsteps == 0, "No camera-relative backward jumps at " + fps + " fps: count=" + backsteps + ", max pixels=" + maxBackstep.ToString("F3"));
            }
            QualitySettings.vSyncCount = previousVsync;
            Application.targetFrameRate = previousFps;
        }

        private void CompareStaticOverlap(Camera camera, Bounds region, string label)
        {
            Vector3 original = camera.transform.position;
            float pixelSize = 2 * -original.z * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad) / 1080;
            // Translate by an exact whole number of pixels, then compare the same world
            // patch. This isolates order changes from subpixel texture sampling.
            const int shift = 512;
            camera.transform.position = new Vector3(region.center.x, region.center.y - shift * .5f * pixelSize, original.z);
            Vector3 bottom = Vector3.Scale(camera.WorldToViewportPoint(region.min), new Vector3(1920, 1080, 1));
            Vector3 top = Vector3.Scale(camera.WorldToViewportPoint(region.max), new Vector3(1920, 1080, 1));
            Color32[] before = Capture(camera, label + "-camera-below", 1920, 1080, false);
            camera.transform.position += new Vector3(0, shift * pixelSize, 0);
            Color32[] after = Capture(camera, label + "-camera-above", 1920, 1080, false);
            camera.transform.position = original;
            int changed = 0, compared = 0;
            for (int y = Mathf.Max(shift, Mathf.CeilToInt(bottom.y)); y <= Mathf.Min(1079, Mathf.FloorToInt(top.y)); y++)
            for (int x = Mathf.Max(0, Mathf.CeilToInt(bottom.x)); x <= Mathf.Min(1919, Mathf.FloorToInt(top.x)); x++)
            {
                Color32 a = before[y * 1920 + x], b = after[(y - shift) * 1920 + x];
                if (Mathf.Abs(a.r - b.r) > 6 || Mathf.Abs(a.g - b.g) > 6 || Mathf.Abs(a.b - b.b) > 6) changed++;
                compared++;
            }
            Check(compared > 100 && changed <= 4, "Stable rendered overlap " + label + ": changed=" + changed + "/" + compared);
        }
        private void OnDestroy() { if (active) Application.logMessageReceived -= OnLog; }
    }
}
