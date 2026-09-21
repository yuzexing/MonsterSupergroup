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
    public static class LimboLostSoulAssets
    {
        public const string Root = LimboReferenceAssets.Root + "/LostSoul";
        public const string EnemyPath = Root + "/ReferenceLostSoul.prefab";
        public const string ExplosionPath = Root + "/ReferenceLostSoulExplosion.prefab";
        [Serializable] public class Polygon { public Vector2[] points; }
        [Serializable] public class Geometry
        {
            public string group, path, type; public bool trigger; public float radius;
            public float[] offset, size, localPosition, localScale, localRotation; public Polygon[] polygons;
        }
        [Serializable] public class Data
        {
            public string sourceSha256; public float cooldown, triggerDistance, chaseSpeed;
            public double previewEnd; public LimboDashAssets.Times extraStageTimes; public Geometry[] geometry;
        }
        [MenuItem("Tools/MonsterSupergroup/Limbo/Create LostSoul reference assets")]
        public static void Create()
        {
            EnemyDefinitionMigration.EnsureLegacyWriterAllowed("LimboLostSoulAssets.Create");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var data = JsonUtility.FromJson<Data>(File.ReadAllText(Root+"/LostSoulAdapted.json"));
            CreateExplosion(data); CreateEnemy(data);
            LimboArtAssets.Apply(false);
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(LimboReferenceAssets.Root+"/Limbo.playable");
            foreach (var clip in timeline.GetOutputTracks().SelectMany(t=>t.GetClips()))
            {
                if (clip.asset is not NetworkEnemySpawnClip spawn || spawn.sourceEnemy!="LostSoul") continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath);
                if (spawn.enemyPrefab == prefab && spawn.referenceReadiness == ReferenceEnemyReadiness.Ready) continue;
                spawn.enemyPrefab=prefab; spawn.contactRadius=0; spawn.referenceReadiness=ReferenceEnemyReadiness.ValidationPending;
                spawn.missingEvidence="Recovered LostSoul integrated; rendered behavior validation pending.";
                spawn.recoveredEvidence="docs/evidence/hellmaiden-attacks/runtime-assets.json#o00037";
                EditorUtility.SetDirty(spawn);
            }
            double end=timeline.GetOutputTracks().SelectMany(t=>t.GetClips()).Where(c=>((NetworkEnemySpawnClip)c.asset).sourceEnemy=="Ghoul").Min(c=>c.start);
            if(Math.Abs(end-data.previewEnd)>.000001)throw new InvalidDataException("LostSoul preview endpoint differs from first Ghoul clip.");
            MakeRules("LostSoul", timeline, end, false, false);
            MakeRules("LostSoulValidation", timeline, end, true, false);
            for (int variant=0;variant<2;variant++)
            {
                var fixture=Fixture("Fixture"+variant, timeline, variant, false);
                MakeRules("LostSoulFixture"+variant,fixture,155,true,true);
            }
            MakeRules("LostSoulBurst",Fixture("Burst",timeline,1,true),35,true,true);
            MakeRules("LostSoulLimited",Fixture("Limited",timeline,0,false,true),40,true,true);
            MakeRules("LostSoulBoundary",Fixture("Boundary",timeline,0,false,false,40),45,true,true);
            MakeRules("LostSoulReuse",VariantSequence(timeline),40,true,true);
            MakeRules("LostSoulExpiry",Fixture("Expiry",timeline,0,false,false,5),45,true,true);
            bool newBarrier=!File.Exists(LimboReferenceAssets.ResourcesRoot+"/LostSoulBarrier.asset");
            MakeRules("LostSoulBarrier",Fixture("Barrier",timeline,1,false,false,50),60,true,true);
            if(newBarrier)
            {
                var rules=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/LostSoulBarrier.asset");
                var sourceRules=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/DashValidation.asset");
                sourceRules.TryCapture(out var captured,out _);var b=captured.Reference.Barriers[0];b.start=5;b.end=50;
                var so=new SerializedObject(rules);var row=so.FindProperty("barriers");row.arraySize=1;
                var item=row.GetArrayElementAtIndex(0);
                item.FindPropertyRelative("start").doubleValue=b.start;item.FindPropertyRelative("end").doubleValue=b.end;
                item.FindPropertyRelative("minimumRadius").floatValue=b.minimumRadius;item.FindPropertyRelative("maximumRadius").floatValue=b.maximumRadius;
                item.FindPropertyRelative("shrinkDuration").floatValue=b.shrinkDuration;item.FindPropertyRelative("sides").intValue=b.sides;
                item.FindPropertyRelative("lifecycleVersion").intValue=b.lifecycleVersion;item.FindPropertyRelative("readiness").enumValueIndex=(int)ReferenceEnemyReadiness.ValidationPending;
                item.FindPropertyRelative("readinessNote").stringValue="Explicit original barrier shifted to 5 seconds for LostSoul clock integration.";
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
        }
        private static TimelineAsset Fixture(string name,TimelineAsset source,int variant,bool burst,bool limited=false,double duration=150)
        {
            string path=Root+"/"+name+".playable";
            var result=AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            if(result!=null)return result;
            result=ScriptableObject.CreateInstance<TimelineAsset>();AssetDatabase.CreateAsset(result,path);
            var original=source.GetOutputTracks().SelectMany(t=>t.GetClips()).Single(c=>Math.Abs(c.start-(burst?540.2833333333333:389.6))<.00001);
            var track=result.CreateTrack<NetworkEnemySpawnTrack>();var clip=track.CreateClip<NetworkEnemySpawnClip>();
            EditorUtility.CopySerialized(original.asset,clip.asset);clip.start=1;clip.duration=burst||limited?original.duration:duration;
            var spawn=(NetworkEnemySpawnClip)clip.asset;spawn.sourceVariant=variant;spawn.expiresOffscreen=false;
            if(!burst&&!limited)spawn.count=1;
            if(name=="Expiry"){spawn.referenceMode=ReferenceSpawnMode.CurveBudget;spawn.spawnCurve=AnimationCurve.Linear(0,1,1,1);spawn.expiresOffscreen=true;}
            spawn.referenceReadiness=ReferenceEnemyReadiness.ValidationPending;
            spawn.missingEvidence="Explicit LostSoul mechanism fixture; source segment shifted to 1 second; single-enemy fixture changes target to 1.";
            EditorUtility.SetDirty(spawn);EditorUtility.SetDirty(track);EditorUtility.SetDirty(result);return result;
        }
        private static TimelineAsset VariantSequence(TimelineAsset source)
        {
            string path=Root+"/Reuse.playable";
            var result=AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);if(result!=null)return result;
            result=ScriptableObject.CreateInstance<TimelineAsset>();AssetDatabase.CreateAsset(result,path);
            var original=source.GetOutputTracks().SelectMany(t=>t.GetClips()).Single(c=>Math.Abs(c.start-389.6)<.00001);
            var track=result.CreateTrack<NetworkEnemySpawnTrack>();
            for(int i=0;i<3;i++)
            {
                var clip=track.CreateClip<NetworkEnemySpawnClip>();EditorUtility.CopySerialized(original.asset,clip.asset);
                clip.start=1+12*i;clip.duration=8;var spawn=(NetworkEnemySpawnClip)clip.asset;
                spawn.sourceVariant=i==1?1:0;spawn.count=1;spawn.expiresOffscreen=false;
                spawn.referenceReadiness=ReferenceEnemyReadiness.ValidationPending;
                spawn.missingEvidence="Explicit v0/v1/v0 sequence for shared art and attack-pool validation; not a source wave.";
                EditorUtility.SetDirty(spawn);
            }
            EditorUtility.SetDirty(track);EditorUtility.SetDirty(result);return result;
        }
        private static void MakeRules(string name,TimelineAsset timeline,double end,bool validation,bool isolated)
        {
            string path=LimboReferenceAssets.ResourcesRoot+"/"+name+".asset";
            if(File.Exists(path))
            {
                if(name=="LostSoul"||name=="LostSoulValidation")
                {
                    var existing=new SerializedObject(AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(path));
                    if(Math.Abs(existing.FindProperty("referenceEndTime").doubleValue-479.9166666666667)<.000001)
                    {existing.FindProperty("referenceEndTime").doubleValue=end;existing.ApplyModifiedPropertiesWithoutUndo();}
                }
                return;
            }
            var rules=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/DashValidation.asset"));
            rules.name=name;AssetDatabase.CreateAsset(rules,path);var so=new SerializedObject(rules);
            so.FindProperty("timeline").objectReferenceValue=timeline;so.FindProperty("referenceEndTime").doubleValue=end;
            so.FindProperty("referenceValidationOnly").boolValue=validation;
            if(isolated)so.FindProperty("barriers").arraySize=0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void CreateExplosion(Data data)
        {
            if(File.Exists(ExplosionPath))return;
            var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(LimboArtAssets.Root+"/Effects/Enemy_Bomb_ExplosionAttack 1.prefab"));
            try
            {
                root.name="ReferenceLostSoulExplosion";
                var old=root.transform.Find("Collider");if(old!=null)Object.DestroyImmediate(old.gameObject);
                var template=AssetDatabase.LoadAssetAtPath<GameObject>(LimboStage2Assets.Root+"/SkeletonAttack.prefab").GetComponent<EnemyAttackPrefab>();
                var hit=Object.Instantiate(template.damageInteraction.gameObject,root.transform);hit.name="Collider";
                var attack=root.AddComponent<EnemyExplosionAttackVFX>();
                attack.colliders=hit;attack.damageInteraction=hit.GetComponent<PlayerDamageInteraction>();
                attack.particleSystem=root.transform.Find("Sparks").GetComponent<ParticleSystem>();
                foreach(var g in data.geometry.Where(g=>g.group=="explosion"))ApplyGeometry(root,g);
                foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {var main=ps.main;main.useUnscaledTime=false;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;}
                hit.SetActive(false);PrefabUtility.SaveAsPrefabAsset(root,ExplosionPath);
            }
            finally{Object.DestroyImmediate(root);}
        }
        private static void CreateEnemy(Data data)
        {
            if(File.Exists(EnemyPath)){RemoveContact();return;}
            AssetDatabase.CopyAsset(LimboStage2Assets.Root+"/ReferenceSkeleton.prefab",EnemyPath);
            AssetDatabase.SetLabels(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath),Array.Empty<string>());
            var root=PrefabUtility.LoadPrefabContents(EnemyPath);
            try
            {
                root.name="ReferenceLostSoul";var controller=root.GetComponent<EnemyController>();
                foreach(var old in root.GetComponents<EnemyAttack>())Object.DestroyImmediate(old);
                var attack=root.AddComponent<EnemyAttackExplosion>();controller.attackScript=attack;
                controller.selectedName="LostSoul";controller.attackCooldown=data.cooldown;controller.attackDistance=data.triggerDistance;
                var so=new SerializedObject(controller);so.FindProperty("stopForAttack").boolValue=false;
                so.FindProperty("facingPlayerDuringWarning").boolValue=false;so.FindProperty("facingPlayerDuringAttack").boolValue=false;
                so.FindProperty("attackOnDeath").boolValue=false;so.FindProperty("stoppingDistance").floatValue=.1f;so.ApplyModifiedPropertiesWithoutUndo();
                var point=new GameObject("ExplosionPosition").transform;point.SetParent(root.transform,false);
                so=new SerializedObject(attack);
                so.FindProperty("_attackWarningPrefab").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>(LimboArtAssets.Root+"/Effects/soul enemy warning.prefab").GetComponent<EnemyAttackWarning>();
                so.FindProperty("_attackVFXPrefab").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>(ExplosionPath).GetComponent<EnemyExplosionAttackVFX>();
                so.FindProperty("explosionPosition").objectReferenceValue=point;so.FindProperty("chaseSpeed").floatValue=data.chaseSpeed;
                so.FindProperty("warningTime").floatValue=data.extraStageTimes.warningTime;so.FindProperty("attackTime").floatValue=data.extraStageTimes.attackTime;
                so.FindProperty("recoveryTime").floatValue=data.extraStageTimes.recoveryTime;so.ApplyModifiedPropertiesWithoutUndo();
                foreach(var g in data.geometry.Where(g=>g.group=="body"))ApplyGeometry(root,g);
                so=new SerializedObject(root.GetComponent<NetworkIdentity>());
                so.FindProperty("_assetId").longValue=NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(EnemyPath)));so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root,EnemyPath);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            RemoveContact();
        }
        private static void RemoveContact()
        {
            var root=PrefabUtility.LoadPrefabContents(EnemyPath);
            try
            {
                var contact=root.GetComponent<EnemyContactDamage>();
                if(contact==null)return;
                if(contact.DamageInteraction!=null)Object.DestroyImmediate(contact.DamageInteraction.gameObject);
                Object.DestroyImmediate(contact);PrefabUtility.SaveAsPrefabAsset(root,EnemyPath);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        internal static void ApplyGeometry(GameObject root,Geometry g)
        {
            var t=g.path==""?root.transform:root.transform.Find(g.path);
            if(t==null)throw new InvalidDataException("LostSoul geometry path missing: "+g.path);
            if(g.type=="Transform")
            {
                if(g.path!="")t.localPosition=new Vector3(g.localPosition[0],g.localPosition[1],g.localPosition[2]);
                t.localScale=new Vector3(g.localScale[0],g.localScale[1],g.localScale[2]);
                t.localRotation=new Quaternion(g.localRotation[0],g.localRotation[1],g.localRotation[2],g.localRotation[3]);
            }
            else
            {
                var c=t.GetComponent<Collider2D>();c.offset=new Vector2(g.offset[0],g.offset[1]);c.isTrigger=g.trigger;
                if(c is CircleCollider2D circle)circle.radius=g.radius;
                if(c is BoxCollider2D box)box.size=new Vector2(g.size[0],g.size[1]);
                if(c is PolygonCollider2D polygon){polygon.pathCount=g.polygons.Length;for(int i=0;i<g.polygons.Length;i++)polygon.SetPath(i,g.polygons[i].points);}
            }
        }
        // Explicit reviewed acceptance, separate from asset creation and fixture execution.
        [MenuItem("Tools/MonsterSupergroup/Limbo/Approve recorded LostSoul validation")]
        public static void MarkValidated()
        {
            var timeline=AssetDatabase.LoadAssetAtPath<TimelineAsset>(LimboReferenceAssets.Root+"/Limbo.playable");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath);
            foreach(var spawn in timeline.GetOutputTracks().SelectMany(t=>t.GetClips()).Select(c=>(NetworkEnemySpawnClip)c.asset))
            {
                if(spawn.sourceEnemy!="LostSoul"||spawn.referenceReadiness!=ReferenceEnemyReadiness.ValidationPending)continue;
                if(spawn.enemyPrefab!=prefab)throw new InvalidDataException("LostSoul acceptance must reference the reviewed prefab.");
                spawn.referenceReadiness=ReferenceEnemyReadiness.Ready;spawn.missingEvidence="";EditorUtility.SetDirty(spawn);
            }
            AssetDatabase.SaveAssets();
            var preview=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/LostSoul.asset");
            if(!preview.TryCapture(out var p,out var error)||p.Reference.ReadinessError()!=null)
                throw new InvalidDataException("LostSoul preview gate: "+error+" / "+p.Reference?.ReadinessError());
            var full=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/Full.asset");
            if(!full.TryCapture(out var f,out error)) throw new InvalidDataException(error);
            LimboFullAssets.VerifyIndependentFlowGate(f.Reference);
            Debug.Log("[LimboLostSoul] Reviewed LostSoul behavior accepted; independent Full flow readiness preserved. See docs/limbo-lostsoul-integration.md.");
        }
        public static void AcceptBatch(){int code=0;try{MarkValidated();}catch(Exception e){Debug.LogException(e);code=1;}finally{EditorApplication.Exit(code);}}
        public static void CreateBatch(){int code=0;try{Create();}catch(Exception e){Debug.LogException(e);code=1;}finally{EditorApplication.Exit(code);}}
        public static void BuildBatch()
        {
            int code=0;
            try
            {
                string output=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("--limbo-build="))?.Substring(14)??"Builds/LimboLostSoul20260915/MonsterSupergroupLimbo.exe";
                MonsterSupergroup.EditorTools.ProjectBuildService.Build("kcp-development",output);
            }
            catch(Exception e){Debug.LogException(e);code=1;}
            finally{EditorApplication.Exit(code);}
        }
    }
}
