using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Com.LuisPedroFonseca.ProCamera2D;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.NordicSample.Editor
{
    public static class NordicStaticSampleBuilder
    {
        public const string Root = "Assets/_Project/Content/Nordic";
        public const string ScenePath = "Assets/_Project/Scenes/NordicStaticSample.unity";
        private const string Artifacts = "Logs/NordicStaticSample";
        private static readonly Vector2 Center = new Vector2(0, .99f);
        private static Material Lit => Load<Material>(Root + "/Materials/NordicLit.mat");
        private static Material Unlit => Load<Material>(Root + "/Materials/NordicUnlit.mat");

        [Serializable] public sealed class Manifest { public string stage; public Prop[] prefabs; public Prop character; public SpriteInfo[] sprites; }
        [Serializable] public sealed class Prop { public string path, source; public int group, sourceSpriteCount, transformCount; public Lamp[] lights; }
        [Serializable] public sealed class Lamp { public string node; public float r, g, b, intensity, inner, outer, falloff; }
        [Serializable] public sealed class SpriteInfo { public string path, source; public float ppu, width, height, pivotX, pivotY; public int vertexCount; }
        private static Manifest Catalog => JsonUtility.FromJson<Manifest>(File.ReadAllText(Root + "/ImportManifest.json"));
        private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing " + typeof(T).Name + ": " + path);

        [MenuItem("Tools/Nordic/1. Validate Imported Resources")]
        public static void ValidateImports()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Manifest manifest = Catalog;
            Require(PlayerSettings.colorSpace == ColorSpace.Linear, "The sample requires the existing Linear project setting.");
            foreach (var info in manifest.sprites)
            {
                Sprite sprite = Load<Sprite>(info.path);
                Require(sprite.texture != null, "Missing texture: " + info.path);
                Require(Mathf.Abs(sprite.pixelsPerUnit - info.ppu) < .001f, "PPU mismatch: " + info.path);
                Require(sprite.vertices.Length == info.vertexCount, "Sprite mesh changed: " + info.path);
                Require(Mathf.Abs(sprite.rect.width - info.width) < .01f && Mathf.Abs(sprite.rect.height - info.height) < .01f, "Sprite rect mismatch: " + info.path);
                Require(Vector2.Distance(sprite.pivot, new Vector2(info.pivotX * info.width, info.pivotY * info.height)) < .02f, "Pivot mismatch: " + info.path);
            }
            foreach (var prop in manifest.prefabs.Concat(manifest.character == null ? Array.Empty<Prop>() : new[] { manifest.character }))
            {
                GameObject prefab = Load<GameObject>(prop.path);
                Require(prefab.GetComponentsInChildren<SpriteRenderer>(true).Length == prop.sourceSpriteCount, "Lost SpriteRenderer: " + prop.path);
                Require(prefab.GetComponentsInChildren<Transform>(true).Length == prop.transformCount, "Lost local composition: " + prop.path);
                foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0, "Missing script: " + prop.path);
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                    Require(r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name.StartsWith("Universal Render Pipeline/"), "Unsupported material: " + prop.path);
                foreach (string dependency in AssetDatabase.GetDependencies(prop.path, true))
                    Require(!dependency.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), "DLL leaked into art dependencies: " + dependency);
            }
            Directory.CreateDirectory(Artifacts);
            File.WriteAllText(Artifacts + "/import-validation.json", JsonUtility.ToJson(new ImportResult { stage = manifest.stage, prefabs = manifest.prefabs.Length, sprites = manifest.sprites.Length, passed = true }, true));
            Debug.Log("[Nordic] Imported resources PASS: " + manifest.prefabs.Length + " prefabs, " + manifest.sprites.Length + " sprites.");
        }
        [Serializable] private sealed class ImportResult { public string stage; public int prefabs, sprites; public bool passed; }

        public static void SmokeCheck()
        {
            ValidateImports();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var tiles = new GameObject("3x3 Stitch Test").transform;
            Sprite snow = Load<Sprite>(Root + "/Sprites/fondo base_V3.asset");
            for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
                SpriteNode("Snow " + x + "," + y, snow, tiles, new Vector3(x * 5.8f, y * 5.8f, 0), "BackgroundBack", (y + 1) * 3 + x + 1);
            Prop tree = Catalog.prefabs.First(p => p.group == 1);
            InstantiateProp(tree, new Vector2(-3, 0), new GameObject("Trees").transform, 0);
            InstantiateProp(Catalog.prefabs.First(p => p.group == 3), new Vector2(2, 0), new GameObject("Grass").transform, 0);
            Camera camera = MakeCamera(new Vector2(0, 0));
            camera.GetComponent<ProCamera2D>().enabled = false;
            MakeGlobalLight();
            Capture(camera, Artifacts + "/smoke-3x3.png", 1920, 1080);
            Debug.Log("[Nordic] Smoke image: " + Artifacts + "/smoke-3x3.png");
        }

        [MenuItem("Tools/Nordic/2. Build Static Sample")]
        public static void BuildSample()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ValidateImports();
            Manifest manifest = Catalog;
            Require(manifest.stage == "full" && manifest.prefabs.Length == 35, "Run the full visual import before building the sample.");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var map = new GameObject("NordicStaticMap");
            SpriteRenderer ground = CopyGround(scene, map.transform);
            Transform baseGround = Child(map.transform, "BaseGround");
            Transform patches = Child(map.transform, "GroundPatches");
            Transform low = Child(map.transform, "LowDecorations");
            Transform tall = Child(map.transform, "TallDecorations");
            Transform landmarks = Child(map.transform, "Landmarks");
            BuildFloor(baseGround, patches);
            BuildDecorations(manifest, low, tall, landmarks);
            ApplyMapSorting(map);
            PrefabUtility.SaveAsPrefabAsset(map, Root + "/NordicStaticMap.prefab");

            Camera camera = MakeCamera(Center);
            MakeGlobalLight();
            NordicSamplePreview preview = new GameObject("SamplePreview").AddComponent<NordicSamplePreview>();
            preview.ground = ground;
            preview.cameraRig = camera.GetComponent<ProCamera2D>();
            BuildCharacter(preview);
            preview.gameObject.AddComponent<NordicSampleValidation>();
            preview.ApplyGroundBounds();
            preview.cameraRig.AddCameraTarget(preview.character.transform);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            ValidateScene();
            Capture(camera, Artifacts + "/sample-center-editor.png", 1920, 1080);
            camera.transform.position = new Vector3(0, Center.y, -26);
            Capture(camera, Artifacts + "/layout-overview-editor.png", 1920, 1200);
            camera.transform.position = new Vector3(0, Center.y, -10);
            // Overview is a diagnostic image; the saved scene retains FOV 80 / distance 10.
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[Nordic] Static sample created: " + ScenePath);
        }

        private static SpriteRenderer CopyGround(Scene destination, Transform map)
        {
            Scene gameplay = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Gameplay.unity", OpenSceneMode.Additive);
            SpriteRenderer source = gameplay.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SpriteRenderer>(true)).Single(r => r.name == "Ground");
            GameObject clone = Object.Instantiate(source.gameObject);
            clone.name = "Ground";
            clone.transform.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(clone, destination);
            clone.transform.SetParent(map, true);
            var ground = clone.GetComponent<SpriteRenderer>();
            ground.transform.position = source.transform.position;
            ground.transform.rotation = Quaternion.identity;
            Vector2 size = ground.sprite.bounds.size;
            ground.transform.localScale = new Vector3(64f / size.x, 40f / size.y, 1);
            ground.color = Color.clear;
            ground.sharedMaterial = Unlit;
            foreach (var collider in clone.GetComponents<Collider2D>()) Object.DestroyImmediate(collider);
            EditorSceneManager.CloseScene(gameplay, true);
            SceneManager.SetActiveScene(destination);
            return ground;
        }

        private static void BuildFloor(Transform baseGround, Transform patches)
        {
            Sprite snow = Load<Sprite>(Root + "/Sprites/fondo base_V3.asset");
            Sprite mud = Load<Sprite>(Root + "/Sprites/terreno dibujado7.asset");
            Sprite gravel = Load<Sprite>(Root + "/Sprites/terreno dibujado_rocas2.asset");
            int order = 0;
            for (int y = -5; y <= 5; y++) for (int x = -7; x <= 7; x++)
            {
                var r = SpriteNode("Snow " + x + "," + y, snow, baseGround, new Vector3(x * 5.8f, Center.y + y * 5.8f, 0), "BackgroundBack", order++);
                r.flipX = ((x + y) & 1) == 0;
                r.flipY = (y & 1) == 0;
            }
            Vector2[] points = { new Vector2(-25,-13), new Vector2(-14,-10), new Vector2(-6,-6), new Vector2(5,-6), new Vector2(15,-4), new Vector2(25,0),
                new Vector2(-27,6), new Vector2(-16,9), new Vector2(-4,8), new Vector2(8,10), new Vector2(18,12), new Vector2(26,15),
                new Vector2(-22,16), new Vector2(1,-15), new Vector2(23,-14), new Vector2(-13,1), new Vector2(13,3), new Vector2(2,1) };
            for (int i = 0; i < points.Length; i++)
            {
                var r = SpriteNode("Patch " + i, i % 3 == 0 ? gravel : mud, patches, (Vector3)(points[i] + Center), "Background", i);
                r.flipX = i % 2 == 0; r.flipY = i % 4 == 0;
                r.color = i % 2 == 0 ? new Color(1, .9340802f, .86477983f, 1) : Color.white;
            }
        }

        private static void BuildDecorations(Manifest catalog, Transform low, Transform tall, Transform landmarks)
        {
            int variant = 0;
            foreach (Prop prop in catalog.prefabs)
            {
                int sameGroup = catalog.prefabs.Take(variant).Count(p => p.group == prop.group);
                Vector2 position;
                switch (prop.group)
                {
                    case 1: position = new Vector2(-10, 1); break;
                    case 2: position = new Vector2(-24 + sameGroup * 3.6f, -10 - (sameGroup % 2) * 2); break;
                    case 3: position = new Vector2(-18 + (sameGroup % 5) * 4, -6 - (sameGroup / 5) * 3); break;
                    case 4: position = new Vector2(11, -2); break;
                    case 5: position = new Vector2(-15 + sameGroup * 4.5f, 7 + (sameGroup % 2) * 2); break;
                    case 6: position = new Vector2(10 + (sameGroup % 3) * 5, 11 + (sameGroup / 3) * 5); break;
                    case 7: position = new Vector2(14, 10); break;
                    case 8: position = new Vector2(12 + (sameGroup % 3) * 5, -10 - (sameGroup / 3) * 4); break;
                    default: position = new Vector2(-23, 13); break;
                }
                InstantiateProp(prop, position + Center, prop.group is 3 or 5 ? low : prop.group is 6 or 7 ? landmarks : tall, variant++);
            }
            // Deliberate fixed clusters leave the central 16x10 region navigable.
            Vector2[] trees = { new Vector2(-26,-16),new Vector2(-22,-14),new Vector2(-28,-4),new Vector2(-24,0),new Vector2(-18,3),new Vector2(-23,9),
                new Vector2(-27,15),new Vector2(-17,15),new Vector2(-11,14),new Vector2(11,-16),new Vector2(19,-16),new Vector2(27,-13),
                new Vector2(25,-7),new Vector2(28,3),new Vector2(24,7),new Vector2(28,15),new Vector2(3,16),new Vector2(9,16) };
            Prop[] treeTypes = catalog.prefabs.Where(p => p.group is 1 or 4).ToArray();
            for (int i = 0; i < trees.Length; i++) InstantiateProp(treeTypes[i % 2], trees[i] + Center, tall, variant++);
            Prop[] grasses = catalog.prefabs.Where(p => p.group is 3 or 5).ToArray();
            var rng = new System.Random(12092026);
            for (int i = 0; i < 145; i++)
            {
                Vector2 point = new Vector2((float)rng.NextDouble() * 59 - 29.5f, (float)rng.NextDouble() * 35 - 17.5f);
                if (Mathf.Abs(point.x) < 8 && Mathf.Abs(point.y) < 5) continue;
                InstantiateProp(grasses[i % grasses.Length], point + Center, low, variant++);
            }
            InstantiateProp(catalog.prefabs.First(p => p.group == 7), Center + new Vector2(22, 13), landmarks, variant);
        }

        private static GameObject InstantiateProp(Prop prop, Vector2 point, Transform parent, int index)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(prop.path), parent);
            instance.name = Path.GetFileNameWithoutExtension(prop.path) + " #" + index;
            instance.transform.position = new Vector3(point.x, point.y, 0);
            bool low = prop.group is 3 or 5;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sortingLayerName = low ? "BackgroundFront" : "Props";
                if (low) renderer.sortingOrder += index * 16;
            }
            if (!low)
            {
                instance.AddComponent<SortingGroup>().sortingLayerName = "Props";
                instance.AddComponent<NordicSortAnchor>().RefreshOrder();
            }
            if (prop.lights.Length > 0) ConfigureTorch(instance, prop);
            return instance;
        }

        private static void ApplyMapSorting(GameObject map)
        {
            // URP 2D chooses perspective distance for equal orders. Give both the
            // composition and its parts a complete order that does not use the camera.
            Transform low = map.transform.Find("LowDecorations");
            var lowCompositions = low.Cast<Transform>().OrderByDescending(t => t.position.y).ThenBy(t => t.GetSiblingIndex()).ToArray();
            for (int i = 0; i < lowCompositions.Length; i++)
            {
                GameObject composition = lowCompositions[i].gameObject;
                var group = composition.GetComponent<SortingGroup>();
                if (group == null) group = composition.AddComponent<SortingGroup>();
                group.sortingLayerName = "BackgroundFront";
                group.sortingOrder = i;
                SetCompositionOrders(composition, false);
            }
            foreach (var row in map.GetComponentsInChildren<NordicSortAnchor>().GroupBy(a => a.FootRow))
            {
                Require(row.Count() < NordicSortAnchor.OrderStride, "Too many tall props share foot row " + row.Key + "; spread their root anchors before baking.");
                int slot = 1;
                foreach (var anchor in row)
                {
                    anchor.stableTieBreak = slot++;
                    anchor.RefreshOrder();
                    Require(Mathf.Abs(anchor.Order) <= 32767, "Sorting order exceeds Unity's range: " + anchor.name);
                    SetCompositionOrders(anchor.gameObject, false);
                }
            }
        }

        private static void SetCompositionOrders(GameObject composition, bool character)
        {
            Renderer[] renderers = composition.GetComponentsInChildren<Renderer>(true);
            // Character hierarchy is authored back-to-front: shadow, legs, back hand,
            // body, head, front hand. Scenery ties use fixed local ground position.
            var ordered = character ? renderers : renderers.OrderBy(r => r.sortingOrder).ThenByDescending(r => r.transform.position.y).ToArray();
            for (int i = 0; i < ordered.Length; i++) ordered[i].sortingOrder = i;
        }

        [MenuItem("Tools/Nordic/5. Refresh Sample Sorting (Keep Layout)")]
        public static void RefreshSampleSorting()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (SceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
            var preview = Object.FindFirstObjectByType<NordicSamplePreview>();
            GameObject map = SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == "NordicStaticMap");
            ApplyMapSorting(map);
            SetCompositionOrders(preview.characterVisual.gameObject, true);
            preview.character.GetComponent<NordicSortAnchor>().RefreshOrder();
            PrefabUtility.SaveAsPrefabAsset(map, Root + "/NordicStaticMap.prefab");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            ValidateScene();
            Camera camera = preview.cameraRig.GetComponent<Camera>();
            Capture(camera, Artifacts + "/sample-center-editor.png", 1920, 1080);
            Vector3 original = camera.transform.position;
            camera.transform.position = new Vector3(0, Center.y, -26);
            Capture(camera, Artifacts + "/layout-overview-editor.png", 1920, 1200);
            camera.transform.position = original;
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
        }

        public static void RefreshSortingAndBuildPlayer() { RefreshSampleSorting(); BuildPlayer(); }

        private static void ConfigureTorch(GameObject instance, Prop prop)
        {
            var lights = new List<Light2D>();
            foreach (Lamp settings in prop.lights)
            {
                Transform node = instance.GetComponentsInChildren<Transform>(true).First(t => t.name == settings.node && t.GetComponent<Light2D>() == null);
                var light = node.gameObject.AddComponent<Light2D>();
                light.lightType = Light2D.LightType.Point;
                light.color = new Color(settings.r, settings.g, settings.b, 1);
                light.intensity = settings.intensity;
                light.pointLightInnerRadius = settings.inner;
                light.pointLightOuterRadius = settings.outer;
                light.falloffIntensity = settings.falloff;
                SetLightLayers(light, settings.node.Contains("Background") ? new[] { "BackgroundBack", "Background", "BackgroundFront" } : new[] { "Props" });
                lights.Add(light);
            }
            var ambient = instance.AddComponent<NordicTorchAmbient>();
            ambient.lights = lights.ToArray();
            ambient.flame = instance.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Flame");
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = true; main.playOnAwake = true; main.stopAction = ParticleSystemStopAction.None;
                ps.useAutoRandomSeed = false; ps.randomSeed = 917;
            }
        }

        private static Camera MakeCamera(Vector2 center)
        {
            Camera camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(center.x, center.y, -10);
            camera.transform.rotation = Quaternion.identity;
            camera.orthographic = false; camera.fieldOfView = 80;
            camera.nearClipPlane = .3f; camera.farClipPlane = 100;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.magenta; // A clear, measurable signal for missing ground coverage.
            camera.allowHDR = true;
            camera.gameObject.AddComponent<AudioListener>();
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            var rig = camera.gameObject.AddComponent<ProCamera2D>();
            rig.Axis = MovementAxis.XY; rig.UpdateType = UpdateType.LateUpdate;
            rig.CenterTargetOnStart = true;
            rig.HorizontalFollowSmoothness = rig.VerticalFollowSmoothness = .15f;
            rig.OffsetX = rig.OffsetY = 0;
            var limits = camera.gameObject.AddComponent<ProCamera2DNumericBoundaries>();
            limits.UseSoftBoundaries = false;
            return camera;
        }

        private static void MakeGlobalLight()
        {
            var light = new GameObject("Global Light 2D").AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.color = Color.white; light.intensity = .9f;
            SetLightLayers(light, new[] { "BackgroundBack", "Background", "BackgroundFront", "Props" });
        }

        private static void SetLightLayers(Light2D light, string[] names)
        {
            var serialized = new SerializedObject(light);
            SerializedProperty layers = serialized.FindProperty("m_ApplyToSortingLayers");
            layers.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++) layers.GetArrayElementAtIndex(i).intValue = SortingLayer.NameToID(names[i]);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildCharacter(NordicSamplePreview preview)
        {
            Require(Catalog.character != null, "Run the full import to include the Nordic Axeldor visual.");
            GameObject root = new GameObject("Preview Character - Axeldor");
            root.transform.position = Center;
            root.layer = LayerMask.NameToLayer("Player");
            var body = root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0; body.constraints = RigidbodyConstraints2D.FreezeRotation;
            var foot = root.AddComponent<CircleCollider2D>(); foot.radius = .13f;
            root.AddComponent<SortingGroup>().sortingLayerName = "Props";
            var sort = root.AddComponent<NordicSortAnchor>(); sort.moving = true; sort.RefreshOrder();
            Transform visual = Child(root.transform, "Nordic Visual");
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(Catalog.character.path), visual);
            foreach (var renderer in rig.GetComponentsInChildren<SpriteRenderer>(true))
            {
                renderer.sortingLayerName = "Props";
                if (renderer.name == "Shadow") renderer.sortingOrder = -100;
            }
            SetCompositionOrders(rig, true);
            AnimationClip idle = Load<AnimationClip>(Root + "/Animations/Viking_Idle_Player.anim");
            AnimationClip walk = Load<AnimationClip>(Root + "/Animations/Viking_Walk_Player.anim");
            Require(AnimationUtility.GetAnimationEvents(idle).Length == 0 && AnimationUtility.GetAnimationEvents(walk).Length == 0, "Original character callbacks must be stripped.");
            var animator = rig.AddComponent<Animator>();
            animator.runtimeAnimatorController = BuildCharacterController(idle, walk);
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            idle.SampleAnimation(rig, 0);
            AlignPreviewVisualFeet(visual);
            preview.character = body;
            preview.characterVisual = visual;
            preview.characterAnimator = animator;
        }

        private static void AlignPreviewVisualFeet(Transform visual)
        {
            // Measure the original tight sprite meshes in the assembled idle pose.
            // Preserve source scale; the wrapper only aligns feet and controls facing.
            var points = visual.GetComponentsInChildren<SpriteRenderer>()
                .Where(r => r.enabled && r.name != "Shadow")
                .SelectMany(r => r.sprite.vertices.Select(v => visual.InverseTransformPoint(r.transform.TransformPoint(v))))
                .ToArray();
            Require(points.Length > 0, "Axeldor visual has no visible sprite mesh.");
            float bottom = points.Min(p => p.y), top = points.Max(p => p.y);
            Require(top > bottom, "Axeldor visual has invalid bounds.");
            visual.localScale = Vector3.one;
            visual.localPosition = new Vector3(0, -bottom, 0);
            Debug.Log("[Nordic] Axeldor source-scale idle height: " + (top - bottom) + "; preview visual scale: 1.");
        }

        private static AnimatorController BuildCharacterController(AnimationClip idle, AnimationClip walk)
        {
            string path = Root + "/Animations/AxeldorPreview.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path) ?? AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.parameters = new[] { new AnimatorControllerParameter { name = "Walking", type = AnimatorControllerParameterType.Bool } };
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState idleState = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Idle") ?? machine.AddState("Idle");
            AnimatorState walkState = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Walk") ?? machine.AddState("Walk");
            idleState.motion = idle; walkState.motion = walk; machine.defaultState = idleState;
            foreach (var state in new[] { idleState, walkState })
                foreach (var transition in state.transitions) state.RemoveTransition(transition);
            var toWalk = idleState.AddTransition(walkState);
            toWalk.hasExitTime = false; toWalk.hasFixedDuration = true; toWalk.duration = .1f;
            toWalk.AddCondition(AnimatorConditionMode.If, 0, "Walking");
            var toIdle = walkState.AddTransition(idleState);
            toIdle.hasExitTime = false; toIdle.hasFixedDuration = true; toIdle.duration = .1f;
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "Walking");
            EditorUtility.SetDirty(controller);
            return controller;
        }

        [MenuItem("Tools/Nordic/3. Validate Static Sample")]
        public static void ValidateScene()
        {
            if (SceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
            var preview = Object.FindFirstObjectByType<NordicSamplePreview>();
            Require(preview != null, "Preview component missing.");
            Require(Vector2.Distance(preview.ground.bounds.size, new Vector2(64, 40)) < .001f, "Ground must be 64x40.");
            Camera camera = preview.cameraRig.GetComponent<Camera>();
            Require(!camera.orthographic && Mathf.Abs(camera.fieldOfView - 80) < .001f && Mathf.Abs(camera.transform.position.z + 10) < .001f, "Camera baseline mismatch.");
            Require(Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length == 1, "Only one sample camera is allowed.");
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0, "Missing script: " + t.name);
                foreach (var component in t.GetComponents<MonoBehaviour>())
                    Require(component.GetType().Namespace != null && (component.GetType().Namespace.StartsWith("MonsterSupergroup.NordicSample") || component.GetType().Namespace.StartsWith("UnityEngine.Rendering") || component.GetType().Namespace.StartsWith("Com.LuisPedroFonseca")), "Unexpected runtime component: " + component.GetType());
                foreach (var r in t.GetComponents<SpriteRenderer>())
                    Require(r.sprite != null && r.sharedMaterial != null, "Incomplete SpriteRenderer: " + t.name);
            }
            string[] dependencies = AssetDatabase.GetDependencies(ScenePath, true);
            foreach (Prop prop in Catalog.prefabs)
                Require(dependencies.Contains(prop.path), "Decoration missing from sample: " + prop.path);
            Require(preview.characterAnimator != null && preview.characterVisual != null, "Axeldor preview bindings missing.");
            Require(dependencies.Contains(Catalog.character.path), "Nordic character prefab missing from sample.");
            foreach (var renderer in preview.characterVisual.GetComponentsInChildren<SpriteRenderer>(true))
                Require(AssetDatabase.GetAssetPath(renderer.sprite).StartsWith(Root + "/Sprites/"), "Non-Nordic character art: " + renderer.name);
            foreach (string path in dependencies)
            {
                Require(!path.Contains("Assembly-CSharp.dll") && !path.Contains("Assets/Plugins/Unity."), "Exported runtime DLL dependency: " + path);
                Require(!path.Contains("NetworkPlayer.prefab") && !path.Contains("EvilWizard"), "Target player art leaked into sample: " + path);
            }
            Directory.CreateDirectory(Artifacts);
            File.WriteAllText(Artifacts + "/scene-validation.txt", "PASS\n64x40; perspective 80; camera Z=-10; clean runtime components; visual dependencies resolved.\n");
            Debug.Log("[Nordic] Static sample validation PASS.");
        }

        [MenuItem("Tools/Nordic/4. Build Standalone Preview")]
        public static void BuildPlayer()
        {
            ValidateScene();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, target = BuildTarget.StandaloneWindows64,
                locationPathName = "Builds/NordicStaticSample/NordicStaticSample.exe",
                options = BuildOptions.Development
            });
            Require(report.summary.result == BuildResult.Succeeded, "Standalone sample build failed: " + report.summary.result);
            Debug.Log("[Nordic] Standalone preview build PASS.");
        }

        public static void BuildAndValidatePlayer() { BuildSample(); BuildPlayer(); }

        private static SpriteRenderer SpriteNode(string name, Sprite sprite, Transform parent, Vector3 position, string layer, int order)
        {
            var r = new GameObject(name).AddComponent<SpriteRenderer>();
            r.transform.SetParent(parent, false); r.transform.position = position;
            r.sprite = sprite; r.sharedMaterial = Lit;
            r.sortingLayerName = layer; r.sortingOrder = order;
            r.spriteSortPoint = SpriteSortPoint.Pivot;
            return r;
        }
        private static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); return t;
        }
        public static void Capture(Camera camera, string path, int width, int height)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            float aspect = camera.aspect;
            camera.aspect = (float)width / height;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.DestroyImmediate(image);
            RenderTexture.active = previous;
            camera.aspect = aspect;
            RenderTexture.ReleaseTemporary(target);
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
