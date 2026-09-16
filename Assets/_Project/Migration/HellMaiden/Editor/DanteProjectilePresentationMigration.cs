using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.HellMaidenMigration.Editor
{
    /// <summary>Repairs the original ID 2 assets in place; derived materials never overwrite shared source assets.</summary>
    public static class DanteProjectilePresentationMigration
    {
        public const string OutputFolder = "Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Projectile";
        public const string ClipPath = OutputFolder + "/AnimationClip/PlayerAttack_Dante_Projectile_In.anim";
        public const string ImpactPath = "Assets/_Project/GameObject/PlayerAttack_Dante_Projectile_Impact.prefab";
        public static readonly string[] ProjectilePaths = {
            "Assets/_Project/GameObject/PlayerAttack_Dante_Projectile.prefab",
            "Assets/_Project/GameObject/PlayerAttack_Dante_Projectile_Fire Variant.prefab",
            "Assets/_Project/GameObject/PlayerAttack_Dante_Projectile_Poison Variant.prefab"
        };
        private const string SpriteShader = "Assets/Plugins/AllIn1SpriteShader/Shaders/AllIn1SpriteShader.shader";
        private const string VfxShader = "Assets/Plugins/AllIn1VfxToolkit/Shaders/AllIn1VfxURPCompat.shader";
        private static string SourceDefault => MonsterSupergroup.EditorTools.ProjectToolPaths.HellMaiden();
        private static readonly string[] Dependencies = {
            "Mesh/FX_MS_HeadCylinder.asset", "Sprite/Shadow (2)_0.asset",
            "Texture2D/Circle SmallAnim2.png", "Texture2D/cloud_2x2_soft_0.png",
            "Texture2D/fire2_1.png", "Texture2D/fire2_6x3.png", "Texture2D/Flame_002_Mask_001.png",
            "Texture2D/Flame_004pixel.png", "Texture2D/FX_TX_Fire_FirePilar_01_6x4_Blur.png",
            "Texture2D/glow1_1.png", "Texture2D/Glow_001_2.png", "Texture2D/Glow_001_3.png",
            "Texture2D/Noise 1_0.png", "Texture2D/palette-downwell_1.png",
            "Texture2D/PlayerAttack_Dante_Projectile_Shadow.png", "Texture2D/rainbow_1.png",
            "Texture2D/seamlessNoise_1.png", "Texture2D/sparkle2_0.png", "Texture2D/white_1.png",
            "AnimationClip/PlayerAttack_Dante_Projectile_In.anim"
        };
        private static readonly string[] Materials = {
            "1asCartoonCoffee_Flame_004-Basic-_3.0_ 2.mat", "asCartoonCoffee_Flame_004-Basic-_3.0_.mat",
            "asCartoonCoffee_Flame_004-Basic-_3.0__0.mat", "asCartoonCoffee_Flame_004-Basic-_3.0__1.mat",
            "ASCartoonCoffee_Glow_001-Basic-_2.0_ 2.mat", "ASfire_3x6_ADD 1.mat",
            "ASFX_MT_HeadLoop_01_Cosmic 2Rasto.mat", "ASFX_MT_HeadLoop_01_Cosmic 2Rasto_0.mat",
            "ASFX_MT_HeadLoop_01_Cosmic 2Rasto_1.mat", "CartoonCoffee_Circle_Ring_001-Basic-_2.0__0.mat",
            "CartoonCoffee_Glow_001-Add-_1.0__2.mat", "cloud_2x2_soft_AB_1.mat", "Distortion_0.mat",
            "glow1_ADD_3.mat", "PlayerAttack_Dante_Projectile_Shadow_0.mat", "sparkle2_ADD 1Pixel_0.mat"
        };


        public static void Import()
        {
            MonsterSupergroup.EditorTools.ProjectToolRunner.CheckLegacyMaintenance("import.projectile", "MonsterSupergroup.HellMaidenMigration.Editor.DanteProjectilePresentationMigration.Import");
            string source = Environment.GetEnvironmentVariable("HELLMAIDEN_SOURCE_PROJECT");
            if (string.IsNullOrWhiteSpace(source)) source = SourceDefault;
            source = Path.Combine(source, "Assets");
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
            foreach (string relative in Dependencies) CopyDependency(source, relative);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var replacements = new Dictionary<string, Material>();
            foreach (string name in Materials)
            {
                string original = Path.Combine(source, "Material", name);
                string path = OutputFolder + "/Material/" + name;
                EnsureFolder(Path.GetDirectoryName(path));
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    // Source YAML retains properties even when its original shader is absent from this project.
                    string yaml = File.ReadAllText(original);
                    yaml = Regex.Replace(yaml, @"(?m)^  m_Shader:.*$", "  m_Shader: {fileID: 4800000, guid: " +
                        AssetDatabase.AssetPathToGUID(SpriteShader) + ", type: 3}");
                    File.WriteAllText(path, yaml, new UTF8Encoding(false));
                    using (var md5 = MD5.Create())
                    {
                        string guid = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes("MonsterSupergroup/Wisp/" + name)))
                            .Replace("-", "").ToLowerInvariant();
                        File.WriteAllText(path + ".meta", "fileFormatVersion: 2\nguid: " + guid + "\n", new UTF8Encoding(false));
                    }
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    material = Require<Material>(path);
                }
                ConfigureMaterial(material, File.ReadAllText(original), name);
                replacements[ReadGuid(original)] = material;
                replacements[AssetDatabase.AssetPathToGUID(path)] = material;
            }
            ConfigureClip();
            foreach (string path in ProjectilePaths.Concat(new[] { ImpactPath })) RepairPrefab(path, replacements);
            AssetDatabase.SaveAssets();
            ValidateImportedAssets();
            Debug.Log("Wisp presentation imported: 3 variants, 16 isolated materials, source appearance clip and shot/loop/hit audio. " +
                "Screen refraction uses local noise distortion; CartoonCoffee alpha tint uses AllIn1 alpha outline.");
        }

        private static void CopyDependency(string source, string relative)
        {
            string from = Path.Combine(source, relative);
            string guid = ReadGuid(from);
            string existing = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(existing)) return;
            string target = OutputFolder + "/" + relative;
            if (File.Exists(target)) throw new InvalidDataException("Unregistered dependency already exists: " + target);
            EnsureFolder(Path.GetDirectoryName(target));
            File.Copy(from, target);
            File.Copy(from + ".meta", target + ".meta");
        }

        private static void ConfigureMaterial(Material material, string yaml, string name)
        {
            bool cartoon = yaml.Contains("guid: ddc0a7b0e4084284c859e167cee4d130") ||
                           yaml.Contains("guid: 6950bdc7bd6d2644ab9ae800371622e6");
            bool distortion = name == "Distortion_0.mat";
            bool builtin = yaml.Contains("fileID: 211,");
            material.shader = Require<Shader>(distortion || builtin ? VfxShader : SpriteShader);
            string[] keywords = Regex.Matches(yaml, @"(?m)^  - ([A-Z0-9_]+)\s*$")
                .Cast<Match>().Select(m => m.Groups[1].Value).Where(k => !k.StartsWith("_")).ToArray();
            material.shaderKeywords = cartoon || distortion || builtin ? Array.Empty<string>() : keywords;
            float sourceBlend = Number(yaml, "_MySrcMode", Number(yaml, "_SrcBlend", 5f));
            float destinationBlend = Number(yaml, "_MyDstMode", Number(yaml, "_DstBlend", name.Contains("-Add-") ? 1f : 10f));
            material.SetFloat(distortion || builtin ? "_SrcMode" : "_MySrcMode", sourceBlend);
            material.SetFloat(distortion || builtin ? "_DstMode" : "_MyDstMode", destinationBlend);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Alpha", Number(yaml, "_Alpha", 1f));
            material.SetColor("_Color", ReadColor(yaml, cartoon ? "_TintColor" : "_Color", Color.white));
            material.renderQueue = 3000;
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetShaderPassEnabled("SRPDefaultUnlit", true);
            if (builtin) material.SetColor("_ShapeColor", Color.white);
            if (cartoon && Number(yaml, "_EnableAlphaTint", 0f) > 0f)
            {
                material.EnableKeyword("ALPHAOUTLINE_ON");
                material.SetColor("_AlphaOutlineColor", ReadColor(yaml, "_AlphaTintColor", Color.white));
                material.SetFloat("_AlphaOutlineGlow", 1f);
                material.SetFloat("_AlphaOutlinePower", Number(yaml, "_AlphaTintPower", 1f));
                material.SetFloat("_AlphaOutlineMinAlpha", Number(yaml, "_AlphaTintMinAlpha", 0f));
                material.SetFloat("_AlphaOutlineBlend", Number(yaml, "_AlphaTintFade", 1f));
            }
            if (distortion)
            {
                material.SetFloat("_Alpha", .15f);
                material.SetColor("_Color", new Color(.35f, .8f, 1f, 1f));
                material.SetColor("_ShapeColor", Color.white);
                material.SetTexture("_ShapeDistortTex", Require<Texture2D>(AssetDatabase.GUIDToAssetPath("7dba7aa575a8a9e49a5509c895599742")));
                material.SetFloat("_ShapeDistortAmount", .1f);
                material.EnableKeyword("SHAPE1DISTORT_ON");
            }
            EditorUtility.SetDirty(material);
        }

        private static void ConfigureClip()
        {
            AnimationClip clip = Require<AnimationClip>(ClipPath);
            // Unity stores material attribute hashes with flag bits. These three hashes were matched
            // against the source shader property names (CRC32 low 28 bits), not guessed by curve shape.
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                string property = binding.propertyName.Replace("path_0x8B19FAF2_uRjvinN", "_Alpha")
                    .Replace("path_0x80A0AD36_itqMpUL", "_AlphaOutlineBlend")
                    .Replace("path_0x84BAC14A_WJNirwN", "_AlphaOutlineMinAlpha");
                if (property == binding.propertyName) continue;
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                AnimationUtility.SetEditorCurve(clip, binding, null);
                var repaired = binding; repaired.propertyName = property;
                AnimationUtility.SetEditorCurve(clip, repaired, curve);
            }
            EditorUtility.SetDirty(clip);
        }

        private static void RepairPrefab(string path, Dictionary<string, Material> replacements)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] slots = renderer.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        if (slots[i] == null) continue;
                        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(slots[i]));
                        if (replacements.TryGetValue(guid, out Material material)) slots[i] = material;
                    }
                    renderer.sharedMaterials = slots;
                }
                foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component == null) continue;
                    string type = component.GetType().Name;
                    if (type == "StudioEventEmitter" || type == "StudioParameterTrigger" ||
                        AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(component))) == "cffbb4d90507e888f705c8dc76ba6e21")
                        Object.DestroyImmediate(component);
                }
                ProjectileAttack attack = root.GetComponent<ProjectileAttack>();
                if (attack != null)
                {
                    if (root.GetComponent<ProjectileVisualState>() == null) root.AddComponent<ProjectileVisualState>();
                    var so = new SerializedObject(attack);
                    SetRef(so, "progressionScaler", root.GetComponent<AttackProgressionScaler>());
                    SetRef(so, "hitbox", root.GetComponentInChildren<PlayerAttackHitBox>(true));
                    SetRef(so, "hitEffectResolver", root.GetComponentInChildren<SpawnableHitEffectResolver>(true));
                    SetRef(so, "particleSystem", root.GetComponent<ParticleSystem>());
                    SetRef(so, "animancer", root.GetComponent("AnimancerComponent"));
                    SetRef(so, "rotationTransform", root.transform.Find("Root"));
                    SetRef(so, "attackStartAnim._Clip", Require<AnimationClip>(ClipPath));
                    so.FindProperty("attackStartAnim._FadeDuration").floatValue = 0f;
                    so.FindProperty("attackStartAnim._Speed").floatValue = 1f;
                    so.FindProperty("attackStartAnim._NormalizedStartTime").floatValue = 0f;
                    so.FindProperty("attackStartAnimTransitionAfterFinish").boolValue = false;
                    so.FindProperty("playPiercingHitPresentation").boolValue = true;
                    ConfigureSound(so, "launchSound", "shot", new[] {305521848, 1139334325, -1555974261, -1191919773});
                    ConfigureSound(so, "projectileLoopSound", "loop", new[] {-450359305, 1330102086, -2065969494, -435797981});
                    ConfigureSound(so, "projectileHitSound", "hit", new[] {997541128, 1077009481, 1909684357, -832539066});
                    so.ApplyModifiedPropertiesWithoutUndo();
                    root.GetComponent<Animator>().cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }
                else
                {
                    var effect = root.GetComponent<AttackHitParticleEffect>();
                    Require(effect != null, "Missing particle impact component");
                    effect.system = root.GetComponent<ParticleSystem>();
                    effect.progressionScaler = root.GetComponent<AttackProgressionScaler>();
                    effect.hitbox = root.GetComponentInChildren<PlayerAttackHitBox>(true);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void ConfigureSound(SerializedObject so, string field, string suffix, int[] guid)
        {
            for (int i = 0; i < 4; i++) so.FindProperty(field + ".eventRef.Guid.Data" + (i + 1)).intValue = guid[i];
            so.FindProperty(field + ".eventRef.Path").stringValue = "event:/sx/plr/Sx_plr_slowprojectile_" + suffix;
            so.FindProperty(field + ".automatic").boolValue = true;
        }


        public static void ValidateImportedAssets()
        {
            AnimationClip clip = Require<AnimationClip>(ClipPath);
            Require(Mathf.Abs(clip.length - 1f / 3f) < .001f, "Source appearance clip duration changed");
            Require(!AnimationUtility.GetCurveBindings(clip).Any(b => b.propertyName.Contains("path_0x")), "Unresolved appearance curves");
            foreach (string path in ProjectilePaths.Concat(new[] { ImpactPath }))
            {
                GameObject root = Require<GameObject>(path);
                Require(root.GetComponentsInChildren<ParticleSystem>(true).Length == (path == ImpactPath ? 4 : 11), "Source particle layers changed: " + path);
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0, "Missing script: " + child.name);
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        // The source contains disabled container renderers with no material.
                        if (!renderer.enabled && material == null) continue;
                        Require(material != null && material.shader != null, "Missing material/shader: " + renderer.name);
                        Require(PlanarMaterialValidation.IsOriginalOrFaithfulPlanarCopy(material, OutputFolder + "/Material"), "Unexpected or altered shared material: " + renderer.name);
                        Require(material.mainTexture != null || renderer is SpriteRenderer, "Missing main texture: " + renderer.name);
                    }
                Require(!root.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c.GetType().Name == "StudioEventEmitter" || c.GetType().Name == "StudioParameterTrigger"), "Legacy audio chain remains");
                if (path == ImpactPath) continue;
                ProjectileAttack attack = root.GetComponent<ProjectileAttack>();
                var so = new SerializedObject(attack);
                Require(so.FindProperty("attackStartAnim._Clip").objectReferenceValue == clip && !attack.attackStartAnimTransitionAfterFinish, "Appearance must not delay firing");
                foreach (string field in new[] {"launchSound", "projectileLoopSound", "projectileHitSound"})
                    Require(so.FindProperty(field + ".automatic").boolValue && so.FindProperty(field + ".eventRef.Guid.Data1").intValue != 0, "Missing projectile sound: " + field);
            }
            Debug.Log("Wisp presentation dependencies validated.");
        }

        private static string ReadGuid(string path) => Regex.Match(File.ReadAllText(path + ".meta"), @"(?m)^guid: ([0-9a-f]+)").Groups[1].Value;
        private static void SetRef(SerializedObject so, string property, Object value)
        { Require(value != null, "Missing reference: " + property); so.FindProperty(property).objectReferenceValue = value; }
        private static T Require<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidDataException("Missing asset: " + path);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
        private static float Number(string yaml, string property, float fallback)
        {
            Match m = Regex.Match(yaml, @"(?m)^\s+" + Regex.Escape(property) + @": ([-+.0-9Ee]+)\s*$");
            return m.Success ? float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : fallback;
        }
        private static Color ReadColor(string yaml, string property, Color fallback)
        {
            Match m = Regex.Match(yaml, @"(?m)^\s+" + Regex.Escape(property) + @": \{r: ([^,]+), g: ([^,]+), b: ([^,]+), a: ([^}]+)\}");
            if (!m.Success) return fallback;
            return new Color(float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), float.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture));
        }
        private static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
