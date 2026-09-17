using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.NordicSample.Editor
{
    public static partial class GameplayGrassArtTools
    {
        public const string Content = "Assets/_Project/Content/Nordic";
        public const string MapPath = Content + "/NordicGameplayMap.prefab";
        public const string ProfilePath = Content + "/Editor/GameplayGrassArt.asset";
        public const string Output = "Logs/GrassArtReplacement";
        internal const string ReplacementName = "Grass Art Replacement";
        private const string SpriteFolder = Content + "/Sprites/";
        internal static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path)
            ?? throw new InvalidOperationException("资源缺失：" + path);
        internal static GameplayGrassArtProfile Profile => Load<GameplayGrassArtProfile>(ProfilePath);
        internal static NordicMidgardLayout.Layout Layout => JsonUtility.FromJson<NordicMidgardLayout.Layout>(File.ReadAllText(Content + "/MidgardLayout.json"));
        internal static bool IsGrass(NordicMidgardLayout.Placement p) => p.kind == "prop" &&
            (Path.GetFileName(p.path).StartsWith("Composition_Grass_", StringComparison.Ordinal) ||
             Path.GetFileName(p.path) == "Grass_1.prefab" || Path.GetFileName(p.path) == "Grass_2 (Grass_1 Variant).prefab");
        internal static bool IsCluster(NordicMidgardLayout.Placement p) => Path.GetFileName(p.path).StartsWith("Composition_Grass_", StringComparison.Ordinal);
        internal static int Variant(string id)
        {
            // FNV-1a, independent of process hash seeds and UnityEngine.Random state.
            uint hash = 2166136261;
            foreach (char c in id) hash = unchecked((hash ^ c) * 16777619);
            return (int)(hash % 3);
        }
        private static HashSet<Sprite> OldGrass() => new HashSet<Sprite>(Enumerable.Range(1, 4).Select(i => Load<Sprite>(SpriteFolder + "minihierba " + i + ".asset")));
        private static Dictionary<string, Transform> Index(GameObject map) => map.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.StartsWith("floor-", StringComparison.Ordinal) || t.name.Contains(" #prop-"))
            .ToDictionary(t => t.name, StringComparer.Ordinal);
        internal static Transform Find(Dictionary<string, Transform> index, NordicMidgardLayout.Placement p)
        {
            string key = p.kind == "floor" ? p.id : Path.GetFileNameWithoutExtension(p.path) + " #" + p.id;
            if (!index.TryGetValue(key, out var t)) throw new InvalidOperationException("布局实例缺失：" + key);
            return t;
        }
        private static Sprite FloorSprite(GameplayGrassArtProfile profile, NordicMidgardLayout.Placement p)
        {
            if (Path.GetFileName(p.path) == "terreno dibujado7.asset") return profile.soil;
            if (Path.GetFileName(p.path) == "terreno dibujado_rocas2.asset") return profile.rockySoil;
            throw new InvalidOperationException("未支持的地表条目：" + p.path);
        }
        private static void CheckProfile(GameplayGrassArtProfile p)
        {
            Require(p.soil && p.rockySoil && p.clusters?.Length == 3 && p.tufts?.Length == 3, "配置需要两张地表、三张草丛、三张单株草。");
            foreach (float value in new[] { p.floorScale, p.clusterScale, p.tuftScale })
                Require(value > 0 && !float.IsNaN(value) && !float.IsInfinity(value), "大小倍率必须为有限正数。");
            foreach (Sprite sprite in p.clusters.Concat(p.tufts))
            {
                Require(sprite, "草图片槽不能为空。");
                Require(Vector2.Distance(sprite.pivot, new Vector2(sprite.rect.width / 2, 0)) < .01f,
                    sprite.name + " 的锚点需在 Sprite Editor 中设为 Bottom Center（底部中央），然后 Apply。");
                Require(!OldGrass().Contains(sprite), "请使用新草素材，不要将原草填入替换槽。");
            }
        }
        private static void CheckEditState()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "请先停止运行。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Require(!SceneManager.GetSceneAt(i).isDirty, "存在未保存场景；请自行保存或关闭后应用，工具不会自动保存场景。");
            Require(PrefabStageUtility.GetCurrentPrefabStage()?.assetPath != MapPath, "请先退出正式地图的 Prefab 编辑模式。");
        }

        public static void Apply()
        {
            CheckEditState();
            var profile = Profile; CheckProfile(profile);
            var layout = Layout;
            string backup = Output + "/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-apply";
            Directory.CreateDirectory(backup);
            File.Copy(MapPath, backup + "/NordicGameplayMap.prefab");
            File.Copy(MapPath + ".meta", backup + "/NordicGameplayMap.prefab.meta");
            File.Copy(ProfilePath, backup + "/GameplayGrassArt.asset");
            GameObject map = PrefabUtility.LoadPrefabContents(MapPath);
            try
            {
                var index = Index(map); var oldGrass = OldGrass();
                var floors = layout.placements.Where(p => p.kind == "floor").ToArray();
                var grasses = layout.placements.Where(IsGrass).ToArray();
                Require(floors.Length == 736 && grasses.Length == 2709, "布局数量与已确认地图不同；停止替换，避免遗漏。");
                if (profile.floorScale == 1)
                {
                    // Remove only our floor XY scale overrides in one operation. Reverting each
                    // property separately repeatedly reconciles the entire large prefab hierarchy.
                    var sources = new HashSet<Object>(floors.Select(p => (Object)PrefabUtility.GetCorrespondingObjectFromOriginalSource(Find(index, p))));
                    var modifications = PrefabUtility.GetPropertyModifications(map);
                    var retained = modifications.Where(m => !sources.Contains(m.target) ||
                        (m.propertyPath != "m_LocalScale.x" && m.propertyPath != "m_LocalScale.y")).ToArray();
                    if (retained.Length != modifications.Length) PrefabUtility.SetPropertyModifications(map, retained);
                }
                var managed = new HashSet<SpriteRenderer>(floors.Select(p => Find(index, p).GetComponent<SpriteRenderer>()));
                foreach (var p in grasses)
                    foreach (var r in Find(index, p).GetComponentsInChildren<SpriteRenderer>(true).Where(r => oldGrass.Contains(r.sprite))) managed.Add(r);
                string protectedBefore = ProtectedFingerprint(map, managed, floors.Select(p => Find(index, p)).ToHashSet());
                foreach (var p in floors)
                {
                    var t = Find(index, p);
                    t.GetComponent<SpriteRenderer>().sprite = FloorSprite(profile, p);
                    t.localScale = new Vector3(p.scale.x * profile.floorScale, p.scale.y * profile.floorScale, p.scale.z);
                }
                foreach (var p in grasses)
                {
                    Transform root = Find(index, p);
                    var originals = root.GetComponentsInChildren<SpriteRenderer>(true).Where(r => oldGrass.Contains(r.sprite)).ToArray();
                    Require(originals.Length > 0, "未找到原草：" + root.name);
                    var source = originals[0];
                    foreach (var r in originals) r.enabled = false;
                    Transform child = root.Find(ReplacementName);
                    if (!child)
                    {
                        child = new GameObject(ReplacementName).transform;
                        child.SetParent(root, false); child.gameObject.layer = source.gameObject.layer;
                        child.gameObject.AddComponent<SpriteRenderer>();
                    }
                    var visual = child.GetComponent<SpriteRenderer>();
                    Require(visual, "替换节点缺少 SpriteRenderer：" + root.name);
                    bool cluster = IsCluster(p);
                    visual.sprite = (cluster ? profile.clusters : profile.tufts)[Variant(p.id)];
                    visual.sharedMaterial = source.sharedMaterial; visual.color = source.color;
                    visual.sortingLayerID = source.sortingLayerID; visual.sortingOrder = source.sortingOrder;
                    visual.spriteSortPoint = SpriteSortPoint.Pivot;
                    visual.flipX = false; visual.flipY = false; visual.enabled = true;
                    child.localPosition = Vector3.zero; child.localRotation = Quaternion.identity;
                    float scale = cluster ? profile.clusterScale : profile.tuftScale;
                    child.localScale = new Vector3(scale, scale, 1);
                }
                Require(protectedBefore == ProtectedFingerprint(map, managed, floors.Select(p => Find(index, p)).ToHashSet()),
                    "非目标对象发生变化；未保存地图。");
                CheckMap(map, profile, layout);
                PrefabUtility.SaveAsPrefabAsset(map, MapPath, out bool success);
                Require(success, "正式地图保存失败。");
            }
            finally { PrefabUtility.UnloadPrefabContents(map); }
            Validate();
            Debug.Log("[GrassArt] 已更新正式地图。备份：" + backup);
        }

        public static void Validate()
        {
            CheckProfile(Profile);
            GameObject map = PrefabUtility.LoadPrefabContents(MapPath);
            try
            {
                var report = CheckMap(map, Profile, Layout);
                Directory.CreateDirectory(Output);
                File.WriteAllText(Output + "/validation.json", JsonUtility.ToJson(report, true));
                Debug.Log($"[GrassArt] PASS: {report.floors} floors; {report.clusters} clusters; {report.tufts} tufts; {report.preservedStones} stones preserved.");
            }
            finally { PrefabUtility.UnloadPrefabContents(map); }
        }
        private static Validation CheckMap(GameObject map, GameplayGrassArtProfile profile, NordicMidgardLayout.Layout layout)
        {
            var report = new Validation { passed = true, map = MapPath, profile = ProfilePath };
            var index = Index(map); var oldGrass = OldGrass();
            foreach (var p in layout.placements.Where(p => p.kind == "floor" || IsGrass(p)))
            {
                var t = Find(index, p);
                Require((t.position - p.position).sqrMagnitude < .000001f, "实例位置改变：" + p.id);
                if (p.kind == "floor")
                {
                    var r = t.GetComponent<SpriteRenderer>();
                    Require(r.sprite == FloorSprite(profile, p), "地表图片不匹配：" + p.id);
                    Require(Vector3.Distance(t.localScale, new Vector3(p.scale.x * profile.floorScale, p.scale.y * profile.floorScale, p.scale.z)) < .00001f, "地表倍率不匹配：" + p.id);
                    Require(r.color == p.color && r.sortingLayerName == "Background" && r.sortingOrder == p.order, "地表颜色或排序改变：" + p.id);
                    report.floors++; continue;
                }
                bool cluster = IsCluster(p); int variant = Variant(p.id);
                Require(Vector3.Distance(t.localScale, p.scale) < .00001f && p.scale.y > 0, "原草布局缩放改变：" + p.id);
                Transform child = t.Find(ReplacementName);
                Require(child, "缺少新草：" + p.id);
                Require(t.Cast<Transform>().Count(c => c.name == ReplacementName) == 1, "存在重复新草：" + p.id);
                var visual = child.GetComponent<SpriteRenderer>();
                float multiplier = cluster ? profile.clusterScale : profile.tuftScale;
                Require(visual && visual.enabled && visual.sprite == (cluster ? profile.clusters : profile.tufts)[variant], "新草引用不匹配：" + p.id);
                Require(child.localPosition == Vector3.zero && child.localRotation == Quaternion.identity &&
                    Vector3.Distance(child.localScale, new Vector3(multiplier, multiplier, 1)) < .00001f, "新草偏移或倍率错误：" + p.id);
                Require(visual.sortingLayerName == "BackgroundFront" && visual.sortingOrder == 5 && visual.spriteSortPoint == SpriteSortPoint.Pivot && !visual.flipY,
                    "新草排序或翻转错误：" + p.id);
                Require(child.GetComponentsInChildren<Collider2D>(true).Length == 0, "新草不应有碰撞体。");
                foreach (var r in t.GetComponentsInChildren<SpriteRenderer>(true).Where(r => r != visual))
                {
                    if (oldGrass.Contains(r.sprite)) { Require(!r.enabled, "旧草仍在显示：" + p.id); report.hiddenGrassParts++; }
                    else { Require(r.enabled, "组合内非草部件被隐藏：" + p.id); report.preservedStones++; }
                }
                if (cluster) { report.clusters++; report.clusterVariants[variant]++; }
                else { report.tufts++; report.tuftVariants[variant]++; }
            }
            Require(report.floors == 736 && report.clusters == 2038 && report.tufts == 671 && report.preservedStones == 1338 && report.hiddenGrassParts == 11579,
                "替换数量或保留部件数量不匹配。");
            Require(map.GetComponentsInChildren<Transform>(true).Count(t => t.name == ReplacementName) == 2709, "存在多余替换节点。");
            foreach (var r in map.GetComponentsInChildren<SpriteRenderer>(true)) Require(r.sprite && r.sharedMaterial, "存在丢失图片或材质：" + r.name);
            foreach (var t in map.GetComponentsInChildren<Transform>(true)) Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0, "存在 Missing Script：" + t.name);
            return report;
        }
        // Capture every untouched transform, renderer and collider before editing the in-memory prefab.
        private static string ProtectedFingerprint(GameObject map, HashSet<SpriteRenderer> managed, HashSet<Transform> scaledFloors)
        {
            var text = new StringBuilder();
            foreach (var t in map.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == ReplacementName) continue;
                text.Append(t.GetInstanceID()).Append(t.localPosition.ToString("R")).Append(t.localRotation.ToString("R"));
                if (!scaledFloors.Contains(t)) text.Append(t.localScale.ToString("R"));
                text.Append(t.gameObject.activeSelf).Append(t.gameObject.layer);
                foreach (var r in t.GetComponents<SpriteRenderer>()) if (!managed.Contains(r)) text.Append(EditorJsonUtility.ToJson(r));
                foreach (var c in t.GetComponents<Collider2D>()) text.Append(EditorJsonUtility.ToJson(c));
            }
            using var hash = SHA256.Create();
            return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
        }
        internal static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        [Serializable] private sealed class Validation
        {
            public bool passed;
            public string map, profile;
            public int floors, clusters, tufts, preservedStones, hiddenGrassParts;
            public int[] clusterVariants = new int[3], tuftVariants = new int[3];
        }
    }
}
