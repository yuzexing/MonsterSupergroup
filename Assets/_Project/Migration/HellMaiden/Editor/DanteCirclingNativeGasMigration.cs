using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AstralShift.HellMaiden.CameraFX;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player.Attacks;
using MonsterSupergroup.GAS;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using AttackStats = MonsterSupergroup.GAS.AttackStats;

namespace MonsterSupergroup.HellMaidenMigration.Editor
{
    /// <summary>Imports the original ID 6 orbiting spheres without importing a legacy runtime.</summary>
    public static class DanteCirclingNativeGasMigration
    {
        public const string OutputFolder = "Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Circling";
        public const string WeaponPath = OutputFolder + "/MonoBehaviour/WeaponData_Dante_Circling.asset";
        public const string BehaviourPath = OutputFolder + "/GameObject/Dante_Circling_Behaviour.prefab";
        public const string AttackPath = OutputFolder + "/GameObject/PlayerAttack_Dante_Circling.prefab";
        public const string StartClipPath = OutputFolder + "/AnimationClip/PlayerAttack_Dante_Circling_Show.anim";
        public const string MainClipPath = OutputFolder + "/AnimationClip/PlayerAttack_Dante_Circling_Idle.anim";
        public const string EndClipPath = OutputFolder + "/AnimationClip/PlayerAttack_Dante_Circling_Hide.anim";
        public const string RestorationLimits =
            "Source AnimatedAttack fields are absent: phase references and StartTransition=true/MainTransition=false " +
            "are explicitly reconstructed. Circling runtime controls Duration externally, including Show; Idle remains " +
            "a zero-length looping pose. Installed AllIn1 shaders replace incomplete exports: custom particle lighting, " +
            "pixel-size autoscaling, Rubfish color gradients/noise/trail controls and SSU shadow wiggle are not restored. " +
            "Rubfish gradient materials use their source HighColor and texture as a visible fallback; other Rubfish " +
            "materials use source MainColor RGB. The opaque black-background FireSparks_03 atlas uses AllIn1 " +
            "luminance-to-alpha as an explicit approximation of missing Rubfish opacity, preserving source alpha " +
            "blending and texture bytes. Source audio references/None triggers are preserved; no playback " +
            "timing is inferred and the bank is unavailable. The source card icon GUID has no asset in the export.";
        private const string SourceDefault = "F:/DecomplieLatest/HellMaiden/ExportedProject";
        private const string SpriteShaderPath = "Assets/Plugins/AllIn1SpriteShader/Shaders/AllIn1SpriteShader.shader";
        private const string VfxShaderPath = "Assets/Plugins/AllIn1VfxToolkit/Shaders/AllIn1VfxURPCompat.shader";
        private const string KnockbackGuid = "b059a95aaaaea704682bf0db9a416783";
        private const string ObsoleteParticleHelperGuid = "cffbb4d90507e888f705c8dc76ba6e21";
        private const string GradientShaderGuid = "1b4d83d9c7b6216489851f955f0233ef";
        private const string TrailShaderGuid = "2ed163af396190f4ea824ab6f944c1d0";
        private const string StandardShaderGuid = "f271282bee17ffa409cd9034eff63ee0";
        private const string OpaqueSparkTextureGuid = "52ddf8f8ef713e14cba057c107c4a1cc";

        // Audited complete content closure. Scripts, DLLs and incomplete shaders are resolved to
        // existing project dependencies. Source GUIDs preserve references and shared asset identity.
        private static readonly string[] SourceAssets =
        {
            "AnimationClip/PlayerAttack_Dante_Circling_Hide.anim",
            "AnimationClip/PlayerAttack_Dante_Circling_Idle.anim",
            "AnimationClip/PlayerAttack_Dante_Circling_Show.anim",
            "AnimatorController/PlayerAttack_Dante_Circling.controller",
            "GameObject/Dante_Circling_Behaviour.prefab", "GameObject/PlayerAttack_Dante_Circling.prefab",
            "Material/AS_BallAttack.mat", "Material/Ball_shader_0.mat",
            "Material/FX_MT_CausticTrails_01as 1.mat", "Material/FX_MT_CausticTrails_01as.mat",
            "Material/FX_MT_CausticTrails_02.mat", "Material/FX_MT_FireSparks_01_5.mat",
            "Material/FX_MT_FireSparks_03.mat", "Material/FX_MT_FireSparks_03as.mat",
            "Material/FX_MT_FlamesSimple_02_Gum 1AS.mat", "Material/FX_MT_FlamesSphere_Gum 123_0.mat",
            "Material/FX_MT_Flames_02_Gumas.mat", "Material/FX_MT_GoingFlame_01.mat",
            "Material/FX_MT_ImpactLens 1AS.mat", "Material/FX_MT_ImpactLight.mat",
            "Material/FX_MT_RadialFire_01_AS.mat", "Material/FX_MT_SimpleGlow_06.mat",
            "Material/Multiply 1_0.mat", "Material/PlayerAttack_Dante_Circling_Sphere_0.mat",
            "Mesh/Plane_3.asset", "Mesh/Sphere_2.asset",
            "MonoBehaviour/KnockBack_CirclingAttack.asset", "MonoBehaviour/WeaponData_Dante_Circling.asset",
            "Sprite/Projectile_shadpw.asset",
            "Texture2D/FX_TX_DenseNoise_01.png", "Texture2D/FX_TX_Fire_GroundTraveling_01_5x4_Gray.png",
            "Texture2D/FX_TX_Fire_GroundTraveling_01_5x4_Gray2.png",
            "Texture2D/FX_TX_Fire_RadialFire_01_3x4.png", "Texture2D/FX_TX_Fire_SimpleFlame_02_4x4.png",
            "Texture2D/FX_TX_Fire_SimpleFlame_03_4x4_Grayscale.png", "Texture2D/FX_TX_FlamesTrail_01.png",
            "Texture2D/FX_TX_GlowADD_01.png", "Texture2D/FX_TX_GradientSecondFire_02.png",
            "Texture2D/FX_TX_LensFlare_011.png", "Texture2D/FX_TX_LightBurst.png",
            "Texture2D/FX_TX_SilkTrail_01.png", "Texture2D/FX_TX_SimpleSpark_0.png",
            "Texture2D/FX_TX_SparksFire_01_Composition2.png", "Texture2D/FX_TX_SparksFire_01_Composition_0.png",
            "Texture2D/LightningSheet_1.png", "Texture2D/PlayerAttack_Dante_Circling_Eye.png",
            "Texture2D/PlayerAttack_Dante_Circling_Sphere.png", "Texture2D/Projectile_shadpw.png",
            "Texture2D/RainbowVertical_1.png", "Texture2D/SSU_Noise_1K_1.png",
            "Texture2D/fire2_1.png", "Texture2D/palette-downwell_1.png", "Texture2D/rainbow_1.png",
            "Texture2D/seamlessNoise_1.png", "Texture2D/white_1.png"
        };

        [MenuItem("Tools/HellMaiden Migration/Import Dante Circling Native GAS Assets")]
        public static void Import()
        {
            string sourceRoot = Environment.GetEnvironmentVariable("HELLMAIDEN_SOURCE_PROJECT");
            if (string.IsNullOrWhiteSpace(sourceRoot)) sourceRoot = SourceDefault;
            sourceRoot = Path.GetFullPath(sourceRoot);
            Require(Directory.Exists(Path.Combine(sourceRoot, "Assets")), "HellMaiden source project is missing: " + sourceRoot);
            string spriteGuid = RequireGuid(SpriteShaderPath);
            string vfxGuid = RequireGuid(VfxShaderPath);
            foreach (string relativePath in SourceAssets) CopyIfMissing(sourceRoot, relativePath, spriteGuid, vfxGuid);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            RepairAttackPrefab();
            ConfigureWeapon();
            ConfigureMaterials(sourceRoot);
            ConfigureDatabase();
            AssetDatabase.SaveAssets();
            ValidateImportedAssets();
            Debug.Log("Dante circling ID 6 imported; existing database entries and default weapon are preserved. " + RestorationLimits);
        }

        private static void CopyIfMissing(string sourceRoot, string relativePath, string spriteGuid, string vfxGuid)
        {
            string source = Path.Combine(sourceRoot, "Assets", relativePath);
            string meta = File.ReadAllText(source + ".meta");
            string guid = Regex.Match(meta, @"(?m)^guid: ([0-9a-f]{32})\s*$").Groups[1].Value;
            Require(guid.Length == 32, "Invalid source GUID: " + source);
            string destination = OutputFolder + "/" + relativePath;
            string existing = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(existing))
            {
                bool owned = relativePath.StartsWith("GameObject/", StringComparison.Ordinal) ||
                    relativePath.StartsWith("AnimationClip/", StringComparison.Ordinal) ||
                    relativePath == "MonoBehaviour/WeaponData_Dante_Circling.asset";
                Require(!owned || existing == destination, "Circling asset already has another canonical path: " + existing);
                return;
            }
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve Unity project root.");
            string destinationFile = Path.Combine(projectRoot, destination);
            if (File.Exists(destinationFile))
            {
                Require(File.Exists(destinationFile + ".meta") && File.ReadAllText(destinationFile + ".meta") == meta,
                    "Refusing to overwrite an existing asset: " + destination);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile));
            if (relativePath.EndsWith(".png", StringComparison.Ordinal)) File.Copy(source, destinationFile);
            else
            {
                string yaml = File.ReadAllText(source)
                    .Replace("{fileID: -536417829, guid: a764a8f1aa53ec5f484d2a941db13b66, type: 3}",
                        "{fileID: 11500000, guid: 0ad50f81b1d25c441943c37a89ba23f6, type: 3}")
                    .Replace("159c7e9144365ce4590110a4cad76836", spriteGuid)
                    .Replace("d892932001015e24db884166029606dc", spriteGuid)
                    .Replace(GradientShaderGuid, vfxGuid).Replace(TrailShaderGuid, vfxGuid).Replace(StandardShaderGuid, vfxGuid);
                if (relativePath.EndsWith(".prefab", StringComparison.Ordinal)) yaml = RemoveObsoleteParticleHelpers(yaml);
                File.WriteAllText(destinationFile, yaml, new UTF8Encoding(false));
            }
            File.WriteAllText(destinationFile + ".meta", meta, new UTF8Encoding(false));
        }

        private static string RemoveObsoleteParticleHelpers(string yaml)
        {
            // These components only feed matrices to the unavailable custom shader; retain all visuals.
            var ids = new List<string>();
            yaml = Regex.Replace(yaml, @"(?ms)^--- !u!114 &(-?\d+)\r?\n.*?(?=^--- !u!|\z)", match =>
            {
                if (!match.Value.Contains(ObsoleteParticleHelperGuid)) return match.Value;
                ids.Add(match.Groups[1].Value);
                return string.Empty;
            });
            foreach (string id in ids)
                yaml = Regex.Replace(yaml, @"(?m)^  - component: \{fileID: " + Regex.Escape(id) + @"\}\r?\n", string.Empty);
            return yaml;
        }

        private static void RepairAttackPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(AttackPath);
            try
            {
                AnimatedAttack attack = root.GetComponent<AnimatedAttack>();
                Transform visualRoot = root.transform.Find("Root");
                Require(attack != null && visualRoot != null, "Source circling attack/root is missing.");
                Component animancer = visualRoot.GetComponent("AnimancerComponent");
                Animator animator = visualRoot.GetComponent<Animator>();
                Require(animancer != null && animator != null, "Source circling Animancer/Animator is missing.");
                var serialized = new SerializedObject(attack);
                Reference(serialized, "progressionScaler", root.GetComponent<AttackProgressionScaler>());
                Reference(serialized, "hitbox", root.GetComponentInChildren<PlayerAttackOvertimeHitBox>(true));
                Reference(serialized, "animancer", animancer);
                Reference(serialized, "rotationTransform", visualRoot);
                // The original component lost all fields. Circling does not call directional Attack;
                // its emitter supplies the authored +45 degree orbit plane. Preserve the orb's zero rotation.
                Required(serialized, "isometricRotation").boolValue = false;
                ConfigureClip(serialized, "attackStartAnim", StartClipPath);
                ConfigureClip(serialized, "attackAnim", MainClipPath);
                ConfigureClip(serialized, "attackEndAnim", EndClipPath);
                // Explicit reconstruction: Show enters the held zero-length Idle; the orbit runtime
                // starts Hide after the external Duration clock, not after an animation timeout.
                Required(serialized, "attackStartAnimTransitionAfterFinish").boolValue = true;
                Required(serialized, "attackAnimTransitionAfterFinish").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var animation = new SerializedObject(animancer);
                Reference(animation, "_Animator", animator);
                animation.ApplyModifiedPropertiesWithoutUndo();
                animator.runtimeAnimatorController = null;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                PrefabUtility.SaveAsPrefabAsset(root, AttackPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void ConfigureClip(SerializedObject attack, string field, string path)
        {
            Required(attack, field + "._Clip").objectReferenceValue = RequireAsset<AnimationClip>(path);
            Required(attack, field + "._FadeDuration").floatValue = 0f;
            Required(attack, field + "._Speed").floatValue = 1f;
            Required(attack, field + "._NormalizedStartTime").floatValue = 0f;
        }

        private static void ConfigureWeapon()
        {
            WeaponData weapon = RequireAsset<WeaponData>(WeaponPath);
            KnockbackSettings knockback = RequireAsset<KnockbackSettings>(AssetDatabase.GUIDToAssetPath(KnockbackGuid));
            Require(weapon.ID == 6 && Mathf.Approximately(knockback.distance, 0.2f), "Source circling ID/knockback changed.");
            var presentation = new WeaponPresentationSettings();
            presentation.Configure(new CameraShakeSettings(), 0.1f, knockback);
            weapon.ConfigureNativeGas(new AttackStats
            {
                damage = 12, critRate = 0.05f, critMultiplier = 1.4f, speed = 1f, size = 1.1f,
                duration = 3.14f, projectileCount = 2, knockbackDistance = 0.2f,
                damageType = MonsterSupergroup.GAS.DamageType.Normal
            }, CombatTags.Attack, presentation);
            weapon.WeaponPrefab = RequireAsset<GameObject>(BehaviourPath).GetComponent<CirclingAttackBehaviour>();
            EditorUtility.SetDirty(weapon);
        }

        private static void ConfigureMaterials(string sourceRoot)
        {
            foreach (string path in SourceAssets.Where(value => value.EndsWith(".mat", StringComparison.Ordinal)))
            {
                // A shared source GUID may already have an owner elsewhere; never change that owner's material.
                Material material = AssetDatabase.LoadAssetAtPath<Material>(OutputFolder + "/" + path);
                if (material == null) continue;
                string source = File.ReadAllText(Path.Combine(sourceRoot, "Assets", path));
                bool gradient = source.Contains("guid: " + GradientShaderGuid);
                if (gradient || source.Contains("guid: " + TrailShaderGuid) || source.Contains("guid: " + StandardShaderGuid))
                {
                    string textureGuid = Regex.Match(source, @"_MainTexture:\s+m_Texture: \{fileID: 2800000, guid: ([0-9a-f]{32})")
                        .Groups[1].Value;
                    Require(textureGuid.Length == 32, "Rubfish main texture is missing: " + path);
                    // These are RGB gradient endpoints, not opacity. Using HighColor for a gradient
                    // keeps its source texture visible (ImpactLight's MainColor is black); it is a lossy fallback.
                    string property = gradient ? "_HighColor" : "_MainColor";
                    Match color = Regex.Match(source, Regex.Escape(property) + @": \{r: ([^,]+), g: ([^,]+), b: ([^,]+), a: [^}]+\}");
                    Require(color.Success, "Rubfish source color is missing: " + path);
                    float Read(int index) => float.Parse(color.Groups[index].Value, System.Globalization.CultureInfo.InvariantCulture);
                    material.shader = RequireAsset<Shader>(VfxShaderPath);
                    material.SetTexture("_MainTex", RequireAsset<Texture2D>(AssetDatabase.GUIDToAssetPath(textureGuid)));
                    material.SetColor("_ShapeColor", new Color(Read(1), Read(2), Read(3), 1f));
                    material.SetColor("_Color", Color.white);
                    material.SetFloat("_Alpha", 1f);
                    material.SetFloat("_SrcMode", 5f);
                    material.SetFloat("_DstMode", path.Contains("SimpleGlow") || path.Contains("ImpactLight") ? 1f : 10f);
                    material.SetFloat("_ZWrite", 0f);
                    material.SetFloat("_CullingOption", 0f);
                    material.renderQueue = 3000;
                    if (path == "Material/FX_MT_FireSparks_03.mat")
                    {
                        Require(textureGuid == OpaqueSparkTextureGuid && gradient,
                            "The audited opaque black-background spark atlas changed.");
                        // This original atlas has alpha=1 even in its black background. Rubfish's
                        // lost opacity shader cannot be reconstructed exactly. AllIn1's existing
                        // luminance-to-alpha feature keeps black pixels transparent with the
                        // original SrcAlpha/OneMinusSrcAlpha blend, without editing the texture.
                        // Materials with authored texture alpha and additive glows do not need it.
                        material.EnableKeyword("PREMULTIPLYCOLOR_ON");
                    }
                }
                else if (path == "Material/Multiply 1_0.mat")
                {
                    // Reconstruct alpha-aware multiply with the existing shader. The original SSU
                    // wiggle implementation is unavailable; the SpriteRenderer supplies its original texture.
                    material.shader = RequireAsset<Shader>(SpriteShaderPath);
                    material.shaderKeywords = new[] { "PREMULTIPLYALPHA_ON" };
                    material.SetFloat("_MySrcMode", 2f); // DstColor.
                    material.SetFloat("_MyDstMode", 10f); // OneMinusSrcAlpha.
                    material.SetFloat("_Brightness", 0f); // SSU's neutral 1 is additive in AllIn1.
                    material.SetFloat("_Contrast", 1f);
                    material.SetFloat("_Alpha", 1f);
                    material.SetFloat("_ZWrite", 0f);
                }
                EditorUtility.SetDirty(material);
            }
        }

        private static void ConfigureDatabase()
        {
            WeaponDB database = RequireAsset<WeaponDB>(DanteNativeGasMigration.NativeWeaponDatabasePath);
            var entries = database.Weapons.ToList();
            WeaponData weapon = RequireAsset<WeaponData>(WeaponPath);
            Require(entries.All(entry => entry != null), "Existing native database contains a null weapon.");
            Require(entries.Count(entry => entry.ID == 6) <= 1 && entries.All(entry => entry.ID != 6 || entry == weapon),
                "Another weapon already owns ID 6.");
            if (!entries.Contains(weapon)) { entries.Add(weapon); database.Configure(entries.ToArray()); EditorUtility.SetDirty(database); }
        }

        [MenuItem("Tools/HellMaiden Migration/Validate Dante Circling Native GAS Assets")]
        public static void ValidateImportedAssets()
        {
            WeaponData weapon = RequireAsset<WeaponData>(WeaponPath);
            weapon.ValidateNativeGas();
            Require(weapon.ID == 6 && weapon.BaseStats.damage == 12 && weapon.AttackTags == CombatTags.Attack,
                "Circling identity/damage/tags changed.");
            var emitter = weapon.WeaponPrefab as CirclingAttackBehaviour;
            GameObject root = RequireAsset<GameObject>(AttackPath);
            AnimatedAttack attack = root.GetComponent<AnimatedAttack>();
            Require(emitter != null && emitter.attackPrefab == attack && emitter.baseRadius == 2f && emitter.baseSpeed == 2f,
                "Circling emitter configuration is missing.");
            Require(attack != null && attack.hitbox is PlayerAttackOvertimeHitBox && attack.progressionScaler != null,
                "Circling hitbox/scaler references are missing.");
            Require(attack.hitbox.GetComponent<CircleCollider2D>()?.radius == 1f, "Source circle collider is missing.");
            Require(attack.progressionScaler.sizeTransforms.Count == 4 && attack.progressionScaler.speedCustomScalers.Count == 0,
                "Circling must retain only its source Size scaling; Speed changes orbit velocity, not the hit interval.");
            Require(attack.attackStartAnimTransitionAfterFinish && !attack.attackAnimTransitionAfterFinish,
                "Externally timed circling phase configuration changed.");
            var serialized = new SerializedObject(attack);
            Require(Required(serialized, "animancer").objectReferenceValue != null && attack.RotationTransform == root.transform.Find("Root"),
                "Circling animation references are missing.");
            ValidateClip(StartClipPath, 0.016666668f, false, 1f);
            ValidateClip(MainClipPath, 0f, true, 1f);
            ValidateClip(EndClipPath, 0.8f, false, 0f);
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0, "Missing script: " + child.name);
            ValidatePresentationDependencies(root);
            Material sparks = root.transform.Find("Root/Scale/Main/BurstSparks")
                .GetComponent<ParticleSystemRenderer>().sharedMaterial;
            Require(sparks.IsKeywordEnabled("PREMULTIPLYCOLOR_ON") &&
                sparks.GetFloat("_SrcMode") == 5f && sparks.GetFloat("_DstMode") == 10f &&
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sparks.GetTexture("_MainTex"))) == OpaqueSparkTextureGuid,
                "BurstSparks requires the original opaque atlas and its explicit luminance-to-alpha fallback; otherwise black billboards appear.");
            foreach (string path in AssetDatabase.FindAssets(string.Empty, new[] { OutputFolder }).Select(AssetDatabase.GUIDToAssetPath))
            {
                if (AssetDatabase.IsValidFolder(path) || path.EndsWith(".png", StringComparison.Ordinal)) continue;
                foreach (Match reference in Regex.Matches(File.ReadAllText(path), @"guid: ([0-9a-f]{32})"))
                {
                    string guid = reference.Groups[1].Value;
                    Require(guid.StartsWith("0000000000000000", StringComparison.Ordinal) ||
                        !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)), "Unresolved GUID " + guid + " in " + path);
                }
            }
        }

        [MenuItem("Tools/HellMaiden Migration/Diagnose Dante Circling Presentation Dependencies")]
        public static void DiagnosePresentationDependencies()
        {
            GameObject root = RequireAsset<GameObject>(AttackPath);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material material = materials[slot];
                    Debug.Log($"Circling renderer={path} type={renderer.GetType().Name} enabled={renderer.enabled} " +
                        $"slot={slot} material={(material == null ? "<null>" : AssetDatabase.GetAssetPath(material))} " +
                        $"shader={(material == null || material.shader == null ? "<null>" : material.shader.name)}");
                }
            }
            ValidatePresentationDependencies(root);
            Debug.Log("Circling presentation dependencies validated; the source Root particle container retains its disabled null-material slot.");
        }

        private static void ValidatePresentationDependencies(GameObject root)
        {
            // Source renderer 199733206282342372 is a particle container: disabled renderer,
            // disabled emission, exactly one null material. This is authored data, not a lost dependency.
            Transform container = root.transform.Find("Root");
            var containerRenderer = container.GetComponent<ParticleSystemRenderer>();
            ParticleSystem containerParticles = container.GetComponent<ParticleSystem>();
            Require(containerRenderer != null && !containerRenderer.enabled && containerParticles != null &&
                !containerParticles.emission.enabled && containerRenderer.sharedMaterials.Length == 1 &&
                containerRenderer.sharedMaterials[0] == null, "Source Root particle-container configuration changed.");
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == containerRenderer) continue;
                string path = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material material = materials[slot];
                    Require(material != null, $"Circling renderer '{path}' ({renderer.GetType().Name}) material slot {slot} is missing.");
                    Require(material.shader != null && material.shader.name != "Hidden/InternalErrorShader",
                        $"Circling renderer '{path}' material slot {slot} '{AssetDatabase.GetAssetPath(material)}' has a missing/error shader.");
                }
            }
        }

        private static void ValidateClip(string path, float length, bool loop, float colliderValue)
        {
            AnimationClip clip = RequireAsset<AnimationClip>(path);
            Require(Mathf.Abs(clip.length - length) < 0.0001f && clip.isLooping == loop, "Source circling timing changed: " + path);
            EditorCurveBinding binding = AnimationUtility.GetCurveBindings(clip).Single(candidate =>
                candidate.path == "Scale/HitBox" && candidate.type == typeof(CircleCollider2D) && candidate.propertyName == "m_Enabled");
            Require(AnimationUtility.GetEditorCurve(clip, binding).keys.All(key => key.value == colliderValue), "Source circling hit window changed.");
        }

        private static SerializedProperty Required(SerializedObject target, string name) =>
            target.FindProperty(name) ?? throw new InvalidDataException("Missing serialized property: " + name);
        private static void Reference(SerializedObject target, string name, Object value)
        { Require(value != null, "Missing reference for " + name); Required(target, name).objectReferenceValue = value; }
        private static T RequireAsset<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidDataException("Missing asset: " + path);
        private static string RequireGuid(string path)
        { string guid = AssetDatabase.AssetPathToGUID(path); Require(guid.Length == 32, "Missing package dependency: " + path); return guid; }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); }
    }
}
