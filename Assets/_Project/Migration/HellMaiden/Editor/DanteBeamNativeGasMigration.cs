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
    /// <summary>Imports the ID 3 beam and its two source variants without changing the weapon database.</summary>
    public static class DanteBeamNativeGasMigration
    {
        public const string OutputFolder = "Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Beam";
        public const string WeaponPath = OutputFolder + "/MonoBehaviour/WeaponData_Dante_DragonsBreath.asset";
        public const string BehaviourPath = OutputFolder + "/GameObject/Dante_DragonsBreath_Behaviour.prefab";
        public const string FireAttackPath = OutputFolder + "/GameObject/PlayerAttack_Dante_DragonsBreath_Fire.prefab";
        public const string PoisonAttackPath = OutputFolder + "/GameObject/PlayerAttack_Dante_DragonsBreath_Poison.prefab";
        public const string StartClipPath = OutputFolder + "/AnimationClip/PlayerAttack_Dante_DragonsBreath_In.anim";
        public const string MainClipPath = OutputFolder + "/AnimationClip/PlayerAttack_Dante_DragonsBreath_Idle.anim";
        public const string EndClipPath = OutputFolder + "/AnimationClip/PlayerAttack_Dante_DragonsBreath_Out.anim";
        private const string SourceDefault = "F:/DecomplieLatest/HellMaiden/ExportedProject";
        private const string SpriteShaderPath = "Assets/Plugins/AllIn1SpriteShader/Shaders/AllIn1SpriteShader.shader";
        private const string VfxShaderPath = "Assets/Plugins/AllIn1VfxToolkit/Shaders/AllIn1VfxURPCompat.shader";
        private const string OvertimeScalerGuid = "de190b0c94c56a15ceb304d88098c94e";
        private const string KnockbackGuid = "fa1ccdc3a358d964ba88355c94961878";
        private const string ObsoleteParticleHelperGuid = "cffbb4d90507e888f705c8dc76ba6e21";

        // Audited transitive content dependencies only. Scripts, DLLs, databases, and incomplete
        // exported shaders are deliberately excluded. Shared content is reused by its source GUID.
        private static readonly string[] SourceAssets =
        {
            "AnimationClip/PlayerAttack_Dante_DragonsBreath_Idle.anim",
            "AnimationClip/PlayerAttack_Dante_DragonsBreath_In.anim",
            "AnimationClip/PlayerAttack_Dante_DragonsBreath_Out.anim",
            "GameObject/Dante_DragonsBreath_Behaviour.prefab",
            "GameObject/PlayerAttack_Dante_DragonsBreath_Fire.prefab",
            "GameObject/PlayerAttack_Dante_DragonsBreath_Poison.prefab",
            "Material/As_postorized2 1Transparent.mat",
            "Material/As_postorized2 1TransparentPoison.mat",
            "Material/As_postorized2.mat",
            "Material/As_postorizedPoison.mat",
            "Material/AsPoisonBubble_0.mat",
            "Material/Flameposterized.mat",
            "Material/FlameposterizedPoison.mat",
            "Material/FX_MT_FireSparks_04.mat",
            "Material/FX_MT_FirewallShape_02.mat",
            "Material/FX_MT_LateralFlameAngular_02AS_0.mat",
            "Material/FX_MT_LateralFlameAngular_0AS.mat",
            "Material/FX_MT_LateralFlameAngular_0ASPoison.mat",
            "Material/New Material 1_0.mat",
            "Material/PlayerAttack_Dante_Area_Letters.mat",
            "Material/PlayerAttack_Dante_DragonsBreath_ChargeBall.mat",
            "Material/PlayerAttack_Dante_DragonsBreath_ChargeBallPoison.mat",
            "Material/PlayerAttack_Dante_DragonsBreath_Circle.mat",
            "Material/PlayerAttack_Dante_DragonsBreath_CirclePoison.mat",
            "Material/Poison.mat",
            "Mesh/FX_MS_HeadCylinder.002.asset",
            "Mesh/FX_MS_HeadCylinder.004.asset",
            "Mesh/Plane.001_3.asset",
            "Mesh/Sphere_2.asset",
            "MonoBehaviour/KnockBack_None.asset",
            "MonoBehaviour/WeaponData_Dante_DragonsBreath.asset",
            "Sprite/Leterrs_0.asset", "Sprite/Leterrs_1.asset", "Sprite/Leterrs_10.asset",
            "Sprite/Leterrs_2.asset", "Sprite/Leterrs_3.asset", "Sprite/Leterrs_4.asset",
            "Sprite/Leterrs_5.asset", "Sprite/Leterrs_6.asset", "Sprite/Leterrs_7.asset",
            "Sprite/Leterrs_8.asset", "Sprite/Leterrs_9.asset", "Sprite/Mask_0.asset",
            "Texture2D/bubble_round3 1.png", "Texture2D/fire2_1.png",
            "Texture2D/FX_TX_Fire_DirectionalFlame_01_4x4 1.png",
            "Texture2D/FX_TX_Fire_DirectionalFlame_01_4x4.png",
            "Texture2D/FX_TX_Fire_DirectionalFlame_03_4x4 1.png",
            "Texture2D/FX_TX_Fire_FirewallLateral_01_5x5_Poison.png",
            "Texture2D/FX_TX_Fire_FirewallLateral_01_5x5_VertexOffset.png",
            "Texture2D/FX_TX_Fire_FirewallLateral_01_5x5.png",
            "Texture2D/FX_TX_Fire_FirewallLateral_01_5x5Gradient.png",
            "Texture2D/FX_TX_Fire_FlameThrower_01_3x5_2 1.png",
            "Texture2D/FX_TX_Fire_FlameThrower_01_3x5_2.png",
            "Texture2D/FX_TX_FireSparks_01.png", "Texture2D/FX_TX_GradientNecronomic_01.png",
            "Texture2D/Leterrs.png", "Texture2D/Mask.png", "Texture2D/palette-downwell_1.png",
            "Texture2D/PlayerAttack_Dante_Area_Letters.png",
            "Texture2D/PlayerAttack_Dante_DragonsBreath_ChargeBall 1.png",
            "Texture2D/PlayerAttack_Dante_DragonsBreath_ChargeBall_4.png",
            "Texture2D/PlayerAttack_Dante_DragonsBreath_Circle 1.png",
            "Texture2D/PlayerAttack_Dante_DragonsBreath_Circle.png",
            "Texture2D/rainbow_1.png", "Texture2D/seamlessNoise_1.png", "Texture2D/white_1.png"
        };

        [MenuItem("Tools/HellMaiden Migration/Import Dante Beam Native GAS Assets")]
        public static void Import()
        {
            string sourceRoot = Environment.GetEnvironmentVariable("HELLMAIDEN_SOURCE_PROJECT");
            if (string.IsNullOrWhiteSpace(sourceRoot)) sourceRoot = SourceDefault;
            sourceRoot = Path.GetFullPath(sourceRoot);
            if (!Directory.Exists(Path.Combine(sourceRoot, "Assets")))
                throw new DirectoryNotFoundException("HellMaiden source project not found: " + sourceRoot);
            Require(!string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(OvertimeScalerGuid)),
                "Migrate PlayerAttackOvertimeHitBoxProgressionScaler with its source GUID before importing the beam.");

            string spriteGuid = RequireGuid(SpriteShaderPath);
            string vfxGuid = RequireGuid(VfxShaderPath);
            foreach (string relativePath in SourceAssets)
                CopyIfMissing(sourceRoot, relativePath, spriteGuid, vfxGuid);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            RepairAttackPrefab(FireAttackPath);
            RepairAttackPrefab(PoisonAttackPath);
            ConfigureWeapon();
            ConfigureRubfishMaterial("Material/FX_MT_FireSparks_04.mat", "e6cf79fab27385242989eecc3bacf996",
                new Color(1f, 0f, 0.010915279f, 1f));
            ConfigureRubfishMaterial("Material/Poison.mat", "e6cf79fab27385242989eecc3bacf996",
                new Color(0.76798105f, 0f, 1f, 1f));
            ConfigureRubfishMaterial("Material/FX_MT_FirewallShape_02.mat", "9cd57e5a16f2dce45a01094992342a95",
                new Color(0.021634102f, 1f, 0f, 1f));
            AssetDatabase.SaveAssets();
            ValidateImportedAssets();
            Debug.Log("Dante beam ID 3 imported; NativeGasWeaponDB was not modified. " +
                "Source AnimatedAttack fields are absent: start/main/end references and Duration-driven transitions " +
                "are explicitly reconstructed. All three source clips remain non-looping. " +
                "Idle retains two unresolved source path_0x curves. Installed AllIn1 shaders replace incomplete " +
                "exports; custom particle lighting, Rubfish gradients/noise/vertex offsets and screen distortion " +
                "are not restored. Source FMOD components and event IDs are retained; their bank is unavailable. " +
                "The source Addressables card icon GUID has no matching asset in the export.");
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
                // Dependency textures, sprites, meshes, materials and knockback settings may already
                // have a canonical project path. Only this migration's own prefabs/clips/weapon are edited.
                bool owned = relativePath.StartsWith("GameObject/", StringComparison.Ordinal) ||
                    relativePath.StartsWith("AnimationClip/", StringComparison.Ordinal) ||
                    relativePath == "MonoBehaviour/WeaponData_Dante_DragonsBreath.asset";
                Require(!owned || existing == destination, "Source beam asset already has a different path: " + existing);
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
                    .Replace("{fileID: -784021338, guid: 2183220b115a31f952f0e070568d769a, type: 3}",
                        "{fileID: 11500000, guid: ee158225ee1e59f4791627785501d950, type: 3}")
                    .Replace("{fileID: 796348501, guid: 841024f847a9ee126ccc5cbf660ee0d4, type: 3}",
                        "{fileID: 11500000, guid: 073797afb82c5a1438f328866b10b3f0, type: 3}")
                    .Replace("159c7e9144365ce4590110a4cad76836", spriteGuid)
                    .Replace("1b4d83d9c7b6216489851f955f0233ef", vfxGuid)
                    .Replace("670de5b32343134449f334f63815680d", vfxGuid)
                    .Replace("e5b830ba40ef36b4b953fe9c192097da", spriteGuid);
                if (relativePath.EndsWith(".prefab", StringComparison.Ordinal))
                    yaml = RemoveObsoleteParticleHelpers(yaml);
                File.WriteAllText(destinationFile, yaml, new UTF8Encoding(false));
            }
            File.WriteAllText(destinationFile + ".meta", meta, new UTF8Encoding(false));
        }

        private static string RemoveObsoleteParticleHelpers(string yaml)
        {
            // This helper supplied matrices only to the absent custom shader. Retain every
            // ParticleSystem, renderer, animation target, material and transform in the export.
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

        private static void RepairAttackPrefab(string path)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                AnimatedAttack attack = root.GetComponent<AnimatedAttack>()
                    ?? throw new InvalidDataException("Beam has no AnimatedAttack: " + path);
                Transform visualRoot = root.transform.Find("Root")
                    ?? throw new InvalidDataException("Beam has no Root transform: " + path);
                Component animancer = visualRoot.GetComponent("AnimancerComponent")
                    ?? throw new InvalidDataException("Beam has no AnimancerComponent: " + path);
                Animator animator = visualRoot.GetComponent<Animator>()
                    ?? throw new InvalidDataException("Beam has no Animator: " + path);
                var serialized = new SerializedObject(attack);
                Reference(serialized, "progressionScaler", root.GetComponent<AttackProgressionScaler>());
                Reference(serialized, "hitbox", root.GetComponentInChildren<PlayerAttackOvertimeHitBox>(true));
                Reference(serialized, "animancer", animancer);
                Reference(serialized, "rotationTransform", visualRoot);
                // The export lost ALL AnimatedAttack fields. Defaults below come from its source
                // class; clip roles follow the matching In/Idle/Out paths and collider curves.
                Required(serialized, "isometricRotation").boolValue = true;
                Required(serialized, "isometricAngle").floatValue = -45f;
                Required(serialized, "rotateInY").boolValue = false;
                ConfigureClip(serialized, "attackStartAnim", StartClipPath);
                ConfigureClip(serialized, "attackAnim", MainClipPath);
                ConfigureClip(serialized, "attackEndAnim", EndClipPath);
                // Reconstruction, not recovered source flags: Beam.Attack passes Duration to the
                // main phase timeout. It must progress after In and hold Idle until that timeout,
                // even if an upgraded Duration exceeds the source's non-looping four-second clip.
                Required(serialized, "attackStartAnimTransitionAfterFinish").boolValue = true;
                Required(serialized, "attackAnimTransitionAfterFinish").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var serializedAnimancer = new SerializedObject(animancer);
                Reference(serializedAnimancer, "_Animator", animator);
                serializedAnimancer.ApplyModifiedPropertiesWithoutUndo();
                animator.runtimeAnimatorController = null;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                PrefabUtility.SaveAsPrefabAsset(root, path);
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
            Require(weapon.ID == 3 && knockback.distance == 0f, "Source beam ID or zero knockback changed.");
            var presentation = new WeaponPresentationSettings();
            presentation.Configure(new CameraShakeSettings(), 0.1f, knockback);
            weapon.ConfigureNativeGas(new AttackStats
            {
                damage = 5, critRate = 0.03f, critMultiplier = 1.3f,
                speed = 3f, size = 1f, duration = 2f, projectileCount = 1,
                knockbackDistance = 0f, damageType = MonsterSupergroup.GAS.DamageType.Normal
            }, CombatTags.Attack, presentation);
            weapon.WeaponPrefab = RequireAsset<GameObject>(BehaviourPath).GetComponent<PlayerBeamAttackBehaviour>();
            EditorUtility.SetDirty(weapon);
        }

        private static void ConfigureRubfishMaterial(string relativePath, string textureGuid, Color shapeColor)
        {
            string path = OutputFolder + "/" + relativePath;
            // Never mutate a shared material that was already imported by another migration.
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) return;
            material.shader = RequireAsset<Shader>(VfxShaderPath);
            material.SetTexture("_MainTex", RequireAsset<Texture2D>(AssetDatabase.GUIDToAssetPath(textureGuid)));
            material.SetColor("_Color", Color.white);
            // Source _MainColor has alpha zero but is an RGB gradient input, not global opacity.
            material.SetColor("_ShapeColor", shapeColor);
            material.SetFloat("_Alpha", 1f);
            material.SetFloat("_SrcMode", 5f);
            material.SetFloat("_DstMode", 10f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_CullingOption", 0f);
            material.renderQueue = 3000;
            EditorUtility.SetDirty(material);
        }

        [MenuItem("Tools/HellMaiden Migration/Validate Dante Beam Native GAS Assets")]
        public static void ValidateImportedAssets()
        {
            WeaponData weapon = RequireAsset<WeaponData>(WeaponPath);
            weapon.ValidateNativeGas();
            Require(weapon.ID == 3 && weapon.BaseStats.damage == 5, "Beam identity/base damage changed.");
            Require(weapon.AttackTags == CombatTags.Attack, "Beam must not acquire projectile or status-damage semantics.");
            Require(weapon.WeaponPrefab is PlayerBeamAttackBehaviour, "Beam emitter reference is missing.");
            var emitter = new SerializedObject(weapon.WeaponPrefab);
            Require(Required(emitter, "variants.defaultPrefab").objectReferenceValue ==
                RequireAsset<GameObject>(FireAttackPath).GetComponent<AnimatedAttack>(), "Default beam must use the source Fire visual.");
            Require(Required(emitter, "variants.firePrefab").objectReferenceValue ==
                Required(emitter, "variants.defaultPrefab").objectReferenceValue, "Source Fire/default variant relation changed.");
            Require(Required(emitter, "variants.poisonPrefab").objectReferenceValue ==
                RequireAsset<GameObject>(PoisonAttackPath).GetComponent<AnimatedAttack>(), "Poison beam reference is missing.");
            Require(!Required(emitter, "allowMultipleAttacks").boolValue, "Source beam disables multiple attacks.");
            foreach (string path in new[] { FireAttackPath, PoisonAttackPath }) ValidateAttack(path);
            ValidateColliderCurve(StartClipPath, 0.36666667f, 0f);
            ValidateColliderCurve(MainClipPath, 4f, 1f);
            ValidateColliderCurve(EndClipPath, 1f, 0f);
        }

        private static void ValidateAttack(string path)
        {
            GameObject root = RequireAsset<GameObject>(path);
            AnimatedAttack attack = root.GetComponent<AnimatedAttack>();
            Require(attack != null && attack.hitbox is PlayerAttackOvertimeHitBox, "Beam overtime hitbox is missing: " + path);
            Require(attack.hitbox.GetComponent<PolygonCollider2D>() != null, "Source beam polygon collider is missing.");
            Require(attack.progressionScaler != null && attack.progressionScaler.speedCustomScalers.Count == 1 &&
                attack.progressionScaler.speedCustomScalers[0] != null, "Beam speed-to-tick progression is missing.");
            var serialized = new SerializedObject(attack);
            Require(Required(serialized, "animancer").objectReferenceValue != null &&
                attack.RotationTransform == root.transform.Find("Root"), "Beam animation/rotation reference is missing.");
            Require(Required(serialized, "attackStartAnim._Clip").objectReferenceValue == RequireAsset<AnimationClip>(StartClipPath) &&
                Required(serialized, "attackAnim._Clip").objectReferenceValue == RequireAsset<AnimationClip>(MainClipPath) &&
                Required(serialized, "attackEndAnim._Clip").objectReferenceValue == RequireAsset<AnimationClip>(EndClipPath),
                "Beam phase clip references are missing.");
            Require(attack.attackStartAnimTransitionAfterFinish && !attack.attackAnimTransitionAfterFinish,
                "Beam must enter the main phase and use the requested Duration timeout.");
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0,
                    "Missing script on beam child: " + child.name);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true).Where(value => value.enabled))
                foreach (Material material in renderer.sharedMaterials)
                    Require(material != null && material.shader != null && material.shader.name != "Hidden/InternalErrorShader",
                        "Missing beam material/shader: " + renderer.name);
        }

        private static void ValidateColliderCurve(string path, float duration, float value)
        {
            AnimationClip clip = RequireAsset<AnimationClip>(path);
            Require(Math.Abs(clip.length - duration) < 0.0001f && !clip.isLooping, "Source beam clip timing changed: " + path);
            EditorCurveBinding binding = AnimationUtility.GetCurveBindings(clip).Single(candidate =>
                candidate.path == "Scale/HitBox" && candidate.type == typeof(PolygonCollider2D) && candidate.propertyName == "m_Enabled");
            Require(AnimationUtility.GetEditorCurve(clip, binding).keys.All(key => key.value == value),
                "Source beam collider window changed: " + path);
        }

        private static SerializedProperty Required(SerializedObject target, string name) =>
            target.FindProperty(name) ?? throw new InvalidDataException("Missing serialized property: " + name);

        private static void Reference(SerializedObject target, string name, Object value)
        {
            Require(value != null, "Missing reference for " + name);
            Required(target, name).objectReferenceValue = value;
        }

        private static T RequireAsset<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidDataException("Missing asset: " + path);

        private static string RequireGuid(string path)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            Require(guid.Length == 32, "Missing package dependency: " + path);
            return guid;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }
}
