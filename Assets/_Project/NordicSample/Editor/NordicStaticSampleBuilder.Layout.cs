using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.NordicSample.Editor
{
    public static partial class NordicStaticSampleBuilder
    {
        private static void ConfigureValidation(NordicSampleValidation validation)
        {
            validation.treePrefab = Load<GameObject>(Catalog.prefabs.First(p => p.group == 1).path);
            Prop torch = Catalog.prefabs.First(p => p.group == 7);
            var fixture = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(torch.path));
            ConfigureTorch(fixture, torch);
            string folder = Root + "/Validation";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Root, "Validation");
            validation.torchPrefab = PrefabUtility.SaveAsPrefabAsset(fixture, folder + "/TorchFixture.prefab");
            Object.DestroyImmediate(fixture);
        }

        private static void BuildRecoveredLayout(GameObject map, Bounds bounds, NordicMidgardLayout.Config settings = null)
        {
            var c = settings ?? Config;
            var layout = NordicMidgardLayout.Generate(c, bounds);
            string json = JsonUtility.ToJson(layout, true);
            Require(json == JsonUtility.ToJson(NordicMidgardLayout.Generate(c, bounds), true), "Layout must be reproducible.");
            Require(layout.placements.Count(p => p.kind == "base") == layout.chunks * 4, "Native base floor count mismatch.");
            Require(c.groups.SelectMany(g => g.paths).Count() == 35, "Expected 35 nonseason references.");
            // Verify actual accepted pairs across adjacent positive/negative chunks.
            foreach (var g in c.groups)
            {
                var props = layout.placements.Where(p => p.group == g.group).ToArray();
                for (int i = 0; i < props.Length; i++)
                for (int j = i + 1; j < props.Length; j++)
                    if (Mathf.Abs(props[i].chunkX - props[j].chunkX) <= 1 && Mathf.Abs(props[i].chunkY - props[j].chunkY) <= 1)
                        Require((props[i].position - props[j].position).sqrMagnitude > g.distance * g.distance,
                            "Spacing violation: " + props[i].id + " / " + props[j].id);
            }
            var baseRoot = Child(map.transform, "BaseFloors");
            var floorsRoot = Child(map.transform, "Floors");
            var propsRoot = Child(map.transform, "Environment");
            var chunks = new Dictionary<string, Transform>();
            var pools = new Dictionary<string, Transform>();
            var catalog = Catalog.prefabs.ToDictionary(p => p.path);
            foreach (var p in layout.placements)
            {
                if (p.kind == "prop")
                {
                    if (!pools.TryGetValue(p.path, out var parent)) pools[p.path] = parent = Child(propsRoot, Path.GetFileNameWithoutExtension(p.path));
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(p.path), parent);
                    instance.name = Path.GetFileNameWithoutExtension(p.path) + " #" + p.id;
                    instance.transform.position = p.position;
                    instance.transform.localScale = p.scale;
                    if (catalog[p.path].lights.Length > 0) ConfigureTorch(instance, catalog[p.path]);
                }
                else
                {
                    string key = p.kind + " " + p.chunkX + "," + p.chunkY;
                    if (!chunks.TryGetValue(key, out var chunk))
                    {
                        chunks[key] = chunk = Child(p.kind == "base" ? baseRoot : floorsRoot, key);
                        chunk.position = new Vector3(p.chunkX * c.chunkSize * c.offset, p.chunkY * c.chunkSize * c.offset, 0);
                    }
                    var renderer = SpriteNode(p.id, Load<Sprite>(p.path), chunk, p.position,
                        p.kind == "base" ? "BackgroundBack" : "Background", p.order);
                    renderer.color = p.color;
                    renderer.spriteSortPoint = (SpriteSortPoint)c.floorSortPoint;
                    renderer.transform.localScale = p.scale;
                    renderer.transform.localRotation = Quaternion.Euler(0, 0, p.rotation);
                }
            }
            Directory.CreateDirectory(Artifacts);
            File.WriteAllText(Root + "/MidgardLayout.json", json);
            File.WriteAllText(Artifacts + "/rule-validation.txt", "PASS\nRepeatable layout; 35 candidates; four base sprites per chunk; adjacent-chunk same-group spacing.\n" +
                "Chunks=" + layout.chunks + "; placements=" + layout.placements.Length + "; absent=" + layout.absentProps.Length + "\n");
            Debug.Log("[Nordic] Native-rule layout: " + layout.chunks + " chunks, " + layout.placements.Length + " placements, " + layout.absentProps.Length + " absent prop types.");
        }

        private static void BuildBoundaries(Transform map, Bounds bounds)
        {
            Transform walls = Child(map, "Boundaries");
            const float thickness = 1;
            Wall("Left", new Vector2(bounds.min.x - thickness / 2, bounds.center.y), new Vector2(thickness, bounds.size.y + 2 * thickness));
            Wall("Right", new Vector2(bounds.max.x + thickness / 2, bounds.center.y), new Vector2(thickness, bounds.size.y + 2 * thickness));
            Wall("Bottom", new Vector2(bounds.center.x, bounds.min.y - thickness / 2), new Vector2(bounds.size.x + 2 * thickness, thickness));
            Wall("Top", new Vector2(bounds.center.x, bounds.max.y + thickness / 2), new Vector2(bounds.size.x + 2 * thickness, thickness));
            void Wall(string name, Vector2 position, Vector2 size)
            {
                var wall = Child(walls, "Boundary " + name);
                wall.position = position; wall.gameObject.layer = LayerMask.NameToLayer("Obstacles");
                var collider = wall.gameObject.AddComponent<BoxCollider2D>(); collider.size = size; collider.isTrigger = false;
            }
        }

        private static void ConfigureSampleRenderer(Camera camera)
        {
            const string rendererPath = Root + "/NordicRenderer2D.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<Renderer2DData>(rendererPath);
            if (renderer == null)
            {
                AssetDatabase.CopyAsset("Assets/Settings/Renderer2D.asset", rendererPath);
                renderer = Load<Renderer2DData>(rendererPath);
            }
            var serialized = new SerializedObject(renderer);
            serialized.FindProperty("m_TransparencySortMode").intValue = (int)TransparencySortMode.CustomAxis;
            serialized.FindProperty("m_TransparencySortAxis").vector3Value = Vector3.up;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var pipeline = Load<UniversalRenderPipelineAsset>("Assets/Settings/UniversalRP.asset");
            var asset = new SerializedObject(pipeline);
            var list = asset.FindProperty("m_RendererDataList");
            int index = -1;
            for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == renderer) index = i;
            if (index < 0)
            {
                index = list.arraySize; list.arraySize++;
                list.GetArrayElementAtIndex(index).objectReferenceValue = renderer;
                asset.ApplyModifiedPropertiesWithoutUndo();
            }
            camera.GetUniversalAdditionalCameraData().SetRenderer(index);
        }
    }
}
