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
        public GameObject treePrefab, torchPrefab;
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
            Check(Vector2.Distance(bounds.size, new Vector2(119.3386f, 67.1280f)) < .001f, "Ground is four by four reference views");
            Check(QualitySettings.activeColorSpace == ColorSpace.Linear, "Linear color space");
            var map = GameObject.Find("NordicStaticMap");
            int spriteCount = map.GetComponentsInChildren<SpriteRenderer>(true).Length;
            int propCount = map.transform.Find("Environment").Cast<Transform>().Sum(t => t.childCount);
            Check(FindObjectsByType<Camera>(FindObjectsSortMode.None).Length == 1, "Single preview camera");
            Check(map.GetComponentsInChildren<NordicSortAnchor>(true).Length == 0, "No numbered Y sorting on baked map");
            Check(map.transform.Find("Boundaries").GetComponentsInChildren<BoxCollider2D>().Length == 4, "Four physical boundary walls");
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            Check(!behaviours.Any(b => b.GetType().Name is "NetworkManager" or "BootGameplayNetworkManager" or "NetworkIdentity" or "PlayerController_HMD" or "WorldManager"), "No active network session or formal player");
            foreach (var renderer in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
                if (renderer.sprite == null || renderer.sharedMaterial == null || renderer.sharedMaterial.shader.name.Contains("Error")) errors.Add("Invalid sprite/material: " + renderer.name);
            Check(Mathf.Abs(preview.characterVisual.localScale.y - 1) < .0001f, "Axeldor source wrapper scale 1");
            Vector3[] mesh = preview.characterVisual.GetComponentsInChildren<SpriteRenderer>().Where(r => r.name != "Shadow")
                .SelectMany(r => r.sprite.vertices.Select(v => preview.characterVisual.InverseTransformPoint(r.transform.TransformPoint(v)))).ToArray();
            Check(mesh.Max(p => p.y) - mesh.Min(p => p.y) > 2 && mesh.Max(p => p.y) - mesh.Min(p => p.y) < 2.4f, "Axeldor original assembled size");
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

            var extreme = new RenderTexture(8192, 512, 0);
            camera.targetTexture = extreme;
            preview.ConstrainCameraView();
            Check(camera.rect.width < 1 && camera.aspect * 16.781992f <= bounds.size.x && bounds.size.x - camera.aspect * 16.781992f < .1f,
                "Extreme aspect uses bounded viewport without zoom");
            CheckCamera(camera, bounds, "extreme offscreen aspect");
            camera.targetTexture = null; Destroy(extreme);
            preview.ConstrainCameraView();

            Screen.SetResolution(1920, 180, FullScreenMode.Windowed);
            yield return new WaitForSeconds(.5f);
            preview.ConstrainCameraView();
            Check(camera.rect.width < 1, "Extreme window uses pillarbox viewport");
            CheckCamera(camera, bounds, "extreme window");
            yield return new WaitForEndOfFrame();
            Capture(camera, "extreme-active-viewport", Mathf.FloorToInt(camera.pixelWidth), 180, true);
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            yield return new WaitForSeconds(.5f);
            preview.ConstrainCameraView();

            // Cover all sixteen reference-screen regions, including chunk seams.
            for (int x = 0; x < 4; x++) for (int y = 0; y < 4; y++)
            {
                preview.Teleport(new Vector2(bounds.min.x + (x + .5f) * bounds.size.x / 4,
                    bounds.min.y + (y + .5f) * bounds.size.y / 4));
                yield return new WaitForEndOfFrame();
                Capture(camera, "region-" + x + "-" + y, 1920, 1080, true);
            }
            foreach (Vector2 direction in new[] { Vector2.left, Vector2.right, Vector2.up, Vector2.down, Vector2.one, -Vector2.one, new Vector2(-1,1), new Vector2(1,-1) })
            {
                preview.Teleport((Vector2)bounds.center + Vector2.Scale(direction, (Vector2)bounds.extents - Vector2.one));
                preview.Move(direction * 500);
                var foot = preview.character.GetComponent<Collider2D>().bounds;
                Check(bounds.Contains(foot.min) && bounds.Contains(foot.max), "Large-step boundary block " + direction);
                preview.Teleport((Vector2)bounds.center + direction * 1000);
                foot = preview.character.GetComponent<Collider2D>().bounds;
                Check(bounds.Contains(foot.min) && bounds.Contains(foot.max), "Teleport clamped " + direction);
            }
            // Temporary fixture isolates occlusion and collision from random layout contents.
            var environment = map.transform.Find("Environment").gameObject;
            environment.SetActive(false);
            var tree = Instantiate(treePrefab, Vector3.zero, Quaternion.identity);
            var actorCollider = preview.character.GetComponent<Collider2D>();
            var rootCollider = tree.GetComponent<Collider2D>();
            preview.Teleport(new Vector2(0, 1.2f));
            yield return new WaitForSeconds(.25f);
            yield return new WaitForEndOfFrame();
            Capture(camera, "tree-behind", 1920, 1080, true);
            Check(preview.character.position.y > tree.transform.position.y && preview.character.GetComponent<SortingGroup>().sortingOrder == 0,
                "Actor uses shared order and Y axis behind tree");
            preview.Teleport(new Vector2(0, -.65f));
            yield return new WaitForSeconds(.25f);
            yield return new WaitForEndOfFrame();
            Capture(camera, "tree-front", 1920, 1080, true);
            Check(preview.character.position.y < tree.transform.position.y, "Actor in front of tree by Y");
            preview.Teleport(new Vector2(0, -1));
            preview.Move(new Vector2(0, 5));
            Check(preview.character.position.y < .2f && !actorCollider.Distance(rootCollider).isOverlapped, "Tree root blocks swept movement");
            preview.Teleport(new Vector2(0, 1.5f));
            Check(!actorCollider.Distance(rootCollider).isOverlapped, "Canopy has no solid collider");
            Destroy(tree);
            yield return null;
            Vector2 origin = new Vector2(-4, 0);
            preview.Teleport(origin); preview.Move(Vector2.right);
            float straight = Vector2.Distance(origin, preview.character.position);
            preview.Teleport(origin); preview.Move(Vector2.one.normalized);
            float diagonal = Vector2.Distance(origin, preview.character.position);
            Check(Mathf.Abs(straight - 1) < .001f && Mathf.Abs(straight - diagonal) < .001f, "Equal axial and normalized diagonal displacement");
            preview.SetPreviewInput(Vector2.right);
            yield return new WaitForSeconds(.25f);
            Check(preview.characterAnimator.GetCurrentAnimatorStateInfo(0).IsName("Walk"), "Nordic walking animation");
            preview.SetPreviewInput(Vector2.zero);
            yield return new WaitForSeconds(.25f);
            Check(preview.characterAnimator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Nordic idle animation");
            var torch = Instantiate(torchPrefab, new Vector3(4, 1, 0), Quaternion.identity);
            var ambient = torch.GetComponent<NordicTorchAmbient>();
            float intensity = ambient.lights[0].intensity;
            yield return new WaitForSeconds(.17f);
            Check(Mathf.Abs(intensity - ambient.lights[0].intensity) > .00001f, "Torch light animates independently");
            Check(torch.GetComponentsInChildren<ParticleSystem>().All(p => p.isPlaying && p.main.loop), "Torch particles loop independently");
            preview.Teleport(new Vector2(3, 0));
            yield return new WaitForEndOfFrame(); Capture(camera, "torch-fixture", 1920, 1080, true);
            Destroy(torch); yield return null;
            environment.SetActive(true);
            Physics2D.SyncTransforms();
            Check(spriteCount == map.GetComponentsInChildren<SpriteRenderer>(true).Length, "Baked instance count unchanged after fixture tests");
            var torches = map.GetComponentsInChildren<NordicTorchAmbient>();
            var particles = map.GetComponentsInChildren<ParticleSystem>();
            yield return CheckRenderRegressions(camera);

            preview.Teleport(preview.SpawnPosition);
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
            float aspect = camera.aspect;
            Rect viewport = camera.rect;
            camera.rect = new Rect(0, 0, 1, 1); // Capture the active view without applying screen letterboxing twice.
            camera.aspect = (float)width / height;
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
            Destroy(image); RenderTexture.active = previous; camera.rect = viewport; camera.aspect = aspect; RenderTexture.ReleaseTemporary(rt);
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
            Check(map.GetComponentsInChildren<NordicSortAnchor>().Length == 0, "Custom-axis sorting replaces numbered ties");
            var visual = preview.characterVisual.GetComponentsInChildren<SpriteRenderer>();
            Check(visual.Select(r => r.sortingOrder).Distinct().Count() == visual.Length, "Axeldor parts have explicit internal ordering");
            foreach (string name in new[] { "Composition_Grass_1 #", "Composition_Flowers_1 #", "Composition_Destructibles_1 #" })
            {
                Transform composition = map.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.StartsWith(name));
                if (composition == null) continue;
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
                preview.Teleport(FindClearLane(14));
                preview.SetPreviewInput(Vector2.right);
                yield return new WaitForSeconds(.65f);
                yield return new WaitForEndOfFrame();
                float previousX = preview.character.position.x;
                float previousScreenX = camera.WorldToScreenPoint(preview.character.transform.position).x;
                float maxSpeedError = 0, maxBackstep = 0;
                int backsteps = 0, stalledFrames = 0;
                var csv = new System.Text.StringBuilder("frame,deltaTime,actorX,cameraX,screenX,speed\n");
                // Select a verified clear lane; the original rules reserve no central clearing.
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

        private Vector2 FindClearLane(float length)
        {
            Bounds b = preview.MapBounds;
            for (int y = -12; y <= 12; y++) for (int x = -15; x <= 5; x++)
            {
                Vector2 p = (Vector2)b.center + new Vector2(x, y);
                bool clear = true;
                for (float d = 0; d <= length; d += .2f) if (!preview.IsFree(p + Vector2.right * d)) { clear = false; break; }
                if (clear) return p;
            }
            throw new InvalidOperationException("No collision-free movement test lane found.");
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
