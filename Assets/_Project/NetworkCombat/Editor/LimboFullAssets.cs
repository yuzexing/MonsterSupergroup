using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class LimboFullAssets
    {
        public const string Root = LimboReferenceAssets.Root + "/Full";
        private static string RulesPath(string name) => LimboReferenceAssets.ResourcesRoot + "/" + name + ".asset";
        [MenuItem("Tools/MonsterSupergroup/Limbo/Prepare full reference flow")]
        public static void Create()
        {
            Directory.CreateDirectory(Root); AssetDatabase.Refresh();
            var full = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(RulesPath("Full"));
            var so = new SerializedObject(full);
            bool initialized = so.FindProperty("referenceEndPolicy").enumValueIndex == (int)ReferenceEndPolicy.WaitForParticipants;
            so.FindProperty("referenceEndPolicy").enumValueIndex = (int)ReferenceEndPolicy.WaitForParticipants;
            if (!initialized)
            {
                so.FindProperty("referenceFlowReadiness").enumValueIndex = (int)ReferenceEnemyReadiness.ValidationPending;
                so.FindProperty("referenceFlowReadinessNote").stringValue = "Full flow implemented; rendered transition and full-run validation pending.";
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            CreateRules("FullValidation", full, true);
            if (!File.Exists(RulesPath("FullFixture")))
            {
                var fixture = CreateRules("FullFixture", full, true);
                so = new SerializedObject(fixture);
                so.FindProperty("referenceEndTime").doubleValue = 20;
                so.FindProperty("barriers").arraySize = 0;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            CreateOverlap("FullBarrierFirst", full, 35.7, 1, 80);
            CreateOverlap("FullBurstFirst", full, 1, 1.5, 80);
            CreateOverlap("FullSlimeBurst", full, 1, -1, 12);
            AssetDatabase.SaveAssets();
        }
        private static void CreateOverlap(string name, GameplayWaveRules full, double burstStart, double barrierStart, double end)
        {
            if (File.Exists(RulesPath(name))) return;
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, Root + "/" + name + ".playable");
            var original = full.Timeline.GetOutputTracks().SelectMany(t => t.GetClips())
                .Single(c => Math.Abs(c.start - 635.6666666666667) < .000001);
            var track = timeline.CreateTrack<NetworkEnemySpawnTrack>(); var clip = track.CreateClip<NetworkEnemySpawnClip>();
            EditorUtility.CopySerialized(original.asset, clip.asset); clip.start = burstStart; clip.duration = original.duration;
            var copy = CreateRules(name, full, true); var so = new SerializedObject(copy);
            so.FindProperty("timeline").objectReferenceValue = timeline; so.FindProperty("referenceEndTime").doubleValue = end;
            var barriers = so.FindProperty("barriers");
            if (barrierStart < 0) barriers.arraySize = 0;
            else
            {
                var source = barriers.GetArrayElementAtIndex(1); double duration = source.FindPropertyRelative("end").doubleValue - source.FindPropertyRelative("start").doubleValue;
                source.FindPropertyRelative("start").doubleValue = barrierStart; source.FindPropertyRelative("end").doubleValue = barrierStart + duration;
                barriers.DeleteArrayElementAtIndex(0);
            }
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(track); EditorUtility.SetDirty(timeline);
        }
        private static GameplayWaveRules CreateRules(string name, GameplayWaveRules source, bool validation)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(RulesPath(name));
            if (existing != null) return existing;
            var copy = UnityEngine.Object.Instantiate(source); copy.name = name;
            AssetDatabase.CreateAsset(copy, RulesPath(name));
            var so = new SerializedObject(copy); so.FindProperty("referenceValidationOnly").boolValue = validation;
            so.ApplyModifiedPropertiesWithoutUndo(); return copy;
        }
        public static void VerifyIndependentFlowGate(ReferenceWaveProgram program)
        {
            // Historical enemy approval tools must not approve or demote the independently accepted flow.
            var readiness = program.FlowReadiness; var note = program.FlowReadinessNote;
            program.FlowReadiness = ReferenceEnemyReadiness.ImplementationPending;
            program.FlowReadinessNote = "Flow gate verification";
            if (program.ReadinessError()?.Contains("flow gate") != true) throw new InvalidDataException("Missing independent flow gate.");
            program.FlowReadiness = readiness; program.FlowReadinessNote = note;
        }
        [MenuItem("Tools/MonsterSupergroup/Limbo/Approve recorded full flow")]
        public static void Accept()
        {
            foreach (string name in new[] { "Full", "FullValidation" })
            {
                var rules = AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(RulesPath(name));
                if (!rules.TryCapture(out var p, out var error) || p.Reference.EndPolicy != ReferenceEndPolicy.WaitForParticipants ||
                    p.Reference.EndTime != 720.9 || p.Reference.Clips.Length != 31)
                    throw new InvalidDataException("Full flow is not configured: " + error);
                var so = new SerializedObject(rules); so.FindProperty("referenceFlowReadiness").enumValueIndex = 0;
                so.FindProperty("referenceFlowReadinessNote").stringValue = ""; so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[LimboFull] Reviewed full flow accepted; no Minos, calibration or pressure acceptance implied.");
        }
        public static void CreateBatch() => Batch(Create);
        public static void AcceptBatch() => Batch(Accept);
        private static void Batch(Action action)
        { int code = 0; try { action(); } catch (Exception e) { Debug.LogException(e); code = 1; } finally { EditorApplication.Exit(code); } }
        public static void BuildBatch() => LimboLostSoulAssets.BuildBatch();
    }
}
