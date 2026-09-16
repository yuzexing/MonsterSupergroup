using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Animancer;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    /// <summary>Selective visual restoration. Does not create enemies, change combat data, or approve gates.</summary>
    public static class LimboArtAssets
    {
        public const string Root = LimboReferenceAssets.Root + "/Art";
        private const string Label = "LimboArtRestoredV1";
        [Serializable] public class Callback { public int index; public string method; }
        [Serializable] public class Binding { public string field, path, normalizedStart; public float length, speed, fade; public string[] eventTimes; public Callback[] visualCallbacks; }
        [Serializable] public class Body { public string group, name, identity, target, template; public Binding[] bindings; public bool contact; }
        [Serializable] public class Effect { public string group, name, template; }
        [Serializable] public class Palette { public string identity, texture; public int variant; public float[] hue; }
        [Serializable] public class Entry { public string source, sourceGuid, sourceSha256, destination, destinationGuid, treatment; }
        [Serializable] public class Source { public Body[] bodies; public Effect[] effects; public Palette[] palettes; public Entry[] entries; }
        [Serializable] public class Bake { public string original, lut, baked; }
        [Serializable] public class Bakes { public Bake[] pairs; }
        private static Source data;
        private static Bakes bakes;

        public static void ApplyOpeningBatch() => Batch(true);
        public static void ApplyAllBatch() => Batch(false);
        private static void Batch(bool opening)
        {
            int exit = 0;
            try { Apply(opening); }
            catch (Exception e) { Debug.LogException(e); exit = 1; }
            finally { EditorApplication.Exit(exit); }
        }

        public static void Apply(bool opening)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            data = JsonUtility.FromJson<Source>(File.ReadAllText(Root + "/ArtSource.json"));
            bakes = JsonUtility.FromJson<Bakes>(File.ReadAllText(Root + "/BakedPalettes.json"));
            foreach (var entry in data.entries.Where(e => e.destination.EndsWith(".mat") && e.treatment == "independent-adaptation"))
                PrepareMaterial(entry.destination);
            PrepareWarningClips();
            ConfigurePaletteDatabase();
            foreach (var body in data.bodies.Where(b => !opening || b.group == "E1" || b.group == "E2")) RestoreBody(body);
            RestoreDash();
            if (!opening)
            {
                RestoreMelee("Skeleton_Warning", LimboStage2Assets.Root + "/SkeletonAttack.prefab");
                RestoreMelee("Skeleton_Warning_Elite Variant", LimboStage2Assets.Root + "/Elite_SkeletonAttack.prefab");
                PrepareDeferredEffect("Ghoul_Warning", true);
                PrepareDeferredEffect("soul enemy warning", true);
                PrepareDeferredEffect("Enemy_Bomb_ExplosionAttack 1", false);
                RestoreFire();
                RestoreImpBullet();
                CreateEffectFixture();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[LimboArt] visual configuration applied; original/Full gates unchanged; rendered verification still required.");
        }

        private static bool Done(Object asset) => asset != null && AssetDatabase.GetLabels(asset).Contains(Label);
        private static void Mark(Object asset) => AssetDatabase.SetLabels(asset, AssetDatabase.GetLabels(asset).Append(Label).Distinct().ToArray());
        private static float Number(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        private static T Require<T>(GameObject root) where T : Component
        { var component = root.GetComponent<T>(); return component != null ? component : root.AddComponent<T>(); }

        private static void PrepareMaterial(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (Done(m)) return;
            // Preserve source colors/textures/visible keyword effects supported by
            // the installed shader. Unavailable custom renderer keywords are not invented.
            var supported = m.shader.keywordSpace.keywords.Select(k => k.name).ToHashSet();
            m.shaderKeywords = m.shaderKeywords.Where(supported.Contains).ToArray();
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0);
            EditorUtility.SetDirty(m); Mark(m);
        }

        private static void PrepareWarningClips()
        {
            foreach (var entry in data.entries.Where(e => e.destination.EndsWith(".anim") && e.treatment == "independent-adaptation"))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(entry.destination);
                if (Done(clip)) continue;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    string property = binding.propertyName;
                    // Unity material-property animation IDs retain the lower 28
                    // CRC bits with a material flag. Confirmed against source
                    // shader property names: _Alpha=9B19FAF2, _FadeAmount=ED9D63DA.
                    if (property.Contains("path_0x8B19FAF2")) property = "material._Alpha";
                    else if (property.Contains("path_0x8D9D63DA")) property = "material._FadeAmount";
                    if (property.Contains("path_0x")) throw new InvalidDataException("Unresolved material animation binding: " + property);
                    if (property == binding.propertyName) continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    AnimationUtility.SetEditorCurve(clip, binding, null);
                    var updated = binding; updated.propertyName = property;
                    AnimationUtility.SetEditorCurve(clip, updated, curve);
                }
                EditorUtility.SetDirty(clip); Mark(clip);
            }
        }

        private static void ConfigurePaletteDatabase()
        {
            var db = AssetDatabase.LoadAssetAtPath<EnemyDatabase>(LimboReferenceAssets.Root + "/AdaptedEnemyDB.asset");
            var so = new SerializedObject(db); var enemies = so.FindProperty("enemies");
            foreach (var p in data.palettes)
                for (int i = 0; i < enemies.arraySize; i++)
                {
                    var e = enemies.GetArrayElementAtIndex(i);
                    if (e.FindPropertyRelative("enemyName").stringValue != p.identity) continue;
                    var variant = e.FindPropertyRelative("enemyData").GetArrayElementAtIndex(p.variant);
                    var color = variant.FindPropertyRelative("colorLUT");
                    if (color.objectReferenceValue == null && !string.IsNullOrEmpty(p.texture))
                        color.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(p.texture);
                    var hue = variant.FindPropertyRelative("hueColor");
                    if (hue.colorValue == Color.clear) hue.colorValue = new Color(p.hue[0], p.hue[1], p.hue[2], p.hue[3]);
                }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform MatchTransform(Transform from, Transform source, Transform target)
        {
            if (from == source) return target;
            var parent = MatchTransform(from.parent, source, target);
            var to = parent.Find(from.name);
            if (to == null) { to = new GameObject(from.name).transform; to.SetParent(parent, false); }
            to.localPosition = from.localPosition; to.localRotation = from.localRotation; to.localScale = from.localScale;
            to.gameObject.SetActive(from.gameObject.activeSelf);
            return to;
        }

        private static void OverlayVisuals(GameObject source, GameObject target)
        {
            var paths = source.GetComponentsInChildren<Renderer>(true).Select(r => AnimationUtility.CalculateTransformPath(r.transform, source.transform)).ToHashSet();
            foreach (var old in target.GetComponentsInChildren<Renderer>(true))
                if (!paths.Contains(AnimationUtility.CalculateTransformPath(old.transform, target.transform))) Object.DestroyImmediate(old);
            foreach (var from in source.GetComponentsInChildren<Renderer>(true))
            {
                var to = MatchTransform(from.transform, source.transform, target.transform);
                if (from is ParticleSystemRenderer)
                {
                    var ps = Require<ParticleSystem>(to.gameObject);
                    EditorUtility.CopySerialized(from.GetComponent<ParticleSystem>(), ps);
                    var main = ps.main; main.useUnscaledTime = false;
                }
                var r = to.GetComponent(from.GetType());
                if (r == null) r = to.gameObject.AddComponent(from.GetType());
                EditorUtility.CopySerialized(from, r);
            }
        }

        private static void RestoreBody(Body body)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(body.target);
            if (asset == null) throw new FileNotFoundException(body.target);
            if (Done(asset))
            {
                // Earlier references retained EnemyBaseSample, whose default
                // animation writes Circle and RGB(255,0,0) underneath Animancer.
                var existing = PrefabUtility.LoadPrefabContents(body.target);
                try
                {
                    var animator = existing.GetComponent<EnemyController>().enemyAnimator;
                    ConfigureDeathPresentation(existing);
                    if (body.contact) existing.GetComponent<Rigidbody2D>().freezeRotation = true;
                    var native = animator.GetComponent<Animator>();
                    var settings = new SerializedObject(animator);
                    settings.FindProperty("useRecoveredMovement").boolValue = true; settings.ApplyModifiedPropertiesWithoutUndo();
                    foreach (var binding in body.bindings) BindVisualCallbacks(settings.FindProperty(binding.field), binding, animator);
                    settings.ApplyModifiedPropertiesWithoutUndo();
                    RestoreInitialBodyFrame(existing.GetComponent<EnemyController>(), body);
                    if (native.runtimeAnimatorController != null && AssetDatabase.GetAssetPath(native.runtimeAnimatorController) == "Assets/Sprite/Sprite.controller")
                    { native.runtimeAnimatorController = null; animator.animator = native; }
                    PrefabUtility.SaveAsPrefabAsset(existing, body.target);
                }
                finally { PrefabUtility.UnloadPrefabContents(existing); }
                return;
            }
            var root = PrefabUtility.LoadPrefabContents(body.target);
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(body.template);
                OverlayVisuals(source, root);
                var controller = root.GetComponent<EnemyController>();
                ConfigureDeathPresentation(root);
                if (body.contact) root.GetComponent<Rigidbody2D>().freezeRotation = true;
                var animator = controller.enemyAnimator;
                var nativeAnimator = animator.GetComponent<Animator>();
                nativeAnimator.runtimeAnimatorController = null; // Source transitions are driven by existing Animancer.
                animator.animator = nativeAnimator;
                var aser = new SerializedObject(animator);
                aser.FindProperty("useRecoveredMovement").boolValue = true;
                var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
                var list = aser.FindProperty("renderers"); list.arraySize = renderers.Length;
                for (int i = 0; i < renderers.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
                var swapper = Require<SpriteRendererPaletteSwapper>(animator.gameObject);
                aser.FindProperty("paletteSwapper").objectReferenceValue = swapper;
                foreach (var binding in body.bindings)
                { Bind(aser.FindProperty(binding.field), binding); BindVisualCallbacks(aser.FindProperty(binding.field), binding, animator); }
                aser.ApplyModifiedPropertiesWithoutUndo();
                RestoreInitialBodyFrame(controller, body);
                var ps = new SerializedObject(swapper);
                ps.FindProperty("_Renderer").objectReferenceValue = animator.GetComponent<SpriteRenderer>();
                var pairs = bakes.pairs.Where(b => data.palettes.Any(p => p.identity == body.identity && p.texture == b.lut)).ToArray();
                var bs = ps.FindProperty("bakedPalettes"); bs.arraySize = pairs.Length;
                for (int i = 0; i < pairs.Length; i++)
                {
                    var p = bs.GetArrayElementAtIndex(i);
                    p.FindPropertyRelative("original").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(pairs[i].original);
                    p.FindPropertyRelative("lut").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(pairs[i].lut);
                    p.FindPropertyRelative("baked").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(pairs[i].baked);
                }
                ps.ApplyModifiedPropertiesWithoutUndo();
                var agent = new SerializedObject(root.GetComponent<NetworkEnemySimulationAgent>());
                agent.FindProperty("referenceArtDatabase").objectReferenceValue = AssetDatabase.LoadAssetAtPath<EnemyDatabase>(LimboReferenceAssets.Root + "/AdaptedEnemyDB.asset");
                agent.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, body.target);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Mark(AssetDatabase.LoadAssetAtPath<GameObject>(body.target));
        }

        private static void RestoreInitialBodyFrame(EnemyController enemy, Body body)
        {
            // Removing the old reference controller can restore its cached Circle
            // default after the visual overlay. Copy the source default again after
            // removing that controller, so a later Animancer.Stop restores the body.
            if (enemy.spriteRenderer.sprite != null && enemy.spriteRenderer.sprite.name != "Circle") return;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(body.template);
            var path = AnimationUtility.CalculateTransformPath(enemy.spriteRenderer.transform, enemy.transform);
            enemy.spriteRenderer.sprite = source.transform.Find(path).GetComponent<SpriteRenderer>().sprite;
        }

        private static void ConfigureDeathPresentation(GameObject root)
        {
            var so = new SerializedObject(root.GetComponent<NetworkEnemyServerDriver>());
            so.FindProperty("waitForDeathPresentation").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            var controller = new SerializedObject(root.GetComponent<EnemyController>());
            controller.FindProperty("animateMovementOnlyDeath").boolValue = true;
            controller.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Bind(SerializedProperty p, Binding b)
        {
            p.FindPropertyRelative("_Clip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AnimationClip>(b.path);
            p.FindPropertyRelative("_Speed").floatValue = b.speed; p.FindPropertyRelative("_FadeDuration").floatValue = b.fade;
            p.FindPropertyRelative("_NormalizedStartTime").floatValue = Number(b.normalizedStart);
            var events = p.FindPropertyRelative("_Events");
            events.FindPropertyRelative("_Callbacks").arraySize = 0; events.FindPropertyRelative("_Names").arraySize = 0;
            var times = events.FindPropertyRelative("_NormalizedTimes"); times.arraySize = b.eventTimes.Length;
            for (int i = 0; i < times.arraySize; i++) times.GetArrayElementAtIndex(i).floatValue = Number(b.eventTimes[i]);
        }

        private static GameObject Template(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(data.effects.Single(e => e.name == name).template);
        private static void BindVisualCallbacks(SerializedProperty transition, Binding binding, EnemyAnimator animator)
        {
            var events = transition.FindPropertyRelative("_Events");
            var times = events.FindPropertyRelative("_NormalizedTimes");
            var callbacks = events.FindPropertyRelative("_Callbacks");
            foreach (var source in binding.visualCallbacks ?? Array.Empty<Callback>())
            {
                if (source.method != "DeathAnimationShadowFade") throw new InvalidDataException("Unknown visual callback " + source.method);
                // Already adapted assets may have had omitted audio events removed.
                // Resolve the callback by source time, not its old serialized index.
                float sourceTime = Number(binding.eventTimes[source.index]);
                int index = -1;
                for (int i = 0; i < times.arraySize; i++)
                    if (times.GetArrayElementAtIndex(i).floatValue.Equals(sourceTime)) { index = i; break; }
                if (index < 0) throw new InvalidDataException("Missing visual event time: " + binding.field);
                if (callbacks.arraySize <= index) callbacks.arraySize = index + 1;
                var slot = callbacks.GetArrayElementAtIndex(index);
                if (slot.managedReferenceValue != null) continue;
                var evt = new Animancer.UnityEvent();
                UnityEditor.Events.UnityEventTools.AddPersistentListener(evt, animator.DeathAnimationShadowFade);
                slot.managedReferenceValue = evt;
            }
            var names = events.FindPropertyRelative("_Names");
            // Last time is the end-event marker. Keep it even without a callback.
            for (int i = times.arraySize - 2; i >= 0; i--)
            {
                if (i < callbacks.arraySize && callbacks.GetArrayElementAtIndex(i).managedReferenceValue != null) continue;
                if (i < names.arraySize && names.GetArrayElementAtIndex(i).objectReferenceValue != null) continue;
                times.DeleteArrayElementAtIndex(i);
                if (i < callbacks.arraySize) callbacks.DeleteArrayElementAtIndex(i);
                if (i < names.arraySize) names.DeleteArrayElementAtIndex(i);
            }
        }

        public static void RepairEventBindingsBatch()
        {
            int exit = 0;
            try
            {
                var source = JsonUtility.FromJson<Source>(File.ReadAllText(Root + "/ArtSource.json"));
                foreach (var body in source.bodies)
                {
                    var root = PrefabUtility.LoadPrefabContents(body.target);
                    try
                    {
                        var animator = root.GetComponent<EnemyController>().enemyAnimator;
                        var settings = new SerializedObject(animator);
                        foreach (var binding in body.bindings) BindVisualCallbacks(settings.FindProperty(binding.field), binding, animator);
                        settings.ApplyModifiedPropertiesWithoutUndo();
                        PrefabUtility.SaveAsPrefabAsset(root, body.target);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                AssetDatabase.SaveAssets();
            }
            catch (Exception e) { Debug.LogException(e); exit = 1; }
            finally { EditorApplication.Exit(exit); }
        }
        private static AnimationClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/Imported/AnimationClip/" + name + ".anim");
        private static MeleeAttackWarning Warning(GameObject host, Transform animationRoot, string start, string end)
        {
            var animator = Require<Animator>(animationRoot.gameObject);
            var animancer = Require<AnimancerComponent>(animationRoot.gameObject);
            animancer.Animator = animator;
            var warning = Require<MeleeAttackWarning>(host);
            var so = new SerializedObject(warning); so.FindProperty("animancer").objectReferenceValue = animancer;
            foreach (var pair in new[] { ("warningStart", start), ("warningEnd", end) })
            {
                var p = so.FindProperty(pair.Item1); p.FindPropertyRelative("_Clip").objectReferenceValue = Clip(pair.Item2);
                p.FindPropertyRelative("_Speed").floatValue = 1; p.FindPropertyRelative("_FadeDuration").floatValue = 0;
            }
            so.ApplyModifiedPropertiesWithoutUndo(); return warning;
        }

        private static void RestoreDash()
        {
            if (Done(AssetDatabase.LoadAssetAtPath<GameObject>(LimboDashAssets.ArrowPath))) return;
            var root = Object.Instantiate(Template("Arrow"));
            try
            {
                root.name = "ReferenceDashArrow"; root.transform.position = Vector3.zero;
                var warning = Warning(root, root.transform.Find("SlimePath"), "Slime Path Show", "Slime Path fadeout");
                root.AddComponent<EnemyAttackPrefab>().attackWarning = warning;
                PrefabUtility.SaveAsPrefabAsset(root, LimboDashAssets.ArrowPath);
            }
            finally { Object.DestroyImmediate(root); }
            Mark(AssetDatabase.LoadAssetAtPath<GameObject>(LimboDashAssets.ArrowPath));
        }

        private static void RestoreMelee(string name, string path)
        {
            if (Done(AssetDatabase.LoadAssetAtPath<GameObject>(path))) return;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                OverlayVisuals(Template(name), root);
                root.GetComponent<EnemyAttackPrefab>().attackWarning = Warning(root.transform.Find("AttackWarning").gameObject,
                    root.transform.Find("AttackWarning"), "MeleeWarning_BuildUp", "MeleeWarning_BuildDown");
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Mark(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        }

        private static void PrepareDeferredEffect(string name, bool warning)
        {
            string path = Root + "/Effects/" + name + ".prefab";
            if (File.Exists(path)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var root = Object.Instantiate(Template(name));
            try
            {
                root.name = name; root.transform.position = Vector3.zero;
                if (warning)
                {
                    bool soul = name.StartsWith("soul");
                    Warning(root, soul ? root.transform : root.transform.Find("AttackWarning"),
                        soul ? "Buildup Lost Soul" : "MeleeWarning_BuildUp", soul ? "Fadeout2" : "MeleeWarning_BuildDown");
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void RestoreFire()
        {
            string path = Root + "/Effects/ReferenceFireParticles.prefab";
            if (!File.Exists(path))
            {
                var root = Object.Instantiate(Template("FireParticles"));
                try { root.name = "ReferenceFireParticles"; root.transform.position = Vector3.zero; PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { Object.DestroyImmediate(root); }
            }
            var fire = PrefabUtility.LoadPrefabContents(path);
            try
            {
                // Network trap completion cannot depend on a local camera's particle culling.
                foreach (var system in fire.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = system.main; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; main.useUnscaledTime = false;
                    system.GetComponent<ParticleSystemRenderer>().sortingLayerName = "EnemyAttack";
                }
                PrefabUtility.SaveAsPrefabAsset(fire, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(fire); }
            var ps = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<ParticleSystem>();
            foreach (var guid in AssetDatabase.FindAssets("t:GameplayWaveRules", new[] { LimboReferenceAssets.ResourcesRoot }))
            {
                var so = new SerializedObject(AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(AssetDatabase.GUIDToAssetPath(guid)));
                so.FindProperty("referenceFormationWarning").objectReferenceValue = ps; so.ApplyModifiedPropertiesWithoutUndo();
            }
            string barrierPath = LimboSpatialAssets.Root + "/ReferenceBarrier.prefab";
            var barrier = PrefabUtility.LoadPrefabContents(barrierPath);
            try
            {
                var so = new SerializedObject(barrier.GetComponent<AstralShift.HellMaiden.Combat.Traps.BarrierTrap>());
                so.FindProperty("particleSystem").objectReferenceValue = ps; so.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(barrier, barrierPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(barrier); }
        }

        private static void RestoreImpBullet()
        {
            string path = LimboImpAssets.BulletPath;
            if (Done(AssetDatabase.LoadAssetAtPath<GameObject>(path))) return;
            var root = PrefabUtility.LoadPrefabContents(path);
            try { OverlayVisuals(Template("EnemyBulletAttackImp"), root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Mark(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        }

        private static void CreateEffectFixture()
        {
            string timelinePath = Root + "/Effects/Display.playable";
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(timelinePath);
            if (timeline == null)
            {
                timeline = ScriptableObject.CreateInstance<TimelineAsset>(); AssetDatabase.CreateAsset(timeline,timelinePath);
                var track = timeline.CreateTrack<NetworkEnemySpawnTrack>(); var clip = track.CreateClip<NetworkEnemySpawnClip>();
                clip.start=800; clip.duration=1; // Required compiler input, outside this empty 24s visual run.
                var spawn = (NetworkEnemySpawnClip)clip.asset;
                spawn.enemyPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(LimboStage2Assets.Root+"/ReferenceBrotchi.prefab");
                spawn.sourceEnemy="Brotchi"; spawn.sourceVariant=0; spawn.count=1;
                spawn.referenceMode=ReferenceSpawnMode.CurveBudget;
                spawn.referenceReadiness=ReferenceEnemyReadiness.Ready;
                EditorUtility.SetDirty(spawn); EditorUtility.SetDirty(track); EditorUtility.SetDirty(timeline);
            }
            foreach(var clip in timeline.GetOutputTracks().SelectMany(t=>t.GetClips()))
                if(clip.start==1000 && clip.asset is NetworkEnemySpawnClip invalid && invalid.referenceMode==ReferenceSpawnMode.None)
                { clip.start=800; invalid.referenceMode=ReferenceSpawnMode.CurveBudget; EditorUtility.SetDirty(invalid); EditorUtility.SetDirty(timeline); }
            string rulesPath=LimboReferenceAssets.ResourcesRoot+"/ArtEffects.asset";
            if (!File.Exists(rulesPath))
            {
                var rules=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/Opening.asset"));
                rules.name="ArtEffects"; var so=new SerializedObject(rules);
                so.FindProperty("timeline").objectReferenceValue=timeline;
                so.FindProperty("referenceEndTime").doubleValue=24; so.FindProperty("referenceValidationOnly").boolValue=true;
                so.FindProperty("barriers").arraySize=0; so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.CreateAsset(rules,rulesPath);
            }
            string prefabPath=LimboReferenceAssets.ResourcesRoot+"/ArtEffectDisplay.prefab";
            if (File.Exists(prefabPath)) return;
            var root=new GameObject("Limbo visual-only effect display");
            try
            {
                var so=new SerializedObject(root.AddComponent<LimboArtEffectFixture>());var effects=so.FindProperty("effects");effects.arraySize=3;
                string[] names={"Ghoul_Warning","soul enemy warning","Enemy_Bomb_ExplosionAttack 1"};
                for(int i=0;i<3;i++)effects.GetArrayElementAtIndex(i).objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Effects/"+names[i]+".prefab");
                so.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
            }
            finally {Object.DestroyImmediate(root);}
        }
    }
}
