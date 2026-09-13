using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Animancer;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.CameraFX;
using AstralShift.HellMaiden.Player;
using Com.LuisPedroFonseca.ProCamera2D;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using Pathfinding;
using Pathfinding.Serialization;
using Path = System.IO.Path;
using Pathfinding.Graphs.Grid;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class NordicGameplayMigration
    {
        public const string Content = "Assets/_Project/Content/Nordic";
        public const string ScenePath = "Assets/_Project/Scenes/Gameplay.unity";
        public const string MapPath = Content + "/NordicGameplayMap.prefab";
        public const string PlayerPath = "Assets/_Project/Content/NetworkCombat/NetworkPlayer.prefab";
        public const string NavPath = Content + "/GameplayNavigation.bytes";
        public const string BaselinePath = Content + "/GameplayPlayerBaseline.json";
        private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Required asset missing: " + path);
        private static void Set(Object obj, string property, Object value)
        { var so = new SerializedObject(obj); so.FindProperty(property).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }

        public static void Run()
        {
            MonsterSupergroup.EditorTools.ProjectToolRunner.CheckLegacyMaintenance("migrate.nordic-gameplay", "MonsterSupergroup.NetworkCombat.Editor.NordicGameplayMigration.Run");
            // Initial backups include pre-existing uncommitted work. Re-runs never overwrite that baseline.
            string folder = "Logs/NordicGameplay/MigrationBaseline"; Directory.CreateDirectory(folder);
            foreach (string path in new[] { ScenePath, PlayerPath })
                if (!File.Exists(folder + "/" + Path.GetFileName(path))) File.Copy(path, folder + "/" + Path.GetFileName(path));
            SavePlayerBaseline(folder + "/NetworkPlayer.prefab");
            BuildPlayer();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var oldMap = Object.FindFirstObjectByType<GameplayMapContext>();
            if (oldMap != null) Object.DestroyImmediate(oldMap.gameObject);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                    if (renderer.name == "Ground") Object.DestroyImmediate(renderer.gameObject);
            var map = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(Content + "/NordicStaticMap.prefab"), scene);
            map.name = "NordicGameplayMap";
            var ground = map.GetComponentsInChildren<SpriteRenderer>().Single(r => r.name == "Ground");
            foreach (var wall in map.GetComponentsInChildren<BoxCollider2D>(true).Where(c => c.name.StartsWith("Boundary ")))
            {
                wall.gameObject.layer = LayerMask.NameToLayer("Edges");
                wall.includeLayers = LayerMask.GetMask("Player", "EnemyCollision"); wall.layerOverridePriority = 10;
            }
            var navObject = new GameObject("Baked Navigation"); navObject.transform.SetParent(map.transform, false);
            var navigation = navObject.AddComponent<AstarPath>(); navigation.scanOnStartup = false;
            navigation.logPathResults = PathLog.OnlyErrors;
            var graph = navigation.data.AddGraph(typeof(GridGraph)) as GridGraph;
            graph.center = ground.bounds.center; graph.rotation = new Vector3(90, 0, 0);
            graph.SetDimensions(Mathf.FloorToInt(ground.bounds.size.x / .25f), Mathf.FloorToInt(ground.bounds.size.y / .25f), .25f);
            graph.neighbours = NumNeighbours.Eight; graph.cutCorners = false;
            float footprint = new[] { "NetworkEnemyBase", "NetworkEnemySkeleton", "NetworkEnemyImp", "NetworkEnemyLustSinner" }
                .Select(n => Load<GameObject>("Assets/_Project/Content/NetworkCombat/" + n + ".prefab").GetComponent<EnemyController>())
                .Where(e => !e.enemyFlyingType).Max(e => GameplayMapContext.Radius((CircleCollider2D)e.collider));
            graph.collision.use2D = true; graph.collision.heightCheck = false;
            graph.collision.collisionCheck = true; graph.collision.type = ColliderType.Sphere;
            graph.collision.diameter = (footprint + GameplayMapContext.Skin) * 2 / .25f;
            graph.collision.mask = LayerMask.GetMask("Obstacles", "Edges");
            Physics2D.SyncTransforms(); navigation.Scan();
            File.WriteAllBytes(NavPath, navigation.data.SerializeGraphs(SerializeSettings.NodesAndSettings));
            AssetDatabase.ImportAsset(NavPath, ImportAssetOptions.ForceSynchronousImport);
            navigation.data.cacheStartup = true; navigation.data.file_cachedStartup = Load<TextAsset>(NavPath);
            var context = map.AddComponent<GameplayMapContext>(); context.Configure(ground, navigation);
            var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(map, MapPath, InteractionMode.AutomatedAction);
            if (PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.Variant) throw new InvalidOperationException("Formal map must inherit the static map.");

            Camera camera = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Single(c => c.GetComponent<GameplayCameraRig>() != null);
            camera.orthographic = false; camera.fieldOfView = 80;
            camera.transform.SetPositionAndRotation(new Vector3(0, .99f, -10), Quaternion.identity);
            var cameraData = camera.GetUniversalAdditionalCameraData();
            var pipeline = Load<UniversalRenderPipelineAsset>("Assets/Settings/UniversalRP.asset");
            var pipelineSo = new SerializedObject(pipeline); var renderers = pipelineSo.FindProperty("m_RendererDataList");
            int rendererIndex = Enumerable.Range(0, renderers.arraySize).Single(i => renderers.GetArrayElementAtIndex(i).objectReferenceValue == Load<ScriptableRendererData>(Content + "/NordicRenderer2D.asset"));
            cameraData.SetRenderer(rendererIndex);
            var rig = camera.GetComponent<ProCamera2D>(); rig.UpdateType = UpdateType.LateUpdate;
            var zoom = camera.GetComponent<ProCamera2DZoomToFitTargets>(); if (zoom != null) zoom.enabled = false;
            Set(camera.GetComponent<GameplayCameraRig>(), "boundaryGround", ground);
            var cameraSo = new SerializedObject(camera.GetComponent<GameplayCameraRig>()); cameraSo.FindProperty("nordicFixedView").boolValue = true; cameraSo.ApplyModifiedPropertiesWithoutUndo();
            var boundaries = camera.GetComponent<ProCamera2DNumericBoundaries>();
            boundaries.LeftBoundary = ground.bounds.min.x; boundaries.RightBoundary = ground.bounds.max.x;
            boundaries.BottomBoundary = ground.bounds.min.y; boundaries.TopBoundary = ground.bounds.max.y; boundaries.UseSoftBoundaries = false;
            Set(Object.FindFirstObjectByType<NetworkGameplayEnemySpawner>(), "boundaryGround", ground);
            foreach (var start in Object.FindObjectsByType<NetworkStartPosition>(FindObjectsSortMode.None).OrderBy(s => s.name))
                start.transform.position = context.FindSpawn(start.transform.position, .1f);
            foreach (var light in Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None).Where(l => l.lightType == Light2D.LightType.Global)) Object.DestroyImmediate(light.gameObject);
            var global = new GameObject("Nordic Global Light").AddComponent<Light2D>();
            global.lightType = Light2D.LightType.Global; global.color = Color.white; global.intensity = .9f;
            var lightSo = new SerializedObject(global); var layers = lightSo.FindProperty("m_ApplyToSortingLayers");
            layers.arraySize = SortingLayer.layers.Length;
            for (int i = 0; i < layers.arraySize; i++) layers.GetArrayElementAtIndex(i).intValue = SortingLayer.layers[i].id;
            lightSo.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Validate();
        }
        private static void SavePlayerBaseline(string path)
        {
            if (File.Exists(BaselinePath)) return;
            string text = File.ReadAllText(path);
            var blocks = Regex.Matches(text, @"(?ms)^--- !u!\d+ &(?<id>\d+)\r?\n(?<body>.*?)(?=^--- !u!|\z)")
                .Cast<Match>().ToDictionary(m => m.Groups["id"].Value, m => m.Groups["body"].Value);
            string rootTransform = blocks.Values.Single(b => b.StartsWith("Transform:") && b.Contains("m_Father: {fileID: 0}"));
            string rootId = Regex.Match(rootTransform, @"m_GameObject: \{fileID: (\d+)\}").Groups[1].Value;
            var scripts = Regex.Matches(blocks[rootId], @"component: \{fileID: (\d+)\}").Cast<Match>()
                .Select(m => blocks[m.Groups[1].Value]).Select(b => Regex.Match(b, @"m_Script: \{fileID: \d+, guid: (\w+)").Groups[1].Value)
                .Where(g => !string.IsNullOrEmpty(g)).Where(g => {
                    Type type = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(g))?.GetClass();
                    return type != null && typeof(NetworkBehaviour).IsAssignableFrom(type);
                }).ToArray();
            if (scripts.Length < 5) throw new InvalidOperationException("Cannot establish original network component baseline.");
            File.WriteAllText(BaselinePath, JsonUtility.ToJson(new PlayerBaseline { networkScripts = scripts }, true));
            AssetDatabase.ImportAsset(BaselinePath);
        }

        private static void BuildPlayer()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                var movement = root.GetComponent<PlayerMovement>();
                var old = root.transform.Find("Nordic Visual"); if (old != null) Object.DestroyImmediate(old.gameObject);
                old = root.transform.Find("Sprite"); if (old != null) Object.DestroyImmediate(old.gameObject);
                var visual = new GameObject("Nordic Visual"); visual.transform.SetParent(root.transform, false);
                var group = visual.AddComponent<SortingGroup>(); group.sortingLayerName = "Props"; group.sortingOrder = 0;
                var facing = new GameObject("Facing"); facing.transform.SetParent(visual.transform, false);
                var art = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(Content + "/Prefabs/Viking (Character Variant).prefab"));
                PrefabUtility.UnpackPrefabInstance(art, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                art.transform.SetParent(facing.transform, false); // Keep the original Scaler local position (.687).
                var renderers = art.GetComponentsInChildren<SpriteRenderer>(true);
                for (int i = 0; i < renderers.Length; i++) { renderers[i].sortingLayerName = "Props"; renderers[i].sortingOrder = i; }
                var unityAnimator = art.AddComponent<Animator>(); unityAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var animancer = art.AddComponent<AnimancerComponent>(); animancer.Animator = unityAnimator;
                var animator = art.AddComponent<NordicPlayerAnimator>();
                foreach (string clipName in new[] { "Viking_Dash", "Viking_Die_Player" }) AnimationUtility.SetAnimationEvents(Clip(clipName), Array.Empty<AnimationEvent>());
                var body = renderers.Single(r => r.name == "Cuerpo");
                Set(animator, "spriteRenderer", body); Set(animator, "defaultMaterial", body.sharedMaterial);
                animator.Configure(facing.transform, body, renderers.Where(r => r.name != "Shadow").ToArray(),
                    Clip("Viking_Idle_Player"), Clip("Viking_Walk_Player"), Clip("Viking_Dash"), Clip("Viking_Die_Player"));
                Clip("Viking_Idle_Player").SampleAnimation(art, 0);
                float bottom = renderers.Where(r => r.name != "Shadow").SelectMany(r => r.sprite.vertices.Select(v => facing.transform.InverseTransformPoint(r.transform.TransformPoint(v)))).Min(v => v.y);
                facing.transform.localPosition = Vector3.down * bottom;
                movement.animator = animator; movement.playerAnimator = animator; movement.spriteRenderer = body;
                var invisibility = root.GetComponent<CharacterInvisibility>();
                invisibility.spriteRenderers = renderers.ToList();
                invisibility.visibleObjects = renderers.Select(r => r.gameObject).Distinct().ToList();
                if (root.GetComponent<NetworkPlayerPresentation>() == null) root.AddComponent<NetworkPlayerPresentation>();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        private static AnimationClip Clip(string name)
        {
            var clip = Load<AnimationClip>(Content + "/Animations/" + name + ".anim");
            return clip;
        }
        public static void Validate()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var map = Object.FindFirstObjectByType<GameplayMapContext>();
            if (map == null || Mathf.Abs(map.Bounds.size.x - 119.3386f) > .001f || Mathf.Abs(map.Bounds.size.y - 67.128f) > .001f) throw new InvalidOperationException("Formal map dimensions mismatch.");
            var player = Load<GameObject>(PlayerPath);
            if (player.GetComponent<CircleCollider2D>().radius != .1f || player.transform.Find("HitBox").GetComponent<CircleCollider2D>().radius != .13f) throw new InvalidOperationException("Player footprint changed.");
            var animator = player.GetComponentInChildren<NordicPlayerAnimator>(true);
            if (animator == null || player.GetComponent<PlayerMovement>().playerAnimator != animator || animator.BodySprite == null) throw new InvalidOperationException("Axeldor binding missing.");
            if (PrefabUtility.GetPrefabAssetType(Load<GameObject>(MapPath)) != PrefabAssetType.Variant) throw new InvalidOperationException("Map variant inheritance lost.");
            var baseline = JsonUtility.FromJson<PlayerBaseline>(File.ReadAllText(BaselinePath));
            var behaviours = player.GetComponents<NetworkBehaviour>();
            var originalScripts = behaviours.Where(c => !(c is NetworkPlayerPresentation)).Select(c => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(c))));
            if (!originalScripts.SequenceEqual(baseline.networkScripts) || !(behaviours.Last() is NetworkPlayerPresentation)) throw new InvalidOperationException("Original network component sequence changed.");
            if (Object.FindObjectsByType<GameplayMapContext>(FindObjectsSortMode.None).Length != 1 ||
                Object.FindObjectsByType<NetworkStartPosition>(FindObjectsSortMode.None).Length != 4) throw new InvalidOperationException("Duplicate map or spawn objects.");
            var rig = Object.FindFirstObjectByType<GameplayCameraRig>();
            var groundRef = new SerializedObject(rig).FindProperty("boundaryGround").objectReferenceValue;
            if (groundRef != map.Ground || new SerializedObject(Object.FindFirstObjectByType<NetworkGameplayEnemySpawner>()).FindProperty("boundaryGround").objectReferenceValue != map.Ground) throw new InvalidOperationException("Ground references diverged.");
            if (Load<TextAsset>(NavPath).bytes.Length < 1000 || !map.Navigation.data.cacheStartup || map.Navigation.scanOnStartup) throw new InvalidOperationException("Baked navigation startup is not configured.");
            foreach (var wall in map.GetComponentsInChildren<BoxCollider2D>(true).Where(c => c.name.StartsWith("Boundary ")))
                if (wall.isTrigger || wall.gameObject.layer != LayerMask.NameToLayer("Edges")) throw new InvalidOperationException("Map boundary is not solid Edges.");
            foreach (var clip in new[] { Clip("Viking_Idle_Player"), Clip("Viking_Walk_Player"), Clip("Viking_Dash"), Clip("Viking_Die_Player") })
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    if (!string.IsNullOrEmpty(binding.path) && animator.transform.Find(binding.path) == null) throw new InvalidOperationException("Animation binding missing: " + clip.name + ":" + binding.path);
            foreach (var renderer in map.GetComponentsInChildren<SpriteRenderer>(true).Concat(player.GetComponentsInChildren<SpriteRenderer>(true)))
                if (renderer.sprite == null || renderer.sharedMaterial == null || !renderer.sharedMaterial.shader.isSupported) throw new InvalidOperationException("Missing visual dependency: " + renderer.name);
            foreach (var root in scene.GetRootGameObjects().Concat(new[] { player }))
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0) throw new InvalidOperationException("Missing script: " + t.name);
            Directory.CreateDirectory("Logs/NordicGameplay");
            File.WriteAllText("Logs/NordicGameplay/static-validation.json", JsonUtility.ToJson(new Validation { passed = true, width = map.Bounds.size.x, height = map.Bounds.size.y,
                sprites = map.GetComponentsInChildren<SpriteRenderer>(true).Length, colliders = map.GetComponentsInChildren<Collider2D>(true).Length }, true));
            var manifest = new IntegrationManifest { map = MapPath, baseMap = Content + "/NordicStaticMap.prefab", sourceLayout = Content + "/MidgardLayout.json",
                dependencies = AssetDatabase.GetDependencies(new[] { ScenePath, PlayerPath }, true).Where(p => p.StartsWith(Content + "/") && File.Exists(p))
                    .OrderBy(p => p, StringComparer.Ordinal).Select(p => new Dependency { path = p, sha256 = Hash(p) }).ToArray() };
            File.WriteAllText("Logs/NordicGameplay/dependencies.json", JsonUtility.ToJson(manifest, true));
            Debug.Log("[NordicGameplay] Static validation PASS");
        }
        private static string Hash(string path) { using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
        [Serializable] private sealed class PlayerBaseline { public string[] networkScripts; }
        [Serializable] private sealed class Dependency { public string path, sha256; }
        [Serializable] private sealed class IntegrationManifest { public string map, baseMap, sourceLayout; public Dependency[] dependencies; }
        [Serializable] private sealed class Validation { public bool passed; public float width, height; public int sprites, colliders; }
    }
}
