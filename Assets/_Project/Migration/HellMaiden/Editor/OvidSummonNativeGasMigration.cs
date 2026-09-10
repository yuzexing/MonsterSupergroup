using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <summary>Focused ID 402 content import. Native summon lifecycle is implemented separately.</summary>
    public static class OvidSummonNativeGasMigration
    {
        public const string OutputFolder = "Assets/_Project/Content/HellMaiden/NativeGAS/Ovid/Summon";
        public const string WeaponPath = OutputFolder + "/MonoBehaviour/WeaponData_Ovid_Summon.asset";
        public const string BehaviourPath = OutputFolder + "/GameObject/Ovid_Summon_Behaviour.prefab";
        public const string DefaultAttackPath = OutputFolder + "/GameObject/Ovid_Summon_ButterflyAI.prefab";
        public const string FireAttackPath = OutputFolder + "/GameObject/Ovid_Summon_ButterflyAIFire.prefab";
        public const string PoisonAttackPath = OutputFolder + "/GameObject/Ovid_Summon_ButterflyAIPoison.prefab";
        public const string CacoonClipPath = OutputFolder + "/AnimationClip/Ovid_Butterfly_Cacoon.anim";
        public const string BirthClipPath = OutputFolder + "/AnimationClip/Ovid_Butterfly_Birth.anim";
        public const string MoveClipPath = OutputFolder + "/AnimationClip/Ovid_Butterfly_Move.anim";
        public const string EnterClipPath = OutputFolder + "/AnimationClip/Ovid_Butterfly_Attack_Enter.anim";
        public const string MainClipPath = OutputFolder + "/AnimationClip/Ovid_Butterfly_Attack_Loop.anim";
        public const string ExitClipPath = OutputFolder + "/AnimationClip/Ovid_Butterfly_Attack_Exit.anim";
        public const string VisualRootPath = "Iso Rotation/Self Rotation";
        public const string HitBoxPath = VisualRootPath + "/Buterfly_Attack Laser/Rotate/Hitbox";
        public const string ShadowPath = VisualRootPath + "/Buterfly_Attack Laser/Rotate/Shadow";
        public const string ShadowShaderPath = "Assets/_Project/Gameplay/Combat/Rendering/OvidSummonShadow.shader";
        public const string RestorationLimits =
            "Source Idle/Attack/Mover component fields are absent. References follow the original hierarchy and clip bindings; " +
            "numeric overrides use the source C# defaults, CustomAnimationCurve uses its default Ease.Linear, and " +
            "ClipTransition speed=1/fade=0/start=0 are explicit reconstruction decisions, not recovered prefab values. " +
            "Gameplay uses cacoonStateTime=3 (approved M4 tuning; source=60). All six clip curves/loop flags are retained, including Attack_Loop's " +
            "non-looping flag. Source ProjectileCount=0 is normalized to one summon for the Native GAS definition. " +
            "Source beamSound is unrecoverable and remains empty; the two FMOD references/None triggers and original " +
            "Unity AudioSource clip/play-on-awake are preserved. No animation audio events are invented. " +
            "Incomplete custom shaders use installed AllIn1 shaders. Archanor material texture/tint/UV scroll are " +
            "mapped with texture alpha blending and ExtraGlow as an approximate RGB gain; Spark_0's opaque black " +
            "atlas uses luminance-to-alpha. Original lighting, exact glow/compositing and absent BeamDouble second " +
            "texture cannot be recovered. The original Multiply shadow keeps its sprite, white tint, geometry and all " +
            "SSU parameters. Only this material uses an alpha-aware multiply shader with an explicit long-axis UV fade " +
            "approximation over the source width=0.84 fraction. The original SSU coordinate/rotation/noise equation is " +
            "unavailable; source Fade=2.61/Rotation=263/noise values are retained, not silently reinterpreted. " +
            "SSU wiggle and custom pixel autoscale are unavailable. " +
            "The source card visual Addressable GUID is absent from the export. Runtime/authority validation is separate.";

        private const string SourceDefault = "F:/DecomplieLatest/HellMaiden/ExportedProject";
        private const string SpriteShaderPath = "Assets/Plugins/AllIn1SpriteShader/Shaders/AllIn1SpriteShader.shader";
        private const string VfxShaderPath = "Assets/Plugins/AllIn1VfxToolkit/Shaders/AllIn1VfxURPCompat.shader";
        private const string KnockbackGuid = "75da00b77c9efb247ba641b508fa9e9f";
        private const string ObsoleteParticleHelperGuid = "cffbb4d90507e888f705c8dc76ba6e21";
        private const string BasicGlowShaderGuid = "c3d057996f54c054ba560f52c575b0f8";
        private const string BeamShaderGuid = "b0bc958e65d3d5b4d9aa0749d8efbf40";
        private const string BeamDoubleShaderGuid = "03266085939102f44a394f5a4bb76593";
        private const string CoffeeShaderGuid = "ddc0a7b0e4084284c859e167cee4d130";

        // Audited content closure. Scripts, DLLs and exported dummy shaders resolve to current dependencies.
        private static readonly string[] SourceAssets =
        {
            "AnimationClip/Ovid_Butterfly_Attack_Enter.anim",
            "AnimationClip/Ovid_Butterfly_Attack_Exit.anim",
            "AnimationClip/Ovid_Butterfly_Attack_Loop.anim",
            "AnimationClip/Ovid_Butterfly_Birth.anim",
            "AnimationClip/Ovid_Butterfly_Cacoon.anim",
            "AnimationClip/Ovid_Butterfly_Move.anim",
            "AudioClip/retro_orbital_small_laser.ogg",
            "GameObject/Ovid_Summon_Behaviour.prefab",
            "GameObject/Ovid_Summon_ButterflyAI.prefab",
            "GameObject/Ovid_Summon_ButterflyAIFire.prefab",
            "GameObject/Ovid_Summon_ButterflyAIPoison.prefab",
            "Material/Atlas01_0.mat",
            "Material/AtlasGreen_0.mat",
            "Material/AtlasGreen_1.mat",
            "Material/AtlasGreen_2.mat",
            "Material/Beam.mat",
            "Material/Beam_0.mat",
            "Material/Beam_1.mat",
            "Material/CartoonCoffee_Glow_001-Basic-_1.0__1.mat",
            "Material/CartoonCoffee_Glow_001-Basic-_2.0__1.mat",
            "Material/CartoonCoffee_Light_Ray_005-Basic-_1.0_ 1.mat",
            "Material/Fly.mat",
            "Material/Fly_0.mat",
            "Material/Fly_1.mat",
            "Material/Gradient_0.mat",
            "Material/Multiply 1_0.mat",
            "Material/Multiply.mat",
            "Material/PlayerAttack_Dante_DragonsBreath_ChargeBall_0.mat",
            "Material/PlayerAttack_Dante_DragonsBreath_ChargeBall_1.mat",
            "Material/PlayerAttack_Dante_DragonsBreath_ChargeBall_2.mat",
            "Material/Spark_0.mat",
            "Material/caccoon.mat",
            "Mesh/RetroCylinder_1.asset",
            "Mesh/Sphere.002.asset",
            "Mesh/Sphere_3.asset",
            "MonoBehaviour/KnockBack_Ovid_Summon.asset",
            "MonoBehaviour/WeaponData_Ovid_Summon.asset",
            "Sprite/Shadow_1.asset",
            "Sprite/circle_blurred 1_0.asset",
            "Texture2D/Glow_001_3.png",
            "Texture2D/Light_Ray_008.png",
            "Texture2D/LightningSheet_1.png",
            "Texture2D/Material.001_Base_color_1001_0.png",
            "Texture2D/PlayerAttack_Dante_DragonsBreath_ChargeBall.png",
            "Texture2D/PlayerAttack_Dante_DragonsBreath_ChargeBall_2.png",
            "Texture2D/PlayerAttack_Dante_DragonsBreath_ChargeBall_3.png",
            "Texture2D/RainbowVertical_1.png",
            "Texture2D/SSU_Noise_1K_1.png",
            "Texture2D/Shadow_2.png",
            "Texture2D/Sphere.png",
            "Texture2D/Sphere_0.png",
            "Texture2D/Sphere_1.png",
            "Texture2D/atlas01_0.png",
            "Texture2D/atlas02 1Green.png",
            "Texture2D/atlas02 1Green_0.png",
            "Texture2D/atlas02 1Green_1.png",
            "Texture2D/beam_dark 1.png",
            "Texture2D/beam_dark 1_1.png",
            "Texture2D/beam_dark 1_2.png",
            "Texture2D/circle_blurred 1_0.png",
            "Texture2D/fire2_1.png",
            "Texture2D/gradient4_0.png",
            "Texture2D/grain_0.png",
            "Texture2D/palette-downwell_1.png",
            "Texture2D/rainbow_1.png",
            "Texture2D/seamlessNoise_1.png",
            "Texture2D/white_1.png",
        };

        [MenuItem("Tools/HellMaiden Migration/Import Ovid Summon Native GAS Assets")]
        public static void Import()
        {
            string sourceRoot = Environment.GetEnvironmentVariable("HELLMAIDEN_SOURCE_PROJECT");
            if (string.IsNullOrWhiteSpace(sourceRoot)) sourceRoot = SourceDefault;
            sourceRoot = Path.GetFullPath(sourceRoot);
            Require(Directory.Exists(Path.Combine(sourceRoot, "Assets")), "HellMaiden source is missing: " + sourceRoot);
            string spriteGuid = RequireGuid(SpriteShaderPath);
            string vfxGuid = RequireGuid(VfxShaderPath);
            foreach (string path in SourceAssets) CopyIfMissing(sourceRoot, path, spriteGuid, vfxGuid);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string path in VariantPaths()) RepairModules(path);
            ConfigureMaterials(sourceRoot);
            ConfigureWeaponAndDatabase();
            AssetDatabase.SaveAssets();
            ValidateImportedAssets();
            Debug.Log("Ovid Summon ID 402 imported; existing weapons and player default preserved. " + RestorationLimits);
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
                    relativePath == "MonoBehaviour/WeaponData_Ovid_Summon.asset";
                Require(!owned || existing == destination, "Summon asset already has another canonical path: " + existing);
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
            if (relativePath.EndsWith(".png", StringComparison.Ordinal) || relativePath.EndsWith(".ogg", StringComparison.Ordinal))
                File.Copy(source, destinationFile);
            else
            {
                string yaml = File.ReadAllText(source)
                    .Replace("{fileID: -536417829, guid: a764a8f1aa53ec5f484d2a941db13b66, type: 3}",
                        "{fileID: 11500000, guid: 0ad50f81b1d25c441943c37a89ba23f6, type: 3}")
                    .Replace("159c7e9144365ce4590110a4cad76836", spriteGuid)
                    .Replace("d892932001015e24db884166029606dc", spriteGuid)
                    .Replace(BasicGlowShaderGuid, vfxGuid).Replace(BeamShaderGuid, vfxGuid)
                    .Replace(BeamDoubleShaderGuid, vfxGuid).Replace(CoffeeShaderGuid, vfxGuid);
                if (relativePath.EndsWith(".prefab", StringComparison.Ordinal)) yaml = RemoveObsoleteParticleHelpers(yaml);
                File.WriteAllText(destinationFile, yaml, new UTF8Encoding(false));
            }
            File.WriteAllText(destinationFile + ".meta", meta, new UTF8Encoding(false));
        }

        private static string RemoveObsoleteParticleHelpers(string yaml)
        {
            var ids = new List<string>();
            yaml = Regex.Replace(yaml, @"(?ms)^--- !u!114 &(-?\d+)\r?\n.*?(?=^--- !u!|\z)", match =>
            {
                // This helper only feeds matrices to the unavailable custom shader. Retain all visual nodes.
                if (!match.Value.Contains(ObsoleteParticleHelperGuid)) return match.Value;
                ids.Add(match.Groups[1].Value);
                return string.Empty;
            });
            foreach (string id in ids)
                yaml = Regex.Replace(yaml, @"(?m)^  - component: \{fileID: " + Regex.Escape(id) + @"\}\r?\n", string.Empty);
            return yaml;
        }

        private static void RepairModules(string path)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                SummonAIBehaviour ai = root.GetComponent<SummonAIBehaviour>();
                var mover = root.GetComponentInChildren<OvidSummonMover>(true);
                var idle = root.GetComponentInChildren<OvidSummonIdleModule>(true);
                var attack = root.GetComponentInChildren<OvidSummonAttackModule>(true);
                var positioning = root.GetComponentInChildren<OvidSummonPositioningModule>(true);
                Transform visual = root.transform.Find(VisualRootPath);
                Require(ai != null && mover != null && idle != null && attack != null && positioning != null && visual != null,
                    "Source summon modules/visual hierarchy are missing: " + path);
                Component animancer = visual.GetComponent("AnimancerComponent");
                Animator animator = visual.GetComponent<Animator>();
                Require(animancer != null && animator != null, "Source summon Animancer/Animator is missing: " + path);
                var aiFields = new SerializedObject(ai);
                Reference(aiFields, "progressionScaler", root.GetComponentInChildren<AttackProgressionScaler>(true));
                Reference(aiFields, "idlingStateModule", idle);
                Reference(aiFields, "positioningStateModule", positioning);
                Reference(aiFields, "attackingStateModule", attack);
                Reference(aiFields, "animancer", animancer);
                aiFields.ApplyModifiedPropertiesWithoutUndo();
                var animation = new SerializedObject(animancer);
                Reference(animation, "_Animator", animator);
                animation.ApplyModifiedPropertiesWithoutUndo();
                Require(animator.runtimeAnimatorController == null, "Source summon Animator unexpectedly acquired a controller.");

                var idleFields = new SerializedObject(idle);
                Reference(idleFields, "mover", mover);
                Float(idleFields, "stopDistance", 2f);
                ConfigureClip(idleFields, "idleAnimation", CacoonClipPath);
                ConfigureClip(idleFields, "birthAnimation", BirthClipPath);
                idleFields.ApplyModifiedPropertiesWithoutUndo();

                var attackFields = new SerializedObject(attack);
                Reference(attackFields, "mover", mover);
                Reference(attackFields, "hitBox", root.transform.Find(HitBoxPath).GetComponent<PlayerAttackOvertimeHitBox>());
                // These overrides are source C# defaults; the exported component contains none of them.
                Float(attackFields, "minDetectionRadius", 5f);
                Float(attackFields, "maxDetectionRadius", 10f);
                Float(attackFields, "clusterSearchRadius", 4f);
                Required(attackFields, "framePartitioningCount").intValue = 4;
                Required(attackFields, "maxEnemiesToProcess").intValue = 100;
                Float(attackFields, "angleOffset", 180f);
                Float(attackFields, "aimSmoothing", 15f);
                Float(attackFields, "sweepAngle", 30f);
                Float(attackFields, "predictionLeadTime", 0.2f);
                Float(attackFields, "velocitySmoothing", 10f);
                ConfigureLinearCurve(attackFields, "sweepAccelerationCurve");
                ConfigureClip(attackFields, "attackEnterAnimation", EnterClipPath);
                ConfigureClip(attackFields, "attackLoopAnimation", MainClipPath);
                ConfigureClip(attackFields, "attackExitAnimation", ExitClipPath);
                for (int index = 1; index <= 4; index++) Required(attackFields, "beamSound.Guid.Data" + index).intValue = 0;
                attackFields.ApplyModifiedPropertiesWithoutUndo();

                var moverFields = new SerializedObject(mover);
                Reference(moverFields, "rb", root.GetComponent<Rigidbody2D>());
                Reference(moverFields, "isoPivot", root.transform.Find("Iso Rotation"));
                Reference(moverFields, "rotationPivot", visual);
                Float(moverFields, "cacoonSpeedMultiplier", 2f);
                Float(moverFields, "maxMoveSpeed", 12f);
                Float(moverFields, "minMoveSpeed", 0.5f);
                Float(moverFields, "maxSpeedDistance", 20f);
                Float(moverFields, "decelerationMultiplier", 2f);
                Float(moverFields, "angleOffset", 180f);
                Float(moverFields, "rotationSmoothing", 20f);
                Float(moverFields, "tiltAngle", -45f);
                Float(moverFields, "hoverFrequency", 2f);
                Float(moverFields, "hoverAmplitude", 0.5f);
                Float(moverFields, "hoverYAxisFactor", 0.5f);
                Float(moverFields, "minAnimSpeed", 1f);
                Float(moverFields, "maxAnimSpeed", 2f);
                ConfigureLinearCurve(moverFields, "accelerationCurve");
                ConfigureClip(moverFields, "moveAnimation", MoveClipPath);
                moverFields.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void ConfigureClip(SerializedObject target, string field, string path)
        {
            Required(target, field + "._Clip").objectReferenceValue = RequireAsset<AnimationClip>(path);
            Float(target, field + "._FadeDuration", 0f);
            Float(target, field + "._Speed", 1f);
            Float(target, field + "._NormalizedStartTime", 0f);
        }

        private static void ConfigureLinearCurve(SerializedObject target, string field)
        {
            Required(target, field + ".useOwnAnimationCurve").boolValue = false;
            SerializedProperty ease = Required(target, field + ".ease");
            int index = Array.IndexOf(ease.enumNames, "Linear");
            Require(index >= 0, "CustomAnimationCurve no longer exposes its source Linear default.");
            ease.enumValueIndex = index;
            Required(target, field + ".animationCurve").animationCurveValue = new AnimationCurve();
        }

        private static void ConfigureMaterials(string sourceRoot)
        {
            foreach (string path in SourceAssets.Where(value => value.EndsWith(".mat", StringComparison.Ordinal)))
            {
                // Shared GUIDs stay with their existing canonical material; do not mutate another migration's owner.
                Material material = AssetDatabase.LoadAssetAtPath<Material>(OutputFolder + "/" + path);
                if (material == null) continue;
                string source = File.ReadAllText(Path.Combine(sourceRoot, "Assets", path));
                bool archanor = source.Contains(BasicGlowShaderGuid) || source.Contains(BeamShaderGuid) || source.Contains(BeamDoubleShaderGuid);
                bool coffee = source.Contains(CoffeeShaderGuid);
                if (archanor || coffee)
                {
                    string property = archanor ? "_TextureSample" : "_MainTex";
                    Match texture = Regex.Match(source, Regex.Escape(property) + @":\s+m_Texture: \{fileID: 2800000, guid: ([0-9a-f]{32}), type: 3\}\s+m_Scale: \{x: ([^,]+), y: ([^}]+)\}\s+m_Offset: \{x: ([^,]+), y: ([^}]+)\}");
                    Require(texture.Success, "Original visual texture is missing: " + path);
                    Color tint = ReadColor(source, archanor ? "_Tint" : "_TintColor");
                    float gain = archanor ? Mathf.Max(1f, ReadFloat(source, "_ExtraGlow")) : 1f;
                    material.shader = RequireAsset<Shader>(VfxShaderPath);
                    material.shaderKeywords = path == "Material/Spark_0.mat" ? new[] { "PREMULTIPLYCOLOR_ON" } : Array.Empty<string>();
                    material.SetTexture("_MainTex", RequireAsset<Texture2D>(AssetDatabase.GUIDToAssetPath(texture.Groups[1].Value)));
                    material.SetTextureScale("_MainTex", new Vector2(Parse(texture.Groups[2].Value), Parse(texture.Groups[3].Value)));
                    material.SetTextureOffset("_MainTex", new Vector2(Parse(texture.Groups[4].Value), Parse(texture.Groups[5].Value)));
                    material.SetColor("_ShapeColor", new Color(tint.r * gain, tint.g * gain, tint.b * gain, 1f));
                    material.SetColor("_Color", Color.white);
                    material.SetFloat("_Alpha", tint.a);
                    material.SetFloat("_SrcMode", 5f);
                    material.SetFloat("_DstMode", 10f);
                    material.SetFloat("_ZWrite", 0f);
                    material.SetFloat("_CullingOption", 0f);
                    material.SetFloat("_ShapeXSpeed", 0f);
                    material.SetFloat("_ShapeYSpeed", 0f);
                    if (source.Contains("_ScrollSpeed:"))
                    {
                        Color scroll = ReadColor(source, "_ScrollSpeed");
                        material.SetFloat("_ShapeXSpeed", scroll.r);
                        material.SetFloat("_ShapeYSpeed", scroll.g);
                    }
                    material.renderQueue = 3000;
                }
                else if (path == "Material/Multiply.mat")
                {
                    Require(source.Contains("d892932001015e24db884166029606dc"), "Summon shadow no longer uses the original SSU multiply definition.");
                    SpriteRenderer shadow = RequireAsset<GameObject>(DefaultAttackPath).transform.Find(ShadowPath).GetComponent<SpriteRenderer>();
                    Require(shadow.sprite != null && shadow.sprite.texture != null, "The original Shadow sprite/texture must remain available.");
                    material.shader = RequireAsset<Shader>(ShadowShaderPath);
                    material.shaderKeywords = Array.Empty<string>();
                    material.SetTexture("_MainTex", shadow.sprite.texture);
                    material.SetColor("_Color", ReadColor(source, "_Color"));
                    Rect rect = shadow.sprite.rect;
                    material.SetVector("_ShadowUVRect", new Vector4(rect.xMin / shadow.sprite.texture.width,
                        rect.yMin / shadow.sprite.texture.height, rect.xMax / shadow.sprite.texture.width, rect.yMax / shadow.sprite.texture.height));
                    float width = ReadFloat(source, "_DirectionalAlphaFadeWidth");
                    Require(width > 0f && width <= 1f, "The audited Shadow fade approximation requires a normalized source width.");
                    material.SetFloat("_ShadowFadeStart", 1f - width);
                    material.SetFloat("_ShadowFadeEnd", 1f);
                    foreach (string property in new[] { "_EnableDirectionalAlphaFade", "_DirectionalAlphaFadeFade",
                        "_DirectionalAlphaFadeRotation", "_DirectionalAlphaFadeWidth", "_DirectionalAlphaFadeNoiseFactor", "_DirectionalAlphaFadeInvert" })
                        material.SetFloat(property, ReadFloat(source, property));
                    material.SetVector("_DirectionalAlphaFadeNoiseScale", ReadColor(source, "_DirectionalAlphaFadeNoiseScale"));
                    material.renderQueue = 3000;
                }
                else if (source.Contains("d892932001015e24db884166029606dc"))
                {
                    material.shader = RequireAsset<Shader>(SpriteShaderPath);
                    material.shaderKeywords = new[] { "PREMULTIPLYALPHA_ON" };
                    material.SetFloat("_MySrcMode", 2f);
                    material.SetFloat("_MyDstMode", 10f);
                    material.SetFloat("_Brightness", 0f);
                    material.SetFloat("_Contrast", 1f);
                    material.SetFloat("_Alpha", 1f);
                    material.SetFloat("_ZWrite", 0f);
                }
                EditorUtility.SetDirty(material);
            }
        }

        private static Color ReadColor(string source, string property)
        {
            Match match = Regex.Match(source, Regex.Escape(property) + @": \{r: ([^,]+), g: ([^,]+), b: ([^,]+), a: ([^}]+)\}");
            Require(match.Success, "Source material color is missing: " + property);
            return new Color(Parse(match.Groups[1].Value), Parse(match.Groups[2].Value), Parse(match.Groups[3].Value), Parse(match.Groups[4].Value));
        }
        private static float ReadFloat(string source, string property)
        {
            Match match = Regex.Match(source, Regex.Escape(property) + @": ([^\r\n]+)");
            Require(match.Success, "Source material float is missing: " + property);
            return Parse(match.Groups[1].Value);
        }
        private static float Parse(string value) => float.Parse(value, CultureInfo.InvariantCulture);

        private static void ConfigureWeaponAndDatabase()
        {
            var emitterRoot = PrefabUtility.LoadPrefabContents(BehaviourPath);
            try
            {
                var emitter = new SerializedObject(emitterRoot.GetComponent<OvidSummonAttackBehaviour>());
                Float(emitter, "cacoonStateTime", 3f);
                emitter.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(emitterRoot, BehaviourPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(emitterRoot); }
            WeaponData weapon = RequireAsset<WeaponData>(WeaponPath);
            KnockbackSettings knockback = RequireAsset<KnockbackSettings>(AssetDatabase.GUIDToAssetPath(KnockbackGuid));
            Require(weapon.ID == 402 && knockback.distance == 0.5f, "Source summon identity/knockback changed.");
            var presentation = new WeaponPresentationSettings();
            presentation.Configure(new CameraShakeSettings(), 0f, knockback);
            weapon.ConfigureNativeGas(new AttackStats
            {
                damage = 24, critRate = 0f, critMultiplier = 1f, speed = 0.2f, size = 1f,
                duration = 1f, projectileCount = 1, knockbackDistance = 0.5f,
                damageType = MonsterSupergroup.GAS.DamageType.Normal
            }, CombatTags.Attack, presentation);
            weapon.WeaponPrefab = RequireAsset<GameObject>(BehaviourPath).GetComponent<OvidSummonAttackBehaviour>();
            EditorUtility.SetDirty(weapon);
            WeaponDB database = RequireAsset<WeaponDB>(DanteNativeGasMigration.NativeWeaponDatabasePath);
            var entries = database.Weapons.ToList();
            Require(entries.All(entry => entry != null), "Existing native database contains a null weapon.");
            Require(entries.Count(entry => entry.ID == 402) <= 1 && entries.All(entry => entry.ID != 402 || entry == weapon),
                "Another weapon already owns ID 402.");
            if (!entries.Contains(weapon)) { entries.Add(weapon); database.Configure(entries.ToArray()); EditorUtility.SetDirty(database); }
        }

        [MenuItem("Tools/HellMaiden Migration/Validate Ovid Summon Native GAS Assets")]
        public static void ValidateImportedAssets()
        {
            WeaponData weapon = RequireAsset<WeaponData>(WeaponPath);
            weapon.ValidateNativeGas();
            Require(weapon.ID == 402 && weapon.BaseStats.damage == 24 && weapon.BaseStats.projectileCount == 1 &&
                weapon.AttackTags == CombatTags.Attack, "Summon Native identity/count normalization changed.");
            var emitter = new SerializedObject(weapon.WeaponPrefab);
            Require(Required(emitter, "cacoonStateTime").floatValue == 3f, "Gameplay cocoon duration must be three seconds.");
            string[] variantFields = { "variants.defaultPrefab", "variants.firePrefab", "variants.poisonPrefab" };
            string[] paths = VariantPaths();
            for (int i = 0; i < paths.Length; i++)
            {
                GameObject root = RequireAsset<GameObject>(paths[i]);
                Require(Required(emitter, variantFields[i]).objectReferenceValue == root.GetComponent<SummonAIBehaviour>(),
                    "Source summon variant reference changed: " + paths[i]);
                Require(root.GetComponentsInChildren<Transform>(true).Length == 66 &&
                    root.GetComponentsInChildren<ParticleSystem>(true).Length == 15, "Original summon hierarchy/particles changed.");
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0,
                        "Missing summon script: " + paths[i] + "/" + AnimationUtility.CalculateTransformPath(child, root.transform));
                ValidatePresentationDependencies(root);
                var attack = new SerializedObject(root.GetComponentInChildren<OvidSummonAttackModule>(true));
                Require(Required(attack, "hitBox").objectReferenceValue == root.transform.Find(HitBoxPath).GetComponent<PlayerAttackOvertimeHitBox>(),
                    "Summon hitbox reference is missing.");
                foreach (string field in new[] { "attackEnterAnimation", "attackLoopAnimation", "attackExitAnimation" })
                    Require(Required(attack, field + "._Clip").objectReferenceValue != null, "Missing summon phase: " + field);
            }
            string[] clips = { CacoonClipPath, BirthClipPath, MoveClipPath, EnterClipPath, MainClipPath, ExitClipPath };
            float[] lengths = { 4f, 3.8f, 1.0333334f, 1f, 0.33333334f, 0.6333334f };
            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip clip = RequireAsset<AnimationClip>(clips[i]);
                Require(Mathf.Abs(clip.length - lengths[i]) < 0.0001f && clip.isLooping == (i == 0 || i == 2),
                    "Source summon clip timing/looping changed: " + clips[i]);
            }
            foreach (string path in AssetDatabase.FindAssets(string.Empty, new[] { OutputFolder }).Select(AssetDatabase.GUIDToAssetPath))
            {
                if (AssetDatabase.IsValidFolder(path) || path.EndsWith(".png", StringComparison.Ordinal) || path.EndsWith(".ogg", StringComparison.Ordinal)) continue;
                foreach (Match reference in Regex.Matches(File.ReadAllText(path), @"guid: ([0-9a-f]{32})"))
                {
                    string guid = reference.Groups[1].Value;
                    Require(guid.StartsWith("0000000000000000", StringComparison.Ordinal) || !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)),
                        "Unresolved GUID " + guid + " in " + path);
                }
            }
        }

        private static void ValidatePresentationDependencies(GameObject root)
        {
            // Source 199880619614767448 / 199695600491528212 are disabled non-emitting containers.
            string[] containers = { VisualRootPath + "/Power_Burst_V3", VisualRootPath + "/Buterfly_Attack Laser" };
            var allowed = new HashSet<Renderer>();
            foreach (string path in containers)
            {
                Transform container = root.transform.Find(path);
                var renderer = container.GetComponent<ParticleSystemRenderer>();
                ParticleSystem particles = container.GetComponent<ParticleSystem>();
                Require(renderer != null && !renderer.enabled && particles != null && !particles.emission.enabled &&
                    renderer.sharedMaterials.Length == 1 && renderer.sharedMaterial == null, "Source particle-container configuration changed: " + path);
                allowed.Add(renderer);
            }
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (allowed.Contains(renderer)) continue;
                string path = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material material = materials[slot];
                    Require(material != null, $"Summon renderer '{path}' slot {slot} is missing its material.");
                    Require(material.shader != null && material.shader.name != "Hidden/InternalErrorShader",
                        $"Summon renderer '{path}' slot {slot} '{AssetDatabase.GetAssetPath(material)}' has a missing/error shader.");
                }
            }
        }

        private static string[] VariantPaths() => new[] { DefaultAttackPath, FireAttackPath, PoisonAttackPath };
        private static SerializedProperty Required(SerializedObject target, string name) =>
            target.FindProperty(name) ?? throw new InvalidDataException("Missing serialized property: " + name);
        private static void Float(SerializedObject target, string name, float value) => Required(target, name).floatValue = value;
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
