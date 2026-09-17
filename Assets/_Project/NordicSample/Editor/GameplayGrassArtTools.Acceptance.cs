using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.NordicSample.Editor
{
    public static partial class GameplayGrassArtTools
    {
        // Runs in the Editor only. Temporary scenes are never saved over Gameplay.
        public static void CaptureSamples() => CaptureViews(true);
        public static void CaptureMap() => CaptureViews(false);
        public static void Acceptance()
        {
            CheckEditState(); CheckProfile(Profile);
            Directory.CreateDirectory(Output);
            var result = new AcceptanceReport();
            var protectedPaths = new[] {
                Content + "/NordicStaticMap.prefab", Content + "/GameplayNavigation.bytes",
                "Assets/_Project/Scenes/Gameplay.unity", "Assets/_Project/Scenes/NordicStaticSample.unity",
                "Assets/_Project/Content/NetworkCombat/NetworkPlayer.prefab",
                "Assets/Sprite/caodiwenli_all.tga", "Assets/Sprite/changjing_caodi.tga", "Assets/Sprite/changjing_caodi.tga.meta"
            };
            var hashes = protectedPaths.ToDictionary(p => p, Hash);
            CaptureSamples();
            Apply(); string first = Hash(MapPath);
            Apply(); result.repeatIdentical = first == Hash(MapPath);
            Require(result.repeatIdentical, "连续应用产生了不同地图文件。");
            var profile = Profile;
            float floor = profile.floorScale, cluster = profile.clusterScale, tuft = profile.tuftScale;
            try
            {
                profile.floorScale = floor * .8f; profile.clusterScale = cluster * .5f; profile.tuftScale = tuft * .75f;
                EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                Apply(); result.scaleChangesVerified = true; // Apply validates all expected scales after reloading.
            }
            finally
            {
                profile.floorScale = floor; profile.clusterScale = cluster; profile.tuftScale = tuft;
                EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                Apply();
            }
            Require(first == Hash(MapPath), "倍率复原后地图未回到相同状态。");
            // A same-layout image overwrite causes this same import path. Do not change the artist's pixels.
            AssetDatabase.ImportAsset("Assets/Sprite/caodiwenli_all.tga", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            Validate(); result.reimportReferencesVerified = first == Hash(MapPath);
            Require(result.reimportReferencesVerified, "图集重新导入改变了地图引用。");
            CaptureMap();
            result.protectedAssetsUnchanged = protectedPaths.All(p => hashes[p] == Hash(p));
            Require(result.protectedAssetsUnchanged, "非目标文件发生变化。");
            result.mapSha256 = first; result.passed = true;
            File.WriteAllText(Output + "/acceptance.json", JsonUtility.ToJson(result, true));
            UnityEngine.Debug.Log("[GrassArt] Acceptance PASS: repeat, scale, reimport, protected assets, captures.");
        }
        private static string Hash(string path)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
        private static void CaptureViews(bool samples)
        {
            Require(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, "截图需要图形设备，请勿使用 -nographics。");
            Directory.CreateDirectory(Output);
            Scene scene = EditorSceneManager.NewPreviewScene();
            var timer = Stopwatch.StartNew();
            try
            {
                var map = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(MapPath), scene);
                double loadMs = timer.Elapsed.TotalMilliseconds;
                var camera = new GameObject("Grass Art Evidence Camera").AddComponent<Camera>();
                SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
                camera.scene = scene;
                camera.orthographic = false; camera.fieldOfView = 80; camera.nearClipPlane = .1f; camera.farClipPlane = 200;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.18f, .2f, .18f);
                camera.transform.SetPositionAndRotation(new Vector3(0, .99f, -10), Quaternion.identity);
                var pipeline = Load<UniversalRenderPipelineAsset>("Assets/Settings/UniversalRP.asset");
                var renderers = new SerializedObject(pipeline).FindProperty("m_RendererDataList");
                int rendererIndex = Enumerable.Range(0, renderers.arraySize).Single(i => renderers.GetArrayElementAtIndex(i).objectReferenceValue == Load<UnityEngine.Rendering.Universal.ScriptableRendererData>(Content + "/NordicRenderer2D.asset"));
                camera.GetUniversalAdditionalCameraData().SetRenderer(rendererIndex);
                var light = new GameObject("Grass Art Evidence Light").AddComponent<Light2D>();
                SceneManager.MoveGameObjectToScene(light.gameObject, scene);
                light.lightType = Light2D.LightType.Global; light.color = Color.white; light.intensity = .9f;
                var lightSo = new SerializedObject(light); var layers = lightSo.FindProperty("m_ApplyToSortingLayers");
                layers.arraySize = SortingLayer.layers.Length;
                for (int i = 0; i < SortingLayer.layers.Length; i++) layers.GetArrayElementAtIndex(i).intValue = SortingLayer.layers[i].id;
                lightSo.ApplyModifiedPropertiesWithoutUndo();
                if (samples)
                {
                    map.transform.Find("Floors").gameObject.SetActive(false);
                    map.transform.Find("Environment").gameObject.SetActive(false);
                    var profile = Profile; CheckProfile(profile);
                    var material = map.transform.Find("BaseFloors").GetComponentInChildren<SpriteRenderer>().sharedMaterial;
                    Add(profile.soil, new Vector2(-5, 5), false); Add(profile.rockySoil, new Vector2(6, 5), false);
                    for (int i = 0; i < 3; i++) { Add(profile.clusters[i], new Vector2(-8 + i * 8, -1), true); Add(profile.tufts[i], new Vector2(-6 + i * 6, -5), true); }
                    camera.transform.position = new Vector3(0, 1, -10);
                    NordicStaticSampleBuilder.Capture(camera, Output + "/samples-eight-sprites.png", 1920, 1080);
                    void Add(Sprite sprite, Vector2 position, bool grass)
                    {
                        var r = new GameObject(sprite.name).AddComponent<SpriteRenderer>();
                        SceneManager.MoveGameObjectToScene(r.gameObject, scene);
                        r.sprite = sprite; r.sharedMaterial = material; r.transform.position = position;
                        r.sortingLayerName = grass ? "BackgroundFront" : "Background"; r.sortingOrder = grass ? 5 : 1;
                        r.spriteSortPoint = grass ? SpriteSortPoint.Pivot : SpriteSortPoint.Center;
                    }
                    return;
                }
                var points = new[] { new Vector2(0, .99f), new Vector2(-12, -5), new Vector2(32, 12), new Vector2(44.75f, 26.16f), new Vector2(-44.75f, -24.18f) };
                for (int i = 0; i < points.Length; i++)
                {
                    camera.transform.position = new Vector3(points[i].x, points[i].y, -10);
                    NordicStaticSampleBuilder.Capture(camera, Output + "/map-view-" + i + ".png", 1920, 1080);
                }
                camera.transform.position = new Vector3(0, .99f, -48);
                NordicStaticSampleBuilder.Capture(camera, Output + "/map-overview.png", 2560, 1440);
                camera.transform.position = new Vector3(0, .99f, -10);
                var render = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                render.Create(); var timings = new double[30];
                try
                {
                    for (int i = 0; i < timings.Length; i++)
                    {
                        timer.Restart();
                        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = render });
                        timings[i] = timer.Elapsed.TotalMilliseconds;
                    }
                }
                finally { render.Release(); Object.DestroyImmediate(render); }
                var stats = new CaptureStats { editorInstantiationMs = loadMs, editorRenderSubmitMedianMs = timings.OrderBy(t => t).ElementAt(15),
                    editorAllocatedMiB = Profiler.GetTotalAllocatedMemoryLong() / 1048576d,
                    enabledSprites = map.GetComponentsInChildren<SpriteRenderer>(true).Count(r => r.enabled && r.gameObject.activeInHierarchy),
                    note = "Editor isolated map render; FOV80 Z=-10 except overview Z=-48. CPU render submission timing, not standalone gameplay frame time or GPU timing. No network/enemy simulation." };
                File.WriteAllText(Output + "/capture-stats.json", JsonUtility.ToJson(stats, true));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        [Serializable] private sealed class AcceptanceReport
        {
            public bool passed, repeatIdentical, scaleChangesVerified, reimportReferencesVerified, protectedAssetsUnchanged;
            public string mapSha256;
        }
        [Serializable] private sealed class CaptureStats
        {
            public double editorInstantiationMs, editorRenderSubmitMedianMs, editorAllocatedMiB;
            public int enabledSprites;
            public string note;
        }
    }
}
