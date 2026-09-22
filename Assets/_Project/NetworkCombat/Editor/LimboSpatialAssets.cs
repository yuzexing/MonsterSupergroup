using System;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.Combat.Traps;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class LimboSpatialAssets
    {
        public const string Root = LimboReferenceAssets.Root + "/Spatial";
        [Serializable] public class Curve { public int mode; public float minimum, maximum; }
        [Serializable] public class Particle { public string sourceId; public float duration; public Curve lifetime, speed, size, rate; }
        [Serializable] public class Data
        {
            public int sides;
            public float minimum, maximum, shrinkDuration, entryDuration, particleRadius, cameraOffset,
                cameraDuration, cameraWait, edgeRadius, entryScale, effectsDelay, activationDelay;
            public Particle[] particles;
        }
        private const string Pending = "Spatial lifecycle integrated; rendered Host/Client validation pending.";

        [MenuItem("Tools/MonsterSupergroup/Limbo/Create spatial validation assets")]
        public static void Create()
        {
            AssetDatabase.Refresh();
            var data = JsonUtility.FromJson<Data>(File.ReadAllText(Root + "/SpatialAdapted.json"));
            var warning = CreateParticles(data);
            var barrier = CreateBarrier(data, warning);
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(LimboReferenceAssets.Root + "/Limbo.playable");
            foreach (var clip in timeline.GetOutputTracks().SelectMany(t => t.GetClips()).Select(c => (NetworkEnemySpawnClip)c.asset))
            {
                if (clip.referenceMode != ReferenceSpawnMode.FormationBurst) continue;
                if (clip.referenceSpawnReadiness == ReferenceEnemyReadiness.ImplementationPending)
                {
                    clip.referenceSpawnReadiness = ReferenceEnemyReadiness.ValidationPending;
                    clip.spawnReadinessNote = Pending; EditorUtility.SetDirty(clip);
                }
            }
            foreach (string guid in AssetDatabase.FindAssets("t:GameplayWaveRules", new[] { LimboReferenceAssets.ResourcesRoot }))
            {
                var rules = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(AssetDatabase.GUIDToAssetPath(guid));
                BindSpatial(rules, warning, barrier, data);
            }
            foreach (string name in new[] { "SpatialB", "SpatialBarrier", "SpatialOverlap" })
                CreateFixture(name, timeline, warning, barrier, data);
            CreateRepositionFixture(timeline);
            CreateRepositionBoundaryFixtures();
            CreateOverlapWaitFixture();
            AssetDatabase.SaveAssets();
            Debug.Log("[LimboSpatial] validation-only assets prepared; no rendered validation claimed; Full remains gated.");
        }

        private static ParticleSystem.MinMaxCurve Value(Curve c) => c.mode == 3
            ? new ParticleSystem.MinMaxCurve(c.minimum, c.maximum) : new ParticleSystem.MinMaxCurve(c.maximum);

        private static ParticleSystem CreateParticles(Data data)
        {
            string path = Root + "/ReferenceTrapWarning.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing.GetComponent<ParticleSystem>();
            var root = new GameObject("ReferenceTrapWarning");
            try
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Content/HellMaiden/NativeGAS/Dante/Ultimate/Material/Fire_Particles_Fast.mat");
                for (int i = 0; i < data.particles.Length; i++)
                {
                    var go = i == 0 ? root : new GameObject("NumericParticle" + i);
                    if (i != 0) go.transform.SetParent(root.transform, false);
                    if (i == 3) go.transform.localPosition = new Vector3(0, .56f, 0);
                    var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var p = data.particles[i]; var main = ps.main;
                    main.duration = p.duration; main.loop = true; main.playOnAwake = false; main.useUnscaledTime = false;
                    main.startLifetime = Value(p.lifetime); main.startSpeed = Value(p.speed); main.startSize = Value(p.size);
                    main.startColor = new Color(1, .32f, .07f, .5f); main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    var emission = ps.emission; emission.rateOverTime = Value(p.rate); emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
                    var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = .35f;
                    var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
                    renderer.sortingLayerName = "EnemyAttack"; renderer.sortingOrder = 10;
                }
                return PrefabUtility.SaveAsPrefabAsset(root, path).GetComponent<ParticleSystem>();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static BarrierTrap CreateBarrier(Data d, ParticleSystem warning)
        {
            string path = Root + "/ReferenceBarrier.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing.GetComponent<BarrierTrap>();
            var root = new GameObject("ReferenceBarrier");
            try
            {
                var child = new GameObject("TrapEdges"); child.transform.SetParent(root.transform, false); child.layer = 22;
                child.transform.localRotation = Quaternion.Euler(45, 0, 0);
                var edge = child.AddComponent<EdgeCollider2D>(); edge.edgeRadius = d.edgeRadius; edge.excludeLayers = 448;
                edge.points = Array.Empty<Vector2>(); edge.enabled = false;
                var trap = root.AddComponent<BarrierTrap>(); var so = new SerializedObject(trap);
                so.FindProperty("trapTransform").objectReferenceValue = child.transform;
                so.FindProperty("_collider").objectReferenceValue = edge; so.FindProperty("particleSystem").objectReferenceValue = warning;
                so.FindProperty("numberOfSides").intValue = d.sides;
                foreach (var value in new[] { ("minRadius",d.minimum), ("maxRadius",d.maximum), ("shrinkDuration",d.shrinkDuration),
                    ("spawnAnimationDuration",d.entryDuration), ("particleSystemRadius",d.particleRadius),
                    ("onSpawnCameraTargetOffset",d.cameraOffset), ("onSpawnCameraTargetDuration",d.cameraDuration), ("onSpawnCameraFramingTimeout",d.cameraWait) })
                    so.FindProperty(value.Item1).floatValue = value.Item2;
                so.FindProperty("destroyOnShrink").boolValue = true;
                so.FindProperty("targetPlayer").boolValue = false; so.FindProperty("hasSlowMo").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, path).GetComponent<BarrierTrap>();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void BindSpatial(GameplayWaveRules rules, ParticleSystem warning, BarrierTrap barrier, Data data)
        {
            var so = new SerializedObject(rules);
            if (so.FindProperty("referenceFormationWarning").objectReferenceValue == null) so.FindProperty("referenceFormationWarning").objectReferenceValue = warning;
            if (so.FindProperty("referenceBarrier").objectReferenceValue == null) so.FindProperty("referenceBarrier").objectReferenceValue = barrier;
            // Existing adapted rule parameters are never rewritten by a Create pass.
            var barriers = so.FindProperty("barriers");
            for (int i = 0; i < barriers.arraySize; i++)
            {
                var b = barriers.GetArrayElementAtIndex(i);
                if (b.FindPropertyRelative("lifecycleVersion").intValue != 0) continue;
                b.FindPropertyRelative("lifecycleVersion").intValue = 1;
                b.FindPropertyRelative("readiness").enumValueIndex = (int)ReferenceEnemyReadiness.ValidationPending;
                b.FindPropertyRelative("readinessNote").stringValue = Pending;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateFixture(string name, TimelineAsset original, ParticleSystem warning, BarrierTrap barrier, Data data)
        {
            string timelinePath = Root + "/" + name + ".playable";
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(timelinePath);
            if (timeline == null)
            {
                timeline = ScriptableObject.CreateInstance<TimelineAsset>(); AssetDatabase.CreateAsset(timeline, timelinePath);
                // A valid captured program needs at least one enemy clip. The barrier-only clip is after its cutoff.
                var source = original.GetOutputTracks().SelectMany(t => t.GetClips()).Single(c => Math.Abs(c.start - 635.6666666666666) < .001);
                foreach (double start in name == "SpatialOverlap" ? new[] { 1.0, 3.0, 25.0, 65.0 } : new[] { name == "SpatialBarrier" ? 90.0 : 1.0 })
                {
                    var track = timeline.CreateTrack<NetworkEnemySpawnTrack>(); var clip = track.CreateClip<NetworkEnemySpawnClip>();
                    EditorUtility.CopySerialized(source.asset, clip.asset); clip.start = start; clip.duration = name == "SpatialOverlap" && start == 1 ? 15 : source.duration;
                    var spawn = (NetworkEnemySpawnClip)clip.asset;
                    spawn.referenceReadiness = ReferenceEnemyReadiness.Ready; spawn.missingEvidence = "";
                    spawn.referenceSpawnReadiness = ReferenceEnemyReadiness.ValidationPending; spawn.spawnReadinessNote = Pending;
                    EditorUtility.SetDirty(spawn); EditorUtility.SetDirty(track);
                }
                EditorUtility.SetDirty(timeline);
            }
            string path = LimboReferenceAssets.ResourcesRoot + "/" + name + ".asset";
            var rules = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(path);
            if (rules != null) return;
            rules = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot + "/Opening.asset"));
            AssetDatabase.CreateAsset(rules, path); var so = new SerializedObject(rules);
            so.FindProperty("timeline").objectReferenceValue = timeline;
            so.FindProperty("referenceValidationOnly").boolValue = true; so.FindProperty("referenceEndTime").doubleValue = name == "SpatialB" ? 12 : 85;
            var barriers = so.FindProperty("barriers"); barriers.arraySize = name == "SpatialB" ? 0 : 1;
            if (barriers.arraySize != 0)
            {
                var b = barriers.GetArrayElementAtIndex(0); double start = name == "SpatialBarrier" ? 1 : 17;
                b.FindPropertyRelative("start").doubleValue = start; b.FindPropertyRelative("end").doubleValue = start + 45;
            }
            so.ApplyModifiedPropertiesWithoutUndo(); BindSpatial(rules, warning, barrier, data);
        }

        private static void CreateRepositionFixture(TimelineAsset original)
        {
            string path = Root + "/SpatialReposition.playable";
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            if (timeline == null)
            {
                timeline = ScriptableObject.CreateInstance<TimelineAsset>(); AssetDatabase.CreateAsset(timeline, path);
                foreach (var identity in new[] { "Skeleton", "Elite_Skeleton" })
                {
                    var source = original.GetOutputTracks().SelectMany(t => t.GetClips()).First(c =>
                        ((NetworkEnemySpawnClip)c.asset).sourceEnemy == identity && ((NetworkEnemySpawnClip)c.asset).sourceVariant == 0);
                    var track = timeline.CreateTrack<NetworkEnemySpawnTrack>(); var clip = track.CreateClip<NetworkEnemySpawnClip>();
                    EditorUtility.CopySerialized(source.asset, clip.asset); clip.start = 1; clip.duration = 1;
                    var spawn = (NetworkEnemySpawnClip)clip.asset; spawn.count = 1; spawn.spawnCurve = AnimationCurve.Constant(0, 1, 1);
                    spawn.expiresOffscreen = false; spawn.referenceReadiness = ReferenceEnemyReadiness.ValidationPending;
                    spawn.missingEvidence = "Explicit position/health fixture; not normal gameplay.";
                    EditorUtility.SetDirty(spawn); EditorUtility.SetDirty(track);
                }
                EditorUtility.SetDirty(timeline);
            }
            path = LimboReferenceAssets.ResourcesRoot + "/SpatialReposition.asset";
            if (AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(path) != null) return;
            var rules = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot + "/Opening.asset"));
            AssetDatabase.CreateAsset(rules, path); var so = new SerializedObject(rules);
            so.FindProperty("timeline").objectReferenceValue = timeline; so.FindProperty("referenceEndTime").doubleValue = 55;
            so.FindProperty("referenceValidationOnly").boolValue = true; so.FindProperty("barriers").arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void CreateAndBuild()
        {
            Debug.LogWarning("[ProjectTools] CreateAndBuild now builds existing Limbo assets only; spatial maintenance must be invoked separately.");
            LimboReferenceAssets.BuildPlayer();
        }

        private static void CreateRepositionBoundaryFixtures()
        {
            foreach (string name in new[] { "SpatialRepositionExpiry", "SpatialRepositionFraming" })
            {
                string rulePath = LimboReferenceAssets.ResourcesRoot + "/" + name + ".asset";
                if (AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(rulePath) != null) continue;
                string timelinePath = Root + "/" + name + ".playable";
                if (!File.Exists(timelinePath)) AssetDatabase.CopyAsset(Root + "/SpatialReposition.playable", timelinePath);
                var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(timelinePath);
                if (name.EndsWith("Expiry"))
                    foreach (var c in timeline.GetOutputTracks().SelectMany(t => t.GetClips()))
                    {
                        var spawn = (NetworkEnemySpawnClip)c.asset;
                        spawn.expiresOffscreen = spawn.sourceEnemy == "Skeleton"; EditorUtility.SetDirty(spawn);
                    }
                var rules = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot + "/SpatialReposition.asset"));
                AssetDatabase.CreateAsset(rules, rulePath); var so = new SerializedObject(rules);
                so.FindProperty("timeline").objectReferenceValue = timeline;
                if (name.EndsWith("Framing"))
                {
                    var original = new SerializedObject(AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot + "/SpatialBarrier.asset"));
                    so.CopyFromSerializedProperty(original.FindProperty("barriers"));
                    var b = so.FindProperty("barriers").GetArrayElementAtIndex(0);
                    b.FindPropertyRelative("start").doubleValue = 30; b.FindPropertyRelative("end").doubleValue = 75;
                    so.FindProperty("referenceEndTime").doubleValue = 110;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void CreateOverlapWaitFixture()
        {
            string path = LimboReferenceAssets.ResourcesRoot + "/SpatialOverlapWait.asset";
            if (AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(path) != null) return;
            var rules = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot + "/SpatialOverlap.asset"));
            AssetDatabase.CreateAsset(rules, path); var so = new SerializedObject(rules);
            var barrier = so.FindProperty("barriers").GetArrayElementAtIndex(0);
            barrier.FindPropertyRelative("start").doubleValue = 5; barrier.FindPropertyRelative("end").doubleValue = 50;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
