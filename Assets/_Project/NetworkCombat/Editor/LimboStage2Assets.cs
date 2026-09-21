using System;
using System.Globalization;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Interactions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class LimboStage2Assets
    {
        public const string Root = LimboReferenceAssets.Root + "/Stage2";
        private const string SkeletonAnimations = "Assets/_Project/Content/NetworkCombat/Validation/HellMaiden/AnimationClip/";
        [Serializable] public class Binding { public string field, sourceClip, normalizedStart; public float length, speed, fade; public string[] eventTimes; }
        [Serializable] public class Geometry { public string path, type; public bool attack, trigger; public float radius; public float[] localPosition,localScale,localRotation,offset,size,points; }
        [Serializable] public class Enemy { public string key,identity,controllerId,animatorId; public float cooldown,distance,warningExtra,activeExtra,recoveryExtra; public bool elite,stopForAttack; public Binding[] bindings; public Geometry[] geometry; }
        [Serializable] public class Key { public string time,value,inSlope,outSlope; }
        [Serializable] public class Curve { public string path,attribute; public Key[] keys; }
        [Serializable] public class Warning { public string name; public float length; public Curve[] curves; }
        [Serializable] public class Data { public string sourceSha256; public Enemy[] enemies; public Warning[] warning; }
        public static readonly string[] Cases = { "Skeleton0","Skeleton2","Elite0","Elite1","Brotchi0","Brotchi1","Slime0","Slime1","Rusher2","Rusher1" };

        [MenuItem("Tools/MonsterSupergroup/Limbo/Create stage two assets")]
        public static void Create()
        {
            EnemyDefinitionMigration.EnsureLegacyWriterAllowed("LimboStage2Assets.Create");
            var data = JsonUtility.FromJson<Data>(File.ReadAllText(Root + "/Stage2Adapted.json"));
            AssetDatabase.Refresh();
            foreach (var warning in data.warning) CreateWarning(warning);
            foreach (var enemy in data.enemies) CreateEnemy(enemy);
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(LimboReferenceAssets.Root + "/Limbo.playable");
            foreach (var spawn in timeline.GetOutputTracks().SelectMany(t=>t.GetClips()).Select(c=>(NetworkEnemySpawnClip)c.asset))
            {
                string key = spawn.sourceEnemy == "Slime" && spawn.referenceMode == ReferenceSpawnMode.AliveTarget ? "Rusher" : spawn.sourceEnemy;
                if (!data.enemies.Any(e=>e.key==key)) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Reference"+key+".prefab");
                if (spawn.enemyPrefab != prefab || spawn.referenceReadiness != ReferenceEnemyReadiness.Ready)
                {
                    spawn.referenceReadiness = ReferenceEnemyReadiness.ValidationPending;
                    spawn.missingEvidence = spawn.referenceMode == ReferenceSpawnMode.FormationBurst
                        ? "Contact behavior alone does not validate B warning/formation lifecycle."
                        : "Recovered stage-two configuration integrated; rendered mechanism validation pending.";
                }
                spawn.enemyPrefab=prefab;
                spawn.recoveredEvidence="docs/evidence/hellmaiden-attacks/recovered-enemies.json#"+spawn.sourceEnemy;
                EditorUtility.SetDirty(spawn);
            }
            MakeRules("Stage2",timeline,209.05,false);
            MakeRules("Stage2Validation",timeline,209.05,true);
            foreach (string name in Cases)
            {
                string key = name.Substring(0,name.Length-1); int variant=name[name.Length-1]-'0';
                if(key=="Elite")key="Elite_Skeleton";
                var enemy=data.enemies.Single(e=>e.key==key);
                string path=Root+"/Fixture"+name+".playable";
                var fixture=AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
                if(fixture==null)
                {
                    fixture=ScriptableObject.CreateInstance<TimelineAsset>(); AssetDatabase.CreateAsset(fixture,path);
                    var track=fixture.CreateTrack<NetworkEnemySpawnTrack>(); var clip=track.CreateClip<NetworkEnemySpawnClip>(); clip.start=1;clip.duration=1;
                    var spawn=(NetworkEnemySpawnClip)clip.asset;
                    spawn.enemyPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Reference"+key+".prefab");
                    spawn.sourceEnemy=enemy.identity;spawn.sourceVariant=variant;spawn.referenceMode=ReferenceSpawnMode.CurveBudget;
                    spawn.count=1;spawn.spawnCurve=AnimationCurve.Constant(0,1,1);spawn.expiresOffscreen=false;spawn.resetOnReposition=!enemy.elite;
                    spawn.contactRadius=key=="Brotchi"?.55f:key=="Slime"||key=="Rusher"?.72f:0;
                    spawn.referenceReadiness=ReferenceEnemyReadiness.ValidationPending;spawn.missingEvidence="Explicit stage-two mechanism fixture.";
                    EditorUtility.SetDirty(spawn);EditorUtility.SetDirty(track);EditorUtility.SetDirty(fixture);
                }
                int high=name=="Skeleton0"?2:name=="Elite0"||name=="Brotchi0"||name=="Slime0"||name=="Rusher2"?1:-1;
                if(high>=0&&!fixture.GetOutputTracks().SelectMany(t=>t.GetClips()).Any(c=>c.start==90))
                {
                    var first=fixture.GetOutputTracks().SelectMany(t=>t.GetClips()).First();
                    var track=fixture.CreateTrack<NetworkEnemySpawnTrack>();var second=track.CreateClip<NetworkEnemySpawnClip>();
                    EditorUtility.CopySerialized(first.asset,second.asset);second.start=90;second.duration=1;
                    ((NetworkEnemySpawnClip)second.asset).sourceVariant=high;
                    EditorUtility.SetDirty(second.asset);EditorUtility.SetDirty(track);EditorUtility.SetDirty(fixture);
                }
                MakeRules("Stage2"+name,fixture,high>=0?110:95,true);
                if(high>=0)
                {
                    var fixtureRules=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/Stage2"+name+".asset");
                    var fixtureSettings=new SerializedObject(fixtureRules);fixtureSettings.FindProperty("referenceEndTime").doubleValue=110;fixtureSettings.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            string limitedPath=Root+"/FixtureRusherWave.playable";
            var limited=AssetDatabase.LoadAssetAtPath<TimelineAsset>(limitedPath);
            if(limited==null)
            {
                limited=ScriptableObject.CreateInstance<TimelineAsset>();AssetDatabase.CreateAsset(limited,limitedPath);
                var original=timeline.GetOutputTracks().SelectMany(t=>t.GetClips()).Single(c=>Math.Abs(c.start-180)<.000001);
                var track=limited.CreateTrack<NetworkEnemySpawnTrack>();var clip=track.CreateClip<NetworkEnemySpawnClip>();
                EditorUtility.CopySerialized(original.asset,clip.asset);clip.start=1;clip.duration=original.duration;
                var spawn=(NetworkEnemySpawnClip)clip.asset;spawn.referenceReadiness=ReferenceEnemyReadiness.ValidationPending;
                spawn.missingEvidence="Explicit L fixture: original 180-second segment shifted by -179 seconds; not continuous play.";
                EditorUtility.SetDirty(spawn);EditorUtility.SetDirty(track);EditorUtility.SetDirty(limited);
            }
            MakeRules("Stage2RusherWave",limited,30,true);
            AssetDatabase.SaveAssets();
            Debug.Log("[LimboStage2] independent assets created; validation gates preserved.");
        }

        private static void CreateEnemy(Enemy data)
        {
            bool melee=data.bindings.Length>0;
            string path=Root+"/Reference"+data.key+".prefab";
            if(!File.Exists(path)) AssetDatabase.CopyAsset(melee?EnemyPrefabVariantMigration.SkeletonPath:EnemyPrefabVariantMigration.BasePath,path);
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.name="Reference"+data.key;
                var controller=root.GetComponent<EnemyController>(); controller.isElite=data.elite;
                controller.attackCooldown=data.cooldown;controller.attackDistance=data.distance;
                var so=new SerializedObject(controller);
                so.FindProperty("stopForAttack").boolValue=data.stopForAttack;
                so.FindProperty("facingPlayerDuringWarning").boolValue=false;so.FindProperty("facingPlayerDuringAttack").boolValue=false;
                so.FindProperty("hasAttackAnimation").boolValue=melee;so.ApplyModifiedPropertiesWithoutUndo();
                root.GetComponent<EnemyContactDamage>().SetContactEnabled(!melee);
                if(melee)
                {
                    string attackPath=Root+"/"+data.key+"Attack.prefab";
                    if(!File.Exists(attackPath)) AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(controller.GetComponent<EnemyAttackMelee>().attackPrefab),attackPath);
                    var attackRoot=PrefabUtility.LoadPrefabContents(attackPath);
                    try
                    {
                        ApplyGeometry(attackRoot,data,true);
                        var interaction=attackRoot.GetComponentInChildren<PlayerDamageInteraction>(true);
                        interaction.damageType=AstralShift.HellMaiden.Player.Attacks.DamageType.Normal;
                        var warning=attackRoot.GetComponentInChildren<MeleeAttackWarning>(true);var ws=new SerializedObject(warning);
                        foreach(var item in new[]{("warningStart","MeleeWarning_BuildUp"),("warningEnd","MeleeWarning_BuildDown")})
                            ws.FindProperty(item.Item1).FindPropertyRelative("_Clip").objectReferenceValue=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/"+item.Item2+".anim");
                        ws.ApplyModifiedPropertiesWithoutUndo();
                        PrefabUtility.SaveAsPrefabAsset(attackRoot,attackPath);
                    }
                    finally{PrefabUtility.UnloadPrefabContents(attackRoot);}
                    var attack=root.GetComponent<EnemyAttackMelee>();attack.attackPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(attackPath).GetComponent<EnemyAttackPrefab>();
                    so=new SerializedObject(attack);so.FindProperty("warningTime").floatValue=data.warningExtra;
                    so.FindProperty("attackTime").floatValue=data.activeExtra;so.FindProperty("recoveryTime").floatValue=data.recoveryExtra;so.ApplyModifiedPropertiesWithoutUndo();
                    var animator=controller.enemyAnimator;so=new SerializedObject(animator);
                    foreach(var binding in data.bindings)
                    {
                        // Elite appearance is an explicit adaptation, not a renamed source animation.
                        AnimationClip clip;
                        if(!data.elite) clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(SkeletonAnimations+binding.sourceClip+".anim");
                        else
                        {
                            string source=binding.field.StartsWith("recovery")?"Enemy_Skeleton_AttackRecovery":binding.field.StartsWith("attackWarning")?"Enemy_Skeleton_Warning":"Enemy_Skeleton_AttackMoment";
                            source+=binding.field.Contains("Left")?"Left":"Right"; if(binding.field.StartsWith("attackWarning"))source+="Down";
                            clip=Retime(AssetDatabase.LoadAssetAtPath<AnimationClip>(SkeletonAnimations+source+".anim"),Root+"/AdaptedElite_"+binding.field+".anim",binding.length);
                        }
                        if(clip==null||Mathf.Abs(clip.length-binding.length)>.0001f)throw new InvalidDataException("Stage-two animation mismatch: "+binding.sourceClip+" actual="+(clip!=null?clip.length:0)+" expected="+binding.length);
                        var t=so.FindProperty(binding.field);t.FindPropertyRelative("_Clip").objectReferenceValue=clip;
                        t.FindPropertyRelative("_Speed").floatValue=binding.speed;t.FindPropertyRelative("_FadeDuration").floatValue=binding.fade;
                        t.FindPropertyRelative("_NormalizedStartTime").floatValue=Parse(binding.normalizedStart);
                        var ev=t.FindPropertyRelative("_Events");ev.FindPropertyRelative("_Callbacks").arraySize=0;ev.FindPropertyRelative("_Names").arraySize=0;
                        var times=ev.FindPropertyRelative("_NormalizedTimes");times.arraySize=binding.eventTimes.Length;
                        for(int i=0;i<times.arraySize;i++)times.GetArrayElementAtIndex(i).floatValue=Parse(binding.eventTimes[i]);
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                else controller.attackScript=null;
                ApplyGeometry(root,data,false);
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        private static float Parse(string v)=>float.Parse(v,CultureInfo.InvariantCulture);
        private static Vector3 V3(float[] v)=>new Vector3(v[0],v[1],v[2]);
        private static void ApplyGeometry(GameObject root,Enemy data,bool attack)
        {
            foreach(var g in data.geometry.Where(g=>g.attack==attack))
            {
                var t=string.IsNullOrEmpty(g.path)?root.transform:root.transform.Find(g.path);
                if(t==null)throw new InvalidDataException("Missing stage-two geometry: "+root.name+"/"+g.path);
                switch(g.type)
                {
                    case "Transform": if(g.path!="")t.localPosition=V3(g.localPosition);t.localScale=V3(g.localScale);t.localRotation=new Quaternion(g.localRotation[0],g.localRotation[1],g.localRotation[2],g.localRotation[3]);break;
                    case "CircleCollider2D": var c=t.GetComponent<CircleCollider2D>();c.radius=g.radius;c.offset=new Vector2(g.offset[0],g.offset[1]);c.isTrigger=g.trigger;break;
                    case "BoxCollider2D": var b=t.GetComponent<BoxCollider2D>();b.size=new Vector2(g.size[0],g.size[1]);b.offset=new Vector2(g.offset[0],g.offset[1]);b.isTrigger=g.trigger;break;
                    case "PolygonCollider2D": var p=t.GetComponent<PolygonCollider2D>();p.pathCount=1;p.SetPath(0,Enumerable.Range(0,g.points.Length/2).Select(i=>new Vector2(g.points[2*i],g.points[2*i+1])).ToArray());p.offset=new Vector2(g.offset[0],g.offset[1]);p.isTrigger=g.trigger;break;
                }
            }
        }
        internal static AnimationClip Retime(AnimationClip source,string path,float length,float frameRate=0)
        {
            if(source==null)throw new InvalidDataException("Missing existing Skeleton animation.");
            var result=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            var copy=UnityEngine.Object.Instantiate(source);copy.name=Path.GetFileNameWithoutExtension(path);float ratio=length/source.length;
            if(frameRate>0)copy.frameRate=frameRate;
            foreach(var binding in AnimationUtility.GetObjectReferenceCurveBindings(copy))
            {
                var keys=AnimationUtility.GetObjectReferenceCurve(copy,binding);
                for(int i=0;i<keys.Length;i++)keys[i].time*=ratio;
                if(frameRate>0&&keys.Length>0)
                {
                    var terminal=keys[keys.Length-1];terminal.time=Mathf.Max(0,length-1/copy.frameRate);
                    keys=keys.Where(k=>k.time<terminal.time).Concat(new[]{terminal}).ToArray();
                }
                // Sprite PPtr curves include one sample after their final key in AnimationClip.length.
                if(keys.Length>0)keys[keys.Length-1].time=Mathf.Max(0,length-1/copy.frameRate);
                AnimationUtility.SetObjectReferenceCurve(copy,binding,keys);
            }
            foreach(var binding in AnimationUtility.GetCurveBindings(copy))
            {var curve=AnimationUtility.GetEditorCurve(copy,binding);var keys=curve.keys;for(int i=0;i<keys.Length;i++){keys[i].time*=ratio;keys[i].inTangent/=ratio;keys[i].outTangent/=ratio;}curve.keys=keys;AnimationUtility.SetEditorCurve(copy,binding,curve);}
            var settings=AnimationUtility.GetAnimationClipSettings(copy);settings.stopTime=length;if(frameRate>0)settings.loopTime=false;AnimationUtility.SetAnimationClipSettings(copy,settings);
            if(result==null){AssetDatabase.CreateAsset(copy,path);return copy;}
            EditorUtility.CopySerialized(copy,result);UnityEngine.Object.DestroyImmediate(copy);EditorUtility.SetDirty(result);return result;
        }
        private static void CreateWarning(Warning data)
        {
            string path=Root+"/"+data.name+".anim";var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(clip==null){clip=new AnimationClip{name=data.name,frameRate=60};AssetDatabase.CreateAsset(clip,path);}
            foreach(var curve in data.curves)
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(curve.path,typeof(SpriteRenderer),curve.attribute),
                    new AnimationCurve(curve.keys.Select(k=>new Keyframe(Parse(k.time),Parse(k.value),Parse(k.inSlope),Parse(k.outSlope))).ToArray()));
            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.stopTime=data.length;AnimationUtility.SetAnimationClipSettings(clip,settings);EditorUtility.SetDirty(clip);
        }
        private static void MakeRules(string name,TimelineAsset timeline,double end,bool validation)
        {
            string path=LimboReferenceAssets.ResourcesRoot+"/"+name+".asset";var rules=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(path);
            if(rules!=null)return;
            rules=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/Opening.asset"));AssetDatabase.CreateAsset(rules,path);
            var so=new SerializedObject(rules);so.FindProperty("timeline").objectReferenceValue=timeline;so.FindProperty("referenceEndTime").doubleValue=end;
            so.FindProperty("referenceValidationOnly").boolValue=validation;so.ApplyModifiedPropertiesWithoutUndo();
        }
        public static void CreateAndBuild(){Create();MonsterSupergroup.EditorTools.ProjectBuildService.Build("kcp-development","Builds/LimboReference/MonsterSupergroupLimbo.exe");}
    }
}
