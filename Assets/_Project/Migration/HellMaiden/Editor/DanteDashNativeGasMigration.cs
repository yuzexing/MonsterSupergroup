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
    /// <summary>Imports ID 8's original two trail variants; gameplay lifecycle is adapted separately.</summary>
    public static class DanteDashNativeGasMigration
    {
        public const string OutputFolder = "Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Dash";
        public const string WeaponPath = OutputFolder + "/MonoBehaviour/WeaponData_Dante_FireTrail.asset";
        public const string BehaviourPath = OutputFolder + "/GameObject/Dante_FireTrail_Behaviour.prefab";
        public const string FireAttackPath = OutputFolder + "/GameObject/FireTrailAttack.prefab";
        public const string PoisonAttackPath = OutputFolder + "/GameObject/FireTrailAttack_Poison.prefab";
        public const string FireParticlesPath = OutputFolder + "/GameObject/FIRE.prefab";
        public const string PoisonParticlesPath = OutputFolder + "/GameObject/FIRE_Poison Variant.prefab";
        public const string RestorationLimits =
            "FireTrail attack fields and particle references survive in the source; no animation references or " +
            "progression scalers are invented. Sampling is movement-driven, not AnimationClip-driven. Source edge " +
            "collider radius/points/non-trigger flag and all particle curves are retained. Runtime Init replaces the " +
            "serialized 0.5-second hit interval with Weapon.Speed; source ID 8 therefore uses one second. Source " +
            "Duration is the sampling window; default point life is Duration/2, independently of particle visual tails. " +
            "Installed AllIn1 shaders replace incomplete exports: custom lighting/pixel-size autoscaling and " +
            "Rubfish gradients/noise are not restored. CartoonCoffee ember alpha tint uses its original RGB as a " +
            "flat fallback, without the missing alpha-dependent tint/UV distortion. Source FMOD event is retained; " +
            "its bank and the source card icon asset are unavailable. Native attack and network validation run separately.";
        private const string SourceDefault = "F:/DecomplieLatest/HellMaiden/ExportedProject";
        private const string SpriteShaderPath = "Assets/Plugins/AllIn1SpriteShader/Shaders/AllIn1SpriteShader.shader";
        private const string VfxShaderPath = "Assets/Plugins/AllIn1VfxToolkit/Shaders/AllIn1VfxURPCompat.shader";
        private const string KnockbackGuid = "fa1ccdc3a358d964ba88355c94961878";
        private const string ObsoleteParticleHelperGuid = "cffbb4d90507e888f705c8dc76ba6e21";
        private const string MovementScriptGuid = "f97ba996c164226802e15ae90f263e96";

        // Complete audited content closure; reuse already imported GUIDs instead of making duplicates.
        // No source scripts, shaders, Ovid data, animation framework, or old database is imported.
        private static readonly string[] SourceAssets =
        {
            "GameObject/Dante_FireTrail_Behaviour.prefab", "GameObject/FIRE.prefab",
            "GameObject/FIRE_Poison Variant.prefab", "GameObject/FireTrailAttack.prefab",
            "GameObject/FireTrailAttack_Poison.prefab",
            "Material/CartoonASCoffee_Flame_010-Basic-_1.0_ 1.mat",
            "Material/CartoonCoffedddde_Flame_004-Basic-_1.0_AS 1.mat",
            "Material/CartoonCoffedddde_Flame_004-Basic-_1.0_AS.mat",
            "Material/CartoonCoffedddde_Flame_004-Basic-_1.0_AS_0.mat",
            "Material/CartoonCoffee_Embers_001-Basic-_1.0__1.mat",
            "Material/FX_MT_FireSparks_03.mat", "Material/FX_MT_Fireground_01 1.mat",
            "Material/FX_MT_Fireground_01AS.mat", "Material/FX_MT_Fireground_03_GreenAS.mat",
            "Mesh/FX_MS_GroundCurved_01.asset",
            "MonoBehaviour/KnockBack_None.asset", "MonoBehaviour/WeaponData_Dante_FireTrail.asset",
            "Texture2D/Embers_001_0.png", "Texture2D/FX_TX_Fire_Fireloop_01_4x4.png",
            "Texture2D/FX_TX_Fire_GroundFire_02_4x5.png", "Texture2D/FX_TX_Fire_SmallFire_01_4x4.png",
            "Texture2D/FX_TX_SparksFire_01_Composition_0.png", "Texture2D/Flame_010_Comp_001.png",
            "Texture2D/Flame_010_Comp_001_Mask_001.png", "Texture2D/Noise 1_0.png",
            "Texture2D/fire2_1.png", "Texture2D/palette-downwell_1.png", "Texture2D/rainbow_1.png",
            "Texture2D/seamlessNoise_1.png", "Texture2D/white_1.png"
        };

        [MenuItem("Tools/HellMaiden Migration/Import Dante Dash Native GAS Assets")]
        public static void Import()
        {
            string sourceRoot = Environment.GetEnvironmentVariable("HELLMAIDEN_SOURCE_PROJECT");
            if (string.IsNullOrWhiteSpace(sourceRoot)) sourceRoot = SourceDefault;
            sourceRoot = Path.GetFullPath(sourceRoot);
            Require(Directory.Exists(Path.Combine(sourceRoot, "Assets")), "HellMaiden source project is missing: " + sourceRoot);
            string spriteGuid = RequireGuid(SpriteShaderPath);
            string vfxGuid = RequireGuid(VfxShaderPath);
            RestoreDashMovementConfiguration(sourceRoot);
            foreach (string path in SourceAssets) CopyIfMissing(sourceRoot, path, spriteGuid, vfxGuid);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureWeapon();
            ConfigureFallbackMaterials();
            ConfigureDatabase();
            AssetDatabase.SaveAssets();
            ValidateImportedAssets();
            Debug.Log("Dante FireTrail ID 8 assets imported; existing database entries/default weapon preserved. " + RestorationLimits);
        }

        [MenuItem("Tools/HellMaiden Migration/Restore Source Player Dash Movement Configuration")]
        public static void RestoreDashMovementConfiguration()
        {
            string sourceRoot = Environment.GetEnvironmentVariable("HELLMAIDEN_SOURCE_PROJECT");
            if (string.IsNullOrWhiteSpace(sourceRoot)) sourceRoot = SourceDefault;
            RestoreDashMovementConfiguration(Path.GetFullPath(sourceRoot));
        }

        private static void RestoreDashMovementConfiguration(string sourceRoot)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve Unity project root.");
            string[] sourceLayers = ReadLayers(Path.Combine(sourceRoot, "ProjectSettings/TagManager.asset"));
            string[] targetLayers = ReadLayers(Path.Combine(projectRoot, "ProjectSettings/TagManager.asset"));
            Require(sourceLayers.Length == 32 && sourceLayers.SequenceEqual(targetLayers),
                "Source and target physics layer indices differ; review the Dash masks before restoring them.");
            string source = File.ReadAllText(Path.Combine(sourceRoot, "Assets/Scenes/Game Scenes/Systems.unity"));
            string targetPath = Path.Combine(projectRoot, DanteNativeGasMigration.NetworkPlayerPrefabPath);
            string target = File.ReadAllText(targetPath);
            Match sourceBlock = DashMovementBlock(source);
            Match targetBlock = DashMovementBlock(target);
            // Copy the authored YAML, including both keys' tangents, tangent/weight modes and weights.
            // This leaves every other PlayerMovement field and every other prefab component untouched.
            string newline = target.Contains("\r\n") ? "\r\n" : "\n";
            string restoredBlock = sourceBlock.Value.Replace("\r\n", "\n").Replace("\n", newline);
            if (targetBlock.Value == restoredBlock) return;
            string restored = target.Substring(0, targetBlock.Index) + restoredBlock +
                target.Substring(targetBlock.Index + targetBlock.Length);
            File.WriteAllText(targetPath, restored, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(DanteNativeGasMigration.NetworkPlayerPrefabPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static string[] ReadLayers(string path)
        {
            Match block = Regex.Match(File.ReadAllText(path), @"(?ms)^  layers:\r?\n(?<layers>(?:  -[^\r\n]*\r?\n)+)");
            Require(block.Success, "Could not read physics layers: " + path);
            return Regex.Matches(block.Groups["layers"].Value, @"(?m)^  -([^\r\n]*)")
                .Cast<Match>().Select(match => match.Groups[1].Value.Trim()).ToArray();
        }

        private static Match DashMovementBlock(string yaml)
        {
            Match[] movements = Regex.Matches(yaml, @"(?m)^  m_Script: \{fileID: 11500000, guid: " + MovementScriptGuid + @", type: 3\}")
                .Cast<Match>().ToArray();
            Require(movements.Length == 1, "Expected exactly one PlayerMovement component before restoring Dash configuration.");
            int nextDocument = yaml.IndexOf("\n--- !u!", movements[0].Index, StringComparison.Ordinal);
            if (nextDocument < 0) nextDocument = yaml.Length;
            Match block = Regex.Match(yaml, @"(?ms)^  dashExclusionLayerMask:\r?\n.*?^  dashBufferTime: [^\r\n]*(?:\r?\n|$)");
            Require(block.Success && block.Index > movements[0].Index && block.Index + block.Length <= nextDocument + 1,
                "Could not locate the complete Dash configuration inside PlayerMovement.");
            foreach (string field in new[] { "dashCurve", "dashObstacleMargin", "obstacleLayerMask", "edgeLayerMask" })
                Require(block.Value.Contains("  " + field + ":"), "Missing source Dash field: " + field);
            return block;
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
                    relativePath == "MonoBehaviour/WeaponData_Dante_FireTrail.asset";
                Require(!owned || existing == destination, "Dash asset already has another canonical path: " + existing);
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
                    .Replace("159c7e9144365ce4590110a4cad76836", spriteGuid)
                    .Replace("e84ff329046efa64182fd8d9d6386fe4", spriteGuid)
                    .Replace("1b4d83d9c7b6216489851f955f0233ef", vfxGuid)
                    .Replace("ddc0a7b0e4084284c859e167cee4d130", vfxGuid);
                if (relativePath.EndsWith(".prefab", StringComparison.Ordinal)) yaml = RemoveObsoleteParticleHelpers(yaml);
                File.WriteAllText(destinationFile, yaml, new UTF8Encoding(false));
            }
            File.WriteAllText(destinationFile + ".meta", meta, new UTF8Encoding(false));
        }

        private static string RemoveObsoleteParticleHelpers(string yaml)
        {
            // The unavailable custom shader consumed these matrices. Keep all particle/renderer nodes.
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

        private static void ConfigureWeapon()
        {
            WeaponData weapon = RequireAsset<WeaponData>(WeaponPath);
            KnockbackSettings knockback = RequireAsset<KnockbackSettings>(AssetDatabase.GUIDToAssetPath(KnockbackGuid));
            Require(weapon.ID == 8 && knockback.distance == 0f, "Source FireTrail identity/knockback changed.");
            var presentation = new WeaponPresentationSettings();
            presentation.Configure(new CameraShakeSettings(), 0.1f, knockback);
            weapon.ConfigureNativeGas(new AttackStats
            {
                damage = 9, critRate = 0.04f, critMultiplier = 1.3f, speed = 1f, size = 1f,
                duration = 1.25f, projectileCount = 1, knockbackDistance = 0f,
                damageType = MonsterSupergroup.GAS.DamageType.Normal
            }, CombatTags.Attack, presentation);
            weapon.WeaponPrefab = RequireAsset<GameObject>(BehaviourPath).GetComponent<DashAttackBehaviour>();
            EditorUtility.SetDirty(weapon);
        }

        private static void ConfigureFallbackMaterials()
        {
            // Circling already imports this same sparks GUID. Only configure it here if Dash owns it.
            ConfigureVfxMaterial("Material/FX_MT_FireSparks_03.mat", "52ddf8f8ef713e14cba057c107c4a1cc", Color.white, Color.white);
            ConfigureVfxMaterial("Material/CartoonCoffee_Embers_001-Basic-_1.0__1.mat", "40e55b4def4b8954491e433f5df3c4cc",
                new Color(2.9960785f, 0.8480729f, 0f, 1f), new Color(1f, 1f, 1f, 0.5f));
        }

        private static void ConfigureVfxMaterial(string relativePath, string textureGuid, Color shapeColor, Color tint)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(OutputFolder + "/" + relativePath);
            if (material == null) return;
            material.shader = RequireAsset<Shader>(VfxShaderPath);
            material.SetTexture("_MainTex", RequireAsset<Texture2D>(AssetDatabase.GUIDToAssetPath(textureGuid)));
            material.SetColor("_ShapeColor", shapeColor);
            material.SetColor("_Color", tint);
            material.SetFloat("_Alpha", 1f);
            material.SetFloat("_SrcMode", 5f);
            material.SetFloat("_DstMode", 10f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_CullingOption", 0f);
            material.renderQueue = 3000;
            EditorUtility.SetDirty(material);
        }

        private static void ConfigureDatabase()
        {
            WeaponDB database = RequireAsset<WeaponDB>(DanteNativeGasMigration.NativeWeaponDatabasePath);
            var entries = database.Weapons.ToList();
            WeaponData weapon = RequireAsset<WeaponData>(WeaponPath);
            Require(entries.All(entry => entry != null), "Existing native database contains a null weapon.");
            Require(entries.Count(entry => entry.ID == 8) <= 1 && entries.All(entry => entry.ID != 8 || entry == weapon),
                "Another weapon already owns ID 8.");
            if (!entries.Contains(weapon)) { entries.Add(weapon); database.Configure(entries.ToArray()); EditorUtility.SetDirty(database); }
        }

        [MenuItem("Tools/HellMaiden Migration/Validate Dante Dash Native GAS Assets")]
        public static void ValidateImportedAssets()
        {
            WeaponData weapon = RequireAsset<WeaponData>(WeaponPath);
            weapon.ValidateNativeGas();
            Require(weapon.ID == 8 && weapon.BaseStats.damage == 9 && weapon.AttackTags == CombatTags.Attack,
                "FireTrail identity/damage/tags changed.");
            Require(weapon.WeaponPrefab is DashAttackBehaviour, "Dash emitter reference is missing.");
            var emitter = new SerializedObject(weapon.WeaponPrefab);
            Require(Required(emitter, "variants.defaultPrefab").objectReferenceValue ==
                RequireAsset<GameObject>(FireAttackPath).GetComponent<MultiParticlePlayerTrailAttack>(), "Default trail reference is missing.");
            Require(Required(emitter, "variants.poisonPrefab").objectReferenceValue ==
                RequireAsset<GameObject>(PoisonAttackPath).GetComponent<MultiParticlePlayerTrailAttack>(), "Poison trail reference is missing.");
            Require(Required(emitter, "variants.firePrefab").objectReferenceValue == null &&
                !Required(emitter, "variants.allowFire").boolValue && Required(emitter, "variants.allowPoison").boolValue,
                "Source trail variant permissions changed.");
            ValidateAttack(FireAttackPath, FireParticlesPath);
            ValidateAttack(PoisonAttackPath, PoisonParticlesPath);
            foreach (string path in new[] { BehaviourPath, FireAttackPath, PoisonAttackPath, FireParticlesPath, PoisonParticlesPath })
            {
                GameObject root = RequireAsset<GameObject>(path);
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0, "Missing script: " + child.name);
                ValidatePresentationDependencies(root, path);
            }
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

        [MenuItem("Tools/HellMaiden Migration/Diagnose Dante Dash Presentation Dependencies")]
        public static void DiagnosePresentationDependencies()
        {
            foreach (string assetPath in new[] { FireAttackPath, PoisonAttackPath, FireParticlesPath, PoisonParticlesPath })
            {
                GameObject root = RequireAsset<GameObject>(assetPath);
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    string path = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
                    Material[] materials = renderer.sharedMaterials;
                    for (int slot = 0; slot < materials.Length; slot++)
                    {
                        Material material = materials[slot];
                        Debug.Log($"Dash prefab={assetPath} renderer={(path.Length == 0 ? "<root>" : path)} " +
                            $"type={renderer.GetType().Name} enabled={renderer.enabled} slot={slot} " +
                            $"material={(material == null ? "<null>" : AssetDatabase.GetAssetPath(material))} " +
                            $"shader={(material == null || material.shader == null ? "<null>" : material.shader.name)}");
                    }
                }
                ValidatePresentationDependencies(root, assetPath);
            }
            Debug.Log("Dash presentation dependencies validated; only the four authored disabled particle-container slots are null.");
        }

        private static void ValidatePresentationDependencies(GameObject root, string assetPath)
        {
            ParticleSystemRenderer containerRenderer = null;
            if (assetPath == FireAttackPath || assetPath == PoisonAttackPath ||
                assetPath == FireParticlesPath || assetPath == PoisonParticlesPath)
            {
                // Source attack renderer 199589667821646929 (Fire_Trail_Small) and segment renderer
                // 199061268324235159 (<root>) have one null slot, disabled rendering and disabled emission.
                // The same authored structure is present in both variants. No other null slot is allowed.
                Transform container = assetPath == FireAttackPath || assetPath == PoisonAttackPath
                    ? root.transform.Find("Fire_Trail_Small") : root.transform;
                Require(container != null, "Source particle container is missing: " + assetPath);
                containerRenderer = container.GetComponent<ParticleSystemRenderer>();
                ParticleSystem particles = container.GetComponent<ParticleSystem>();
                Require(containerRenderer != null && !containerRenderer.enabled && particles != null &&
                    !particles.emission.enabled && containerRenderer.sharedMaterials.Length == 1 &&
                    containerRenderer.sharedMaterials[0] == null, "Source disabled particle-container configuration changed: " + assetPath);
            }
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == containerRenderer) continue;
                string relativePath = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
                string path = assetPath + " :: " + (relativePath.Length == 0 ? "<root>" : relativePath);
                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material material = materials[slot];
                    Require(material != null, $"Dash renderer '{path}' ({renderer.GetType().Name}) material slot {slot} is missing.");
                    Require(material.shader != null && material.shader.name != "Hidden/InternalErrorShader",
                        $"Dash renderer '{path}' material slot {slot} '{AssetDatabase.GetAssetPath(material)}' has a missing/error shader.");
                }
            }
        }

        private static void ValidateAttack(string path, string particlesPath)
        {
            GameObject root = RequireAsset<GameObject>(path);
            var attack = root.GetComponent<MultiParticlePlayerTrailAttack>();
            Require(attack != null && attack.hitbox is PlayerAttackOvertimeHitBox, "Trail overtime hitbox is missing.");
            var serialized = new SerializedObject(attack);
            Require(Required(serialized, "trailParticles").objectReferenceValue == RequireAsset<GameObject>(particlesPath).GetComponent<ParticleSystem>(),
                "Source trail particle prefab reference is missing.");
            Require(Required(serialized, "trailStepParticles").objectReferenceValue == root.transform.Find("Fire_Trail_Small").GetComponent<ParticleSystem>(),
                "Source attached step particle reference is missing.");
            EdgeCollider2D edge = root.GetComponent<EdgeCollider2D>();
            Require(Required(serialized, "edgeCollider").objectReferenceValue == edge && edge != null && edge.edgeRadius == 0.5f,
                "Source edge collider reference/radius changed.");
            Require(Required(serialized, "trailDelta").floatValue == 1f && attack.progressionScaler == null,
                "Source trail spacing/no-scaler configuration changed.");
            var hitbox = new SerializedObject(attack.hitbox);
            Require(Required(hitbox, "hitInterval").floatValue == 0.5f && Required(hitbox, "timeoutAfterExit").floatValue == 0.3f,
                "Serialized source hitbox defaults changed; runtime Speed overrides the tick interval.");
        }

        private static SerializedProperty Required(SerializedObject target, string name) =>
            target.FindProperty(name) ?? throw new InvalidDataException("Missing serialized property: " + name);
        private static T RequireAsset<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidDataException("Missing asset: " + path);
        private static string RequireGuid(string path)
        { string guid = AssetDatabase.AssetPathToGUID(path); Require(guid.Length == 32, "Missing package dependency: " + path); return guid; }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); }
    }
}
