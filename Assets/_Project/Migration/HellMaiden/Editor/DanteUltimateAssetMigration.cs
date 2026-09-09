using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AstralShift.HellMaiden.Data.Cards;
using AstralShift.HellMaiden.Player.Attacks;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.HellMaidenMigration.Editor
{
    /// <summary>Imports the original Ultimate definition and attack resources; never assigns a Native ability ID.</summary>
    public static class DanteUltimateAssetMigration
    {
        public const string OutputFolder = "Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Ultimate";
        public const string DefinitionPath = OutputFolder + "/MonoBehaviour/UltimateData_Dante.asset";
        public const string MainAttackPath = OutputFolder + "/GameObject/Ultimate AttackDante.prefab";
        public const string WaveAttackPath = OutputFolder + "/GameObject/Fire circle_Ultimate.prefab";
        public const string MainClipPath = OutputFolder + "/AnimationClip/DanteUltimate_BaseAnim.anim";
        public const string IdleClipPath = OutputFolder + "/AnimationClip/DanteUltimate_BaseAnimIddle.anim";
        public const string WaveClipPath = OutputFolder + "/AnimationClip/DanteUltimateWave.anim";
        public const string MainControllerPath = OutputFolder + "/AnimatorController/Ultimate AttackDante.controller";
        public const string WaveControllerPath = OutputFolder + "/AnimatorController/DanteUltimateRingController.controller";
        public const string DeferredUiPrefabGuid = "623267e54be9d434fbbb5d9ce9fed4eb";
        public const string SourceGlowMaterialGuid = "e4a17982fe63da8409d82b905adf382b";
        public const string AdaptedGlowMaterialGuid = "11d05dbb87d44c20a38de779738fedaa";
        public const string RestorationLimits =
            "UltimateData retains legacy ID=0 and its source stats; this import creates no WeaponData, modifies no " +
            "database, and assigns no Native/network ability ID. The ultimateAttackEvents reference originally points " +
            "to DanteUltimateAttackAnimations (GUID 623267e54be9d434fbbb5d9ce9fed4eb); it is explicitly cleared because " +
            "that UI/video flow is deferred. Main and wave runtime fields/references survive intact and require no " +
            "invented defaults. The original main controller, wave controller, all three clips, UnscaledTime animators, " +
            "SpawnWave/ShakeCamera/onAttackAnimationEnd events, light nodes and 428 wave particle systems are retained. " +
            "Missing old URP DLL clip references map to the installed Light2D script. Three undecoded exporter curve " +
            "properties remain unchanged: material.path_0x8B19FAF2_uRjvinN, script_0xDAB5983F_WRNotnJ and " +
            "script_0xADB2A8A9_qoIJtjL; their exact behavior is not recovered. Existing AllIn1 shaders replace the " +
            "incomplete custom shader; custom lighting/pixel autoscale and SSU effects are unavailable. Source SSU " +
            "ADD material retains its texture/color via alpha-aware multiply, following its Multiplicative shader; " +
            "the built-in glow particle material retains its source alpha/additive blend with the installed VFX shader. " +
            "That glow material has an Ultimate-local adapted copy because its original GUID is already used by a " +
            "shared built-in material; existing shared resources are unchanged. The source PausableParticleSystem " +
            "script GUID maps to the already migrated class GUID. " +
            "TimelineEffects and PausableParticleSystem remain for explicit Native/local-presentation adaptation; " +
            "the import does not activate global camera, pause, loot, invulnerability or legacy damage behavior.";

        private const string SourceDefault = "F:/DecomplieLatest/HellMaiden/ExportedProject";
        private const string SpriteShaderPath = "Assets/Plugins/AllIn1SpriteShader/Shaders/AllIn1SpriteShader.shader";
        private const string VfxShaderPath = "Assets/Plugins/AllIn1VfxToolkit/Shaders/AllIn1VfxURPCompat.shader";
        private const string LightScriptPath = "Packages/com.unity.render-pipelines.universal/Runtime/2D/Light2D.cs";
        private const string PausableParticleScriptPath = "Assets/_Project/Gameplay/Combat/Helpers/PausableParticleSystem.cs";
        private const string ObsoleteParticleHelperGuid = "cffbb4d90507e888f705c8dc76ba6e21";

        // Only the attack closure. The definition's legacy UI/video prefab is deliberately outside this import.
        private static readonly string[] SourceAssets =
        {
            "AnimationClip/DanteUltimateWave.anim",
            "AnimationClip/DanteUltimate_BaseAnim.anim",
            "AnimationClip/DanteUltimate_BaseAnimIddle.anim",
            "AnimatorController/DanteUltimateRingController.controller",
            "AnimatorController/Ultimate AttackDante.controller",
            "GameObject/Fire circle_Ultimate.prefab",
            "GameObject/Ultimate AttackDante.prefab",
            "Material/3_1.mat",
            "Material/4_1.mat",
            "Material/ADD.mat",
            "Material/BarrierTrap_fire_ground_3x3_AB.mat",
            "Material/CartoonCoffee_Flame_007-Basic-_1.0_.mat",
            "Material/Circle_1.mat",
            "Material/FX_MT_Firetorch_Loop_02.mat",
            "Material/FX_MT_HeadLoop_05_0.mat",
            "Material/Fire_Particles_Fast.mat",
            "Material/Leters.mat",
            "Material/Leters2.mat",
            "Material/Small_Fires.mat",
            "Material/feather.mat",
            "Material/glow1_ADD_3.mat",
            "Mesh/Sphere_0.asset",
            "Mesh/Sphere_1.asset",
            "Mesh/Torus_0.asset",
            "Mesh/Torus_2.asset",
            "MonoBehaviour/KnockBack_None.asset",
            "MonoBehaviour/KnockBack_UltimateDante.asset",
            "MonoBehaviour/UltimateData_Dante.asset",
            "Sprite/Circle 1.asset",
            "Sprite/Circle_Ground_0_0.asset",
            "Sprite/sprite_sheet (1)_0_0.asset",
            "Sprite/sprite_sheet (1)_10_0.asset",
            "Sprite/sprite_sheet (1)_11_0.asset",
            "Sprite/sprite_sheet (1)_12_0.asset",
            "Sprite/sprite_sheet (1)_13_0.asset",
            "Sprite/sprite_sheet (1)_14_0.asset",
            "Sprite/sprite_sheet (1)_15_0.asset",
            "Sprite/sprite_sheet (1)_1_0.asset",
            "Sprite/sprite_sheet (1)_2_0.asset",
            "Sprite/sprite_sheet (1)_3_0.asset",
            "Sprite/sprite_sheet (1)_4_0.asset",
            "Sprite/sprite_sheet (1)_5_0.asset",
            "Sprite/sprite_sheet (1)_6_0.asset",
            "Sprite/sprite_sheet (1)_7_0.asset",
            "Sprite/sprite_sheet (1)_8_0.asset",
            "Sprite/sprite_sheet (1)_9_0.asset",
            "Texture2D/4_2.png",
            "Texture2D/Circle 1.png",
            "Texture2D/Circle 1e.png",
            "Texture2D/Circle_1.png",
            "Texture2D/Circle_Ground_1.png",
            "Texture2D/FX_TX_FireLoop_Alternative_01.png",
            "Texture2D/FX_TX_GradientFire_01 1.png",
            "Texture2D/Flame_007.png",
            "Texture2D/GradintBurn.png",
            "Texture2D/LightningSheet_1.png",
            "Texture2D/Philautia.png",
            "Texture2D/PlayerAttack_Area_Circle_Outer_MaskCircle.png",
            "Texture2D/PlayerAttack_Area_Circle_Outer_MaskCircle2.png",
            "Texture2D/RainbowVertical_1.png",
            "Texture2D/SSU_Noise_1K_1.png",
            "Texture2D/Xenia.png",
            "Texture2D/feathers_shape.png",
            "Texture2D/fire2 1.png",
            "Texture2D/fire2_1.png",
            "Texture2D/fire_ground_soft3x32.png",
            "Texture2D/glow1_1.png",
            "Texture2D/gradient21.png",
            "Texture2D/gradient21_0.png",
            "Texture2D/gradient2212_0.png",
            "Texture2D/gradient_2.png",
            "Texture2D/palette-downwell_1.png",
            "Texture2D/rainbow_1.png",
            "Texture2D/seamlessNoise_1.png",
            "Texture2D/sprite_sheet (1)_0.png",
            "Texture2D/white_1.png",
        };

        [MenuItem("Tools/HellMaiden Migration/Import Dante Ultimate Attack Assets")]
        public static void Import()
        {
            string sourceRoot = Environment.GetEnvironmentVariable("HELLMAIDEN_SOURCE_PROJECT");
            if (string.IsNullOrWhiteSpace(sourceRoot)) sourceRoot = SourceDefault;
            sourceRoot = Path.GetFullPath(sourceRoot);
            Require(Directory.Exists(Path.Combine(sourceRoot, "Assets")), "HellMaiden source project is missing: " + sourceRoot);
            string spriteGuid = RequireGuid(SpriteShaderPath);
            string lightGuid = RequireGuid(LightScriptPath);
            string pausableGuid = RequireGuid(PausableParticleScriptPath);
            foreach (string path in SourceAssets) CopyIfMissing(sourceRoot, path, spriteGuid, lightGuid, pausableGuid);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var definition = new SerializedObject(RequireAsset<UltimateData>(DefinitionPath));
            Required(definition, "ultimateAttackEvents").objectReferenceValue = null;
            definition.ApplyModifiedPropertiesWithoutUndo();
            RestoreSourceControllerMetadata();
            ConfigureMaterials();
            AssetDatabase.SaveAssets();
            ValidateImportedAssets();
            Debug.Log("Dante Ultimate source attack assets imported without database/ability-ID changes. " + RestorationLimits);
        }

        private static void CopyIfMissing(string sourceRoot, string relativePath, string spriteGuid, string lightGuid, string pausableGuid)
        {
            string source = Path.Combine(sourceRoot, "Assets", relativePath);
            string meta = File.ReadAllText(source + ".meta");
            if (relativePath == "Material/glow1_ADD_3.mat")
                meta = meta.Replace(SourceGlowMaterialGuid, AdaptedGlowMaterialGuid);
            string guid = Regex.Match(meta, @"(?m)^guid: ([0-9a-f]{32})\s*$").Groups[1].Value;
            Require(guid.Length == 32, "Invalid source GUID: " + source);
            string destination = OutputFolder + "/" + relativePath;
            string existing = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(existing))
            {
                bool owned = relativePath.StartsWith("GameObject/", StringComparison.Ordinal) ||
                    relativePath.StartsWith("AnimationClip/", StringComparison.Ordinal) ||
                    relativePath.StartsWith("AnimatorController/", StringComparison.Ordinal) ||
                    relativePath == "MonoBehaviour/UltimateData_Dante.asset" ||
                    relativePath == "Material/glow1_ADD_3.mat";
                Require(!owned || existing == destination, "Ultimate asset already has another canonical path: " + existing);
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
                    .Replace("d892932001015e24db884166029606dc", spriteGuid)
                    .Replace(SourceGlowMaterialGuid, AdaptedGlowMaterialGuid)
                    .Replace("492ccc46bed566f66e0d966a42f827c7", pausableGuid)
                    .Replace("{fileID: 796348501, guid: 841024f847a9ee126ccc5cbf660ee0d4, type: 3}",
                        "{fileID: 11500000, guid: " + lightGuid + ", type: 3}")
                    .Replace("073797afb82c5a1438f328866b10b3f0", lightGuid);
                if (relativePath.EndsWith(".prefab", StringComparison.Ordinal)) yaml = RemoveObsoleteParticleHelpers(yaml);
                if (relativePath == "MonoBehaviour/UltimateData_Dante.asset")
                    yaml = Regex.Replace(yaml, @"(?m)^  ultimateAttackEvents: \{fileID: \d+, guid: " + DeferredUiPrefabGuid + @", type: 3\}",
                        "  ultimateAttackEvents: {fileID: 0}");
                File.WriteAllText(destinationFile, yaml, new UTF8Encoding(false));
            }
            File.WriteAllText(destinationFile + ".meta", meta, new UTF8Encoding(false));
        }

        private static string RemoveObsoleteParticleHelpers(string yaml)
        {
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

        private static void ConfigureMaterials()
        {
            // Do not alter already imported shared material GUIDs.
            Material multiply = AssetDatabase.LoadAssetAtPath<Material>(OutputFolder + "/Material/ADD.mat");
            if (multiply != null)
            {
                multiply.shader = RequireAsset<Shader>(SpriteShaderPath);
                multiply.shaderKeywords = new[] { "PREMULTIPLYALPHA_ON" };
                multiply.SetFloat("_MySrcMode", 2f);
                multiply.SetFloat("_MyDstMode", 10f);
                multiply.SetFloat("_Brightness", 0f);
                multiply.SetFloat("_Contrast", 1f);
                multiply.SetFloat("_Alpha", 1f);
                multiply.SetFloat("_ZWrite", 0f);
                EditorUtility.SetDirty(multiply);
            }
            Material glow = AssetDatabase.LoadAssetAtPath<Material>(OutputFolder + "/Material/glow1_ADD_3.mat");
            if (glow != null)
            {
                glow.shader = RequireAsset<Shader>(VfxShaderPath);
                glow.shaderKeywords = Array.Empty<string>();
                glow.SetTexture("_MainTex", RequireAsset<Texture2D>(AssetDatabase.GUIDToAssetPath("3867ce58eaf7305489456ce9212b3068")));
                glow.SetColor("_ShapeColor", Color.white);
                glow.SetColor("_Color", Color.white);
                glow.SetFloat("_SrcMode", 5f);
                glow.SetFloat("_DstMode", 1f);
                glow.SetFloat("_Alpha", 1f);
                glow.SetFloat("_ZWrite", 0f);
                glow.SetFloat("_CullingOption", 0f);
                glow.renderQueue = 3000;
                EditorUtility.SetDirty(glow);
            }
        }

        private static void RestoreSourceControllerMetadata()
        {
            var controller = RequireAsset<AnimatorController>(MainControllerPath);
            AnimatorState idle = controller.layers[0].stateMachine.states.Single(state => state.state.name == "Idle").state;
            AnimatorStateTransition transition = idle.transitions.Single(value => value.destinationState != null && value.destinationState.name == "Attack");
            Require(transition.hasFixedDuration, "The audited Ultimate entry transition must use seconds.");
            GameObject root = PrefabUtility.LoadPrefabContents(MainAttackPath);
            try
            {
                var data = new SerializedObject(root.GetComponent<DanteUltimateAttack>());
                Required(data, "entryTransitionDuration").floatValue = transition.duration;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, MainAttackPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [MenuItem("Tools/HellMaiden Migration/Validate Dante Ultimate Attack Assets")]
        public static void ValidateImportedAssets()
        {
            UltimateData definition = RequireAsset<UltimateData>(DefinitionPath);
            var main = RequireAsset<GameObject>(MainAttackPath).GetComponent<DanteUltimateAttack>();
            var wave = RequireAsset<GameObject>(WaveAttackPath).GetComponent<UltimateDamageAttack>();
            Require(main != null && wave != null, "Original Ultimate main/wave components are missing.");
            Require(definition.Id == 0 && definition.ultimateAttackEvents == null && definition.ultimateAttackWeaponBehaviour == main,
                "Ultimate source definition/deferred UI boundary changed.");
            Require(main.ultimateData == definition && main.DanteUltimateWavePrefab == wave.gameObject,
                "Original Ultimate definition/wave references are missing.");
            Require(main.animator == main.GetComponent<Animator>() && wave.animator == wave.GetComponent<Animator>() &&
                main.animator.updateMode == AnimatorUpdateMode.UnscaledTime && wave.animator.updateMode == AnimatorUpdateMode.UnscaledTime,
                "Original Ultimate Animator references/unscaled clocks changed.");
            Transform hitbox = wave.transform.Find("Hitbox");
            Require(hitbox != null && wave.hitbox == hitbox.GetComponent<PlayerAttackHitBox>() && wave.progressionScaler == null,
                "Original Ultimate wave hitbox/no-scaler configuration changed.");
            ValidateHierarchy(main.gameObject, 20, 10, 13, "Dante_Burning");
            ValidateHierarchy(wave.gameObject, 431, 428, 429, null);
            ValidateClip(MainClipPath, 4.0333333f, false);
            ValidateClip(IdleClipPath, 0f, true);
            ValidateClip(WaveClipPath, 2.5166667f, false);
            AnimationEvent[] events = AnimationUtility.GetAnimationEvents(RequireAsset<AnimationClip>(MainClipPath));
            AnimationEvent[] spawns = events.Where(value => value.functionName == "SpawnWave").ToArray();
            Require(spawns.Length == 2 && spawns[0].intParameter == 1 && spawns[0].time == 0f &&
                spawns[1].intParameter == 2 && Mathf.Abs(spawns[1].time - 2.1166666f) < 0.0001f, "Original two-wave event schedule changed.");
            foreach (string path in AssetDatabase.FindAssets(string.Empty, new[] { OutputFolder }).Select(AssetDatabase.GUIDToAssetPath))
            {
                if (AssetDatabase.IsValidFolder(path) || path.EndsWith(".png", StringComparison.Ordinal)) continue;
                foreach (Match reference in Regex.Matches(File.ReadAllText(path), @"guid: ([0-9a-f]{32})"))
                {
                    string guid = reference.Groups[1].Value;
                    Require(guid.StartsWith("0000000000000000", StringComparison.Ordinal) || !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)),
                        "Unresolved GUID " + guid + " in " + path);
                }
            }
        }

        private static void ValidateHierarchy(GameObject root, int transforms, int particles, int renderers, string allowedContainer)
        {
            Require(root.GetComponentsInChildren<Transform>(true).Length == transforms &&
                root.GetComponentsInChildren<ParticleSystem>(true).Length == particles &&
                root.GetComponentsInChildren<Renderer>(true).Length == renderers, "Original Ultimate hierarchy changed: " + root.name);
            Transform containerTransform = allowedContainer == null ? null : root.transform.Find(allowedContainer);
            Require(allowedContainer == null || containerTransform != null, "Original Ultimate particle container is missing: " + allowedContainer);
            Renderer container = containerTransform == null ? null : containerTransform.GetComponent<ParticleSystemRenderer>();
            Require(allowedContainer == null || container != null, "Original Ultimate particle-container renderer is missing: " + allowedContainer);
            if (container != null)
                Require(!container.enabled && !container.GetComponent<ParticleSystem>().emission.enabled &&
                    container.sharedMaterials.Length == 1 && container.sharedMaterial == null,
                    "Original disabled Dante_Burning particle-container configuration changed.");
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0,
                    "Missing Ultimate script: " + AnimationUtility.CalculateTransformPath(child, root.transform));
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == container) continue;
                string path = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
                for (int slot = 0; slot < renderer.sharedMaterials.Length; slot++)
                {
                    Material material = renderer.sharedMaterials[slot];
                    Require(material != null, $"Ultimate renderer '{path}' slot {slot} has no material.");
                    Require(material.shader != null && material.shader.name != "Hidden/InternalErrorShader",
                        $"Ultimate renderer '{path}' slot {slot} '{AssetDatabase.GetAssetPath(material)}' has a missing/error shader.");
                }
            }
        }

        private static void ValidateClip(string path, float duration, bool loop)
        {
            AnimationClip clip = RequireAsset<AnimationClip>(path);
            Require(Mathf.Abs(clip.length - duration) < 0.0001f && clip.isLooping == loop, "Original Ultimate clip timing changed: " + path);
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
