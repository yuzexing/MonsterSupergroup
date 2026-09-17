using System;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Interactions;
using Mirror;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class LimboGhoulAssets
    {
        public const string Root = LimboReferenceAssets.Root + "/Ghoul";
        public const string EnemyPath = Root + "/ReferenceGhoul.prefab";
        public const string AttackPath = Root + "/ReferenceGhoulAttack.prefab";
        [Serializable] public class Data
        {
            public float cooldown, triggerDistance; public int consecutiveAttacks;
            public Vector3 areaSideWarpDistance;
            public double previewEnd; public LimboDashAssets.Times extraStageTimes;
            public LimboLostSoulAssets.Geometry[] geometry;
            public WarningStepData warningStep;
        }
        [Serializable] public class WarningStepData { public string objectId, attackId, rigidbodyId; public float stepDistance; }
        [MenuItem("Tools/MonsterSupergroup/Limbo/Create Ghoul reference assets")]
        public static void Create()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var data = JsonUtility.FromJson<Data>(File.ReadAllText(Root+"/GhoulAdapted.json"));
            CreateAttack(data); CreateEnemy(data); UpdateWarningStep(); LimboArtAssets.Apply(false);
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(LimboReferenceAssets.Root+"/Limbo.playable");
            foreach(var spawn in timeline.GetOutputTracks().SelectMany(t=>t.GetClips()).Select(c=>(NetworkEnemySpawnClip)c.asset).Where(c=>c.sourceEnemy=="Ghoul"))
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath);
                if(spawn.enemyPrefab==prefab && spawn.referenceReadiness==ReferenceEnemyReadiness.Ready)continue;
                spawn.enemyPrefab=prefab;spawn.contactRadius=0;spawn.referenceReadiness=ReferenceEnemyReadiness.ValidationPending;
                spawn.missingEvidence="Ghoul absolute combo and original art integrated; rendered validation pending.";
                spawn.recoveredEvidence="docs/evidence/hellmaiden-attacks/runtime-assets.json#o00138";EditorUtility.SetDirty(spawn);
            }
            // Enemy acceptance must never implicitly approve the unfinished end-of-level flow.
            var full=new SerializedObject(AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/Full.asset"));
            if (full.FindProperty("referenceEndPolicy").enumValueIndex == (int)ReferenceEndPolicy.ImmediatePreview)
            {
                full.FindProperty("referenceFlowReadiness").enumValueIndex=(int)ReferenceEnemyReadiness.ImplementationPending;
                full.FindProperty("referenceFlowReadinessNote").stringValue="720.9-second busy/transition flow and full-run acceptance pending.";full.ApplyModifiedPropertiesWithoutUndo();
            }
            Rules("Ghoul",timeline,data.previewEnd,false,false);Rules("GhoulValidation",timeline,data.previewEnd,true,false);
            Rules("GhoulFixture",Fixture(timeline,"Fixture",449.9166666666667,1,210),215,true,true);
            Rules("GhoulBoundary",Fixture(timeline,"Boundary",449.9166666666667,1,40),45,true,true);
            Rules("GhoulInterrupt",Fixture(timeline,"Interrupt",449.9166666666667,1,55),60,true,true);
            Rules("GhoulLimited",Fixture(timeline,"Limited",449.9166666666667,30,30),40,true,true);
            Rules("GhoulCurve",Fixture(timeline,"Curve",479.9166666666667,50,30),40,true,true);
            Rules("GhoulImpBurst",Fixture(timeline,"ImpBurst",480.5,10,15),25,true,true);
            Rules("GhoulSoulBurst",Fixture(timeline,"SoulBurst",540.2833333333333,10,15),25,true,true);
            Rules("GhoulRusher",Fixture(timeline,"Rusher",509.65,80,30),40,true,true);
            AssetDatabase.SaveAssets();
        }
        private static void CreateAttack(Data data)
        {
            if(File.Exists(AttackPath))return;
            var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(LimboArtAssets.Root+"/Effects/Ghoul_Warning.prefab"));
            try
            {
                root.name="ReferenceGhoulAttack";
                var empty=root.transform.Find("AttackWarning/Collider");if(empty!=null)Object.DestroyImmediate(empty.gameObject);
                var template=AssetDatabase.LoadAssetAtPath<GameObject>(LimboStage2Assets.Root+"/SkeletonAttack.prefab").GetComponent<EnemyAttackPrefab>();
                var hit=Object.Instantiate(template.damageInteraction.gameObject,root.transform.Find("AttackWarning"));hit.name="Collider";
                var attack=root.AddComponent<EnemyAttackPrefab>();attack.damageInteraction=hit.GetComponent<PlayerDamageInteraction>();
                attack.attackWarning=root.GetComponent<MeleeAttackWarning>();
                foreach(var g in data.geometry.Where(g=>g.group=="attack"))LimboLostSoulAssets.ApplyGeometry(root,g);
                root.transform.position=Vector3.zero;hit.SetActive(false);PrefabUtility.SaveAsPrefabAsset(root,AttackPath);
            }
            finally{Object.DestroyImmediate(root);}
        }
        private static void CreateEnemy(Data data)
        {
            if(File.Exists(EnemyPath))return;
            AssetDatabase.CopyAsset(LimboStage2Assets.Root+"/ReferenceSkeleton.prefab",EnemyPath);
            AssetDatabase.SetLabels(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath),Array.Empty<string>());
            var root=PrefabUtility.LoadPrefabContents(EnemyPath);
            try
            {
                root.name="ReferenceGhoul";var controller=root.GetComponent<EnemyController>();
                foreach(var old in root.GetComponents<EnemyAttack>())Object.DestroyImmediate(old);
                var attack=root.AddComponent<SequenceEnemyAttack>();controller.attackScript=attack;attack.consecutiveAttacks=data.consecutiveAttacks;
                attack.areaSideWarpDistance=data.areaSideWarpDistance;
                attack.attackPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(AttackPath).GetComponent<EnemyAttackPrefab>();
                var oldAnimator=controller.enemyAnimator;var host=oldAnimator.gameObject;
                var animator=host.AddComponent<MultipleAttackAnimator>();
                EditorUtility.CopySerializedManagedFieldsOnly(oldAnimator,animator);Object.DestroyImmediate(oldAnimator);controller.enemyAnimator=animator;
                var so=new SerializedObject(animator);so.FindProperty("attack").objectReferenceValue=attack;so.FindProperty("attackSets").arraySize=3;so.ApplyModifiedPropertiesWithoutUndo();
                controller.selectedName="Ghoul";controller.attackCooldown=data.cooldown;controller.attackDistance=data.triggerDistance;
                controller.stopForAttack=true;controller.facingPlayerDuringWarning=false;controller.facingPlayerDuringAttack=false;
                so=new SerializedObject(attack);so.FindProperty("warningTime").floatValue=data.extraStageTimes.warningTime;
                so.FindProperty("attackTime").floatValue=data.extraStageTimes.attackTime;so.FindProperty("recoveryTime").floatValue=data.extraStageTimes.recoveryTime;so.ApplyModifiedPropertiesWithoutUndo();
                foreach(var g in data.geometry.Where(g=>g.group=="body"))LimboLostSoulAssets.ApplyGeometry(root,g);
                var contact=root.GetComponent<EnemyContactDamage>();if(contact!=null){if(contact.DamageInteraction!=null)Object.DestroyImmediate(contact.DamageInteraction.gameObject);Object.DestroyImmediate(contact);}
                so=new SerializedObject(root.GetComponent<NetworkIdentity>());so.FindProperty("_assetId").longValue=NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(EnemyPath)));so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root,EnemyPath);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        [MenuItem("Tools/MonsterSupergroup/Limbo/Update Ghoul warning step")]
        public static void UpdateWarningStep()
        {
            var data = JsonUtility.FromJson<Data>(File.ReadAllText(Root + "/GhoulAdapted.json"));
            if (data.warningStep == null || data.warningStep.stepDistance <= 0)
                throw new InvalidDataException("Extract the recovered Ghoul warning step before updating assets.");
            var root = PrefabUtility.LoadPrefabContents(EnemyPath);
            try
            {
                var step = root.GetComponent<EnemyWarningStep>();
                if (step == null)
                {
                    step = root.AddComponent<EnemyWarningStep>();
                    step.Configure(root.GetComponent<SequenceEnemyAttack>(), root.GetComponent<Rigidbody2D>(), data.warningStep.stepDistance);
                    // Loading a variant's contents can temporarily assign its parent's Mirror ID.
                    // Keep the reference prefab's own registration identity when saving it.
                    var identity = new SerializedObject(root.GetComponent<NetworkIdentity>());
                    identity.FindProperty("_assetId").longValue = NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(EnemyPath)));
                    identity.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, EnemyPath);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(LimboReferenceAssets.Root + "/Limbo.playable");
            Rules("GhoulMotion", Fixture(timeline,"Motion",449.9166666666667,1,60),65,true,true);
            AssetDatabase.SaveAssets();
        }
        private static TimelineAsset Fixture(TimelineAsset source,string name,double start,int count,double duration)
        {
            string path=Root+"/"+name+".playable";var result=AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);if(result!=null)return result;
            result=ScriptableObject.CreateInstance<TimelineAsset>();AssetDatabase.CreateAsset(result,path);
            var original=source.GetOutputTracks().SelectMany(t=>t.GetClips()).Single(c=>Math.Abs(c.start-start)<.000001);
            var track=result.CreateTrack<NetworkEnemySpawnTrack>();var clip=track.CreateClip<NetworkEnemySpawnClip>();EditorUtility.CopySerialized(original.asset,clip.asset);
            clip.start=1;clip.duration=duration;var spawn=(NetworkEnemySpawnClip)clip.asset;spawn.count=count;spawn.expiresOffscreen=false;
            if(spawn.sourceEnemy=="Ghoul"){spawn.referenceReadiness=ReferenceEnemyReadiness.ValidationPending;spawn.missingEvidence="Explicit Ghoul mechanism fixture.";}
            EditorUtility.SetDirty(spawn);EditorUtility.SetDirty(track);EditorUtility.SetDirty(result);return result;
        }
        private static void Rules(string name,TimelineAsset timeline,double end,bool validation,bool isolated)
        {
            string path=LimboReferenceAssets.ResourcesRoot+"/"+name+".asset";if(File.Exists(path))return;
            var rules=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/LostSoulValidation.asset"));
            rules.name=name;AssetDatabase.CreateAsset(rules,path);var so=new SerializedObject(rules);
            so.FindProperty("timeline").objectReferenceValue=timeline;so.FindProperty("referenceEndTime").doubleValue=end;so.FindProperty("referenceValidationOnly").boolValue=validation;
            so.FindProperty("referenceFlowReadiness").enumValueIndex=0;so.FindProperty("referenceFlowReadinessNote").stringValue="";
            if(isolated)so.FindProperty("barriers").arraySize=0;so.ApplyModifiedPropertiesWithoutUndo();
        }
        // Explicit acceptance after reviewing the archived rendered fixture results.
        [MenuItem("Tools/MonsterSupergroup/Limbo/Approve recorded Ghoul validation")]
        public static void MarkValidated()
        {
            var timeline=AssetDatabase.LoadAssetAtPath<TimelineAsset>(LimboReferenceAssets.Root+"/Limbo.playable");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath);
            foreach(var clip in timeline.GetOutputTracks().SelectMany(t=>t.GetClips()).Select(c=>(NetworkEnemySpawnClip)c.asset).Where(c=>c.sourceEnemy=="Ghoul"))
            {
                if(clip.enemyPrefab!=prefab||(clip.referenceReadiness!=ReferenceEnemyReadiness.ValidationPending&&clip.referenceReadiness!=ReferenceEnemyReadiness.Ready))
                    throw new InvalidDataException("Ghoul behavior must be integrated before accepting its evidence.");
                clip.referenceReadiness=ReferenceEnemyReadiness.Ready;clip.missingEvidence="";EditorUtility.SetDirty(clip);
            }
            AssetDatabase.SaveAssets();
            var preview=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/Ghoul.asset");
            if(!preview.TryCapture(out var p,out var error)||p.Reference.ReadinessError()!=null)throw new InvalidDataException("Ghoul preview gate: "+error+" / "+p.Reference?.ReadinessError());
            var full=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/Full.asset");
            if(!full.TryCapture(out var f,out error)) throw new InvalidDataException(error);
            LimboFullAssets.VerifyIndependentFlowGate(f.Reference);
            Debug.Log("[LimboGhoul] Reviewed behavior accepted; independent Full flow readiness preserved. See docs/limbo-ghoul-integration.md.");
        }
        public static void AcceptBatch(){int code=0;try{MarkValidated();}catch(Exception e){Debug.LogException(e);code=1;}finally{EditorApplication.Exit(code);}}
        public static void CreateBatch(){int code=0;try{Create();}catch(Exception e){Debug.LogException(e);code=1;}finally{EditorApplication.Exit(code);}}
        public static void UpdateWarningStepBatch(){int code=0;try{UpdateWarningStep();}catch(Exception e){Debug.LogException(e);code=1;}finally{EditorApplication.Exit(code);}}
        public static void BuildBatch()=>LimboLostSoulAssets.BuildBatch();
    }
}
