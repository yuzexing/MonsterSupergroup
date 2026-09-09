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
    /// <summary>Imports only the original ID 1 slash and its presentation dependencies.</summary>
    public static class DanteMeleeNativeGasMigration
    {
        public const string OutputFolder =
            "Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Melee";
        public const string WeaponPath = OutputFolder + "/MonoBehaviour/WeaponData_Dante_Melee.asset";
        public const string BehaviourPath = OutputFolder + "/GameObject/Dante_Slash_Behaviour.prefab";
        public const string AttackPath = OutputFolder + "/GameObject/PlayerAttack_Dante_Slash.prefab";
        public const string ClipPath = OutputFolder + "/AnimationClip/PlayerAttack_Dante_Slash_Idle_0.anim";
        private const string KnockbackPath = OutputFolder + "/MonoBehaviour/KnockBack_MeleeAttack.asset";
        private const string SourceDefault = "F:/DecomplieLatest/HellMaiden/ExportedProject";
        private const string ObsoleteParticleHelperGuid = "cffbb4d90507e888f705c8dc76ba6e21";
        private const string SpriteShaderPath = "Assets/Plugins/AllIn1SpriteShader/Shaders/AllIn1SpriteShader.shader";
        private const string VfxShaderPath = "Assets/Plugins/AllIn1VfxToolkit/Shaders/AllIn1VfxURPCompat.shader";

        // This deliberately excludes all source scripts, DLLs, shaders, Ultimate and databases.
        // Existing dependencies with the same source GUID are reused wherever they currently live.
        private static readonly string[] SourceAssets =
        {
            "Texture2D/palette-downwell_1.png", "Texture2D/seamlessNoise_1.png",
            "Texture2D/white_1.png", "Texture2D/rainbow_1.png", "Texture2D/fire2_1.png",
            "Texture2D/Leters copy_0.png", "Texture2D/Leters copyblack_0.png",
            "Texture2D/FX_TX_Fire_FireSlash_02_6x23_0.png",
            "Texture2D/FX_TX_FlamesNoise_01_0.png",
            "Texture2D/FX_TX_Fire_FireSlash_02_6x2as_0.png",
            "Texture2D/FX_MT_GlowAlpha_01_0.png",
            "Texture2D/FX_TX_Fire_FireSlash_02_6x2as2_0.png",
            "Texture2D/HMD_Asset_Quill_Texture_0.png",
            "Mesh/FX_MS_FlatCylinder_DeformedUV_01_1.asset",
            "Mesh/FX_MS_FlatCylinder_DeformedUV_01_2.asset", "Mesh/FountainPen_0.asset",
            "Material/ASFX_MT_FireSparks_01 1_0.mat", "Material/ASFX_MT_SlashFire_01 1_0.mat",
            "Material/GlowASFX_MT_SlashFire_01 2_0.mat", "Material/ASDARKFX_MT_SlashFire_01 1_0.mat",
            "Material/M_FountainPen_0.mat", "Material/FX_MT_SlashFire_01_0.mat",
            "Material/Slash_text 1_0.mat",
            "AnimationClip/PlayerAttack_Dante_Slash_Idle_0.anim",
            "AnimatorController/PlayerAttack_Dante_Slash_0.controller",
            "MonoBehaviour/KnockBack_MeleeAttack.asset",
            "GameObject/PlayerAttack_Dante_Slash.prefab", "GameObject/Dante_Slash_Behaviour.prefab",
            "MonoBehaviour/WeaponData_Dante_Melee.asset"
        };

        [MenuItem("Tools/HellMaiden Migration/Import Dante Melee Native GAS Assets")]
        public static void Import()
        {
            string sourceRoot = Environment.GetEnvironmentVariable("HELLMAIDEN_SOURCE_PROJECT");
            if (string.IsNullOrWhiteSpace(sourceRoot)) sourceRoot = SourceDefault;
            sourceRoot = Path.GetFullPath(sourceRoot);
            if (!Directory.Exists(Path.Combine(sourceRoot, "Assets")))
                throw new DirectoryNotFoundException("HellMaiden source project not found: " + sourceRoot);

            string spriteGuid = RequireGuid(SpriteShaderPath);
            string vfxGuid = RequireGuid(VfxShaderPath);
            foreach (string relativePath in SourceAssets)
                CopyIfMissing(sourceRoot, relativePath, spriteGuid, vfxGuid);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            RepairSlashPrefab();
            ConfigureWeapon();
            ConfigureRubfishMaterial();
            AssetDatabase.SaveAssets();
            ValidateImportedAssets();
            Debug.Log("Dante melee ID 1 imported and validated; NativeGasWeaponDB was not modified. " +
                      "Original particles, meshes, clip and collider curves are preserved. " +
                      "The two exported shaders are incomplete: installed AllIn1 shaders are used, " +
                      "without the old custom particle-scale/light response or Rubfish noise/color lerp. " +
                      "The source Addressables icon GUID has no asset in the export; Ultimate is not migrated.");
        }

        private static void CopyIfMissing(string sourceRoot, string relativePath, string spriteGuid, string vfxGuid)
        {
            string source = Path.Combine(sourceRoot, "Assets", relativePath);
            string meta = File.ReadAllText(source + ".meta");
            string guid = Regex.Match(meta, @"(?m)^guid: ([0-9a-f]{32})\s*$").Groups[1].Value;
            if (guid.Length != 32) throw new InvalidDataException("Invalid source GUID: " + source);
            string destination = OutputFolder + "/" + relativePath;
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve the Unity project root.");
            string destinationFile = Path.Combine(projectRoot, destination);
            string existing = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(existing))
            {
                // Do not overwrite an existing shared texture or move it to a second canonical path.
                if (existing != destination && relativePath.StartsWith("Texture2D/", StringComparison.Ordinal)) return;
                if (existing != destination)
                    throw new InvalidOperationException("Source asset already has a different canonical path: " + existing);
                return;
            }
            if (File.Exists(destinationFile))
            {
                if (!File.Exists(destinationFile + ".meta") || File.ReadAllText(destinationFile + ".meta") != meta)
                    throw new InvalidOperationException("Refusing to overwrite an existing asset: " + destination);
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
                    .Replace("42ede0d402ae8ad4089cf6d4f78e44ce", vfxGuid);
                if (relativePath.EndsWith(".prefab", StringComparison.Ordinal))
                    yaml = RemoveObsoleteParticleHelpers(yaml);
                if (relativePath == "MonoBehaviour/WeaponData_Dante_Melee.asset")
                    yaml = Regex.Replace(yaml, @"(?m)^  ultimateData:.*$", "  ultimateData: {fileID: 0}");
                File.WriteAllText(destinationFile, yaml, new UTF8Encoding(false));
            }
            File.WriteAllText(destinationFile + ".meta", meta, new UTF8Encoding(false));
        }

        private static string RemoveObsoleteParticleHelpers(string yaml)
        {
            // This helper only supplied matrices to AllIn1CustomUrp2dRenderer, which is not imported.
            // Remove only that component, keeping every particle, renderer and transform untouched.
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

        private static void RepairSlashPrefab()
        {
            AnimationClip clip = RequireAsset<AnimationClip>(ClipPath);
            GameObject root = PrefabUtility.LoadPrefabContents(AttackPath);
            try
            {
                AnimatedAttack attack = root.GetComponent<AnimatedAttack>()
                    ?? throw new InvalidDataException("Slash has no AnimatedAttack.");
                Transform visualRoot = root.transform.Find("Root")
                    ?? throw new InvalidDataException("Slash has no Root transform.");
                Component animancer = visualRoot.GetComponent("AnimancerComponent")
                    ?? throw new InvalidDataException("Slash has no AnimancerComponent on Root.");
                Animator animator = visualRoot.GetComponent<Animator>()
                    ?? throw new InvalidDataException("Slash has no Animator on Root.");
                var serialized = new SerializedObject(attack);
                Reference(serialized, "progressionScaler", root.GetComponent<AttackProgressionScaler>());
                Reference(serialized, "hitbox", root.GetComponentInChildren<PlayerAttackHitBox>(true));
                Reference(serialized, "animancer", animancer);
                Reference(serialized, "rotationTransform", visualRoot);
                Required(serialized, "isometricRotation").boolValue = true;
                Required(serialized, "isometricAngle").floatValue = -45f;
                Required(serialized, "rotateInY").boolValue = false;
                Required(serialized, "attackAnim._Clip").objectReferenceValue = clip;
                Required(serialized, "attackAnim._FadeDuration").floatValue = 0f;
                Required(serialized, "attackAnim._Speed").floatValue = 1f;
                Required(serialized, "attackAnim._NormalizedStartTime").floatValue = 0f;
                Required(serialized, "attackAnimTransitionAfterFinish").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var serializedAnimancer = new SerializedObject(animancer);
                Reference(serializedAnimancer, "_Animator", animator);
                serializedAnimancer.ApplyModifiedPropertiesWithoutUndo();

                // Animancer owns clip playback; the source controller remains an imported reference artifact.
                animator.runtimeAnimatorController = null;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                PrefabUtility.SaveAsPrefabAsset(root, AttackPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void ConfigureWeapon()
        {
            WeaponData weapon = RequireAsset<WeaponData>(WeaponPath);
            KnockbackSettings knockback = RequireAsset<KnockbackSettings>(KnockbackPath);
            if (weapon.ID != 1 || Math.Abs(knockback.distance - 0.6f) > 0.0001f)
                throw new InvalidDataException("Source melee identity or knockback no longer matches the audited export.");
            var presentation = new WeaponPresentationSettings();
            presentation.Configure(new CameraShakeSettings(), 0.1f, knockback);
            weapon.ConfigureNativeGas(new AttackStats
            {
                damage = 19, critRate = 0.07f, critMultiplier = 1.3f,
                speed = 1f, size = 1f, duration = 1f, projectileCount = 1,
                knockbackDistance = knockback.distance, damageType = MonsterSupergroup.GAS.DamageType.Normal
            }, CombatTags.Attack, presentation);
            weapon.WeaponPrefab = RequireAsset<GameObject>(BehaviourPath).GetComponent<MeleeAttackBehaviour>();
            var serialized = new SerializedObject(weapon);
            Required(serialized, "ultimateData").objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(weapon);
        }

        private static void ConfigureRubfishMaterial()
        {
            Material material = RequireAsset<Material>(OutputFolder + "/Material/FX_MT_SlashFire_01_0.mat");
            material.shader = RequireAsset<Shader>(VfxShaderPath);
            // Only properties with a direct meaning are translated. The original shader body is a white
            // placeholder, so its noise/emission/color interpolation cannot be faithfully reconstructed.
            material.SetTexture("_MainTex", RequireAsset<Texture2D>(OutputFolder + "/Texture2D/FX_TX_Fire_FireSlash_02_6x23_0.png"));
            material.SetColor("_Color", Color.white);
            material.SetColor("_ShapeColor", new Color(1f, 0.29948562f, 0f, 1f));
            material.SetFloat("_Alpha", 1f);
            material.SetFloat("_SrcMode", 5f);
            material.SetFloat("_DstMode", 10f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_CullingOption", 0f);
            material.renderQueue = 3000;
            EditorUtility.SetDirty(material);
        }

        [MenuItem("Tools/HellMaiden Migration/Validate Dante Melee Native GAS Assets")]
        public static void ValidateImportedAssets()
        {
            WeaponData weapon = RequireAsset<WeaponData>(WeaponPath);
            weapon.ValidateNativeGas();
            Require(weapon.ID == 1 && weapon.BaseStats.damage == 19, "Melee ID/base damage changed.");
            Require(weapon.AttackTags == CombatTags.Attack, "Melee must not carry Projectile semantics.");
            Require(weapon.WeaponPrefab is MeleeAttackBehaviour, "Melee emitter reference is missing.");
            var emitter = (MeleeAttackBehaviour)weapon.WeaponPrefab;
            Require(emitter.prefab != null, "Melee slash prefab reference is missing.");
            AnimatedAttack attack = emitter.prefab;
            Require(attack.hitbox is PlayerAttackHitBox, "Melee hitbox reference is missing.");
            Require(attack.progressionScaler != null && attack.progressionScaler.sizeTransforms.Count == 2,
                "Source size progression layers must be retained.");
            var serialized = new SerializedObject(attack);
            AnimationClip clip = RequireAsset<AnimationClip>(ClipPath);
            Require(Required(serialized, "animancer").objectReferenceValue != null, "Melee Animancer reference is missing.");
            Require(Required(serialized, "attackAnim._Clip").objectReferenceValue == clip, "Melee attack clip is missing.");
            Require(Required(serialized, "attackAnimTransitionAfterFinish").boolValue, "Melee clip must release the slash on completion.");
            Require(Math.Abs(clip.length - 2f / 3f) < 0.001f && !clip.isLooping, "Source slash duration/loop mode changed.");
            Require(AnimationUtility.GetCurveBindings(clip).Any(binding =>
                binding.path == "Scale/HitBox" && binding.propertyName == "m_Enabled" && binding.type == typeof(BoxCollider2D)),
                "Original slash collider timing curve is missing.");
            foreach (Transform child in attack.GetComponentsInChildren<Transform>(true))
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0,
                    "Missing script on slash child: " + child.name);
            foreach (Renderer renderer in attack.GetComponentsInChildren<Renderer>(true).Where(value => value.enabled))
                foreach (Material material in renderer.sharedMaterials)
                    Require(material != null && material.shader != null &&
                            material.shader.name != "Hidden/InternalErrorShader", "Missing slash material/shader: " + renderer.name);
        }

        private static SerializedProperty Required(SerializedObject target, string name) =>
            target.FindProperty(name) ?? throw new InvalidDataException("Missing serialized property: " + name);

        private static void Reference(SerializedObject target, string name, Object value)
        {
            if (value == null) throw new InvalidDataException("Missing object for " + name);
            Required(target, name).objectReferenceValue = value;
        }

        private static T RequireAsset<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidDataException("Missing asset: " + path);

        private static string RequireGuid(string path)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (guid.Length != 32) throw new InvalidDataException("Missing package dependency: " + path);
            return guid;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }
}
