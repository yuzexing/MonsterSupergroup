using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Animancer;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Interactions;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace MonsterSupergroup.NetworkCombat.Editor
{
    public static class LimboDashAssets
    {
        public const string Root = LimboReferenceAssets.Root + "/Dash";
        public const string EnemyPath = Root + "/ReferenceBrotchiDash.prefab";
        public const string ArrowPath = Root + "/ReferenceDashArrow.prefab";
        [Serializable] public class Times { public float warningTime, attackTime, recoveryTime; }
        [Serializable] public class Key { public float time, value, inTangent, outTangent; }
        [Serializable] public class PhysicsData { public float mass, linearDamping, angularDamping, gravityScale; public int constraints; }
        [Serializable] public class Geometry { public string path, type; public bool arrow, enabled, trigger; public float radius; public float[] offset, size, localPosition, localScale, localRotation; }
        [Serializable] public class Data
        {
            public string sourceSha256; public float cooldown, triggerDistance, distance, warningStartLength, warningEndLength;
            public bool stopForAttack, homing, returnToStart; public int exclusionMask;
            public Times extraStageTimes; public Key[] movementCurve; public Geometry[] geometry; public LimboStage2Assets.Binding[] bindings;
            public PhysicsData physics;
        }

        // Create only missing adaptation assets. Existing Inspector/JSON adjustments are preserved.
        [MenuItem("Tools/MonsterSupergroup/Limbo/Create Dash reference assets")]
        public static void Create()
        {
            AssetDatabase.Refresh();
            var data = JsonUtility.FromJson<Data>(File.ReadAllText(Root + "/DashAdapted.json"));
            if(data.bindings.Length != 12) throw new InvalidDataException("Expected twelve recovered Dash transitions.");
            CreateArrow(data); CreateEnemy(data);
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(LimboReferenceAssets.Root + "/Limbo.playable");
            foreach(var spawn in timeline.GetOutputTracks().SelectMany(t=>t.GetClips()).Select(c=>(NetworkEnemySpawnClip)c.asset))
            {
                if(spawn.sourceEnemy=="Brotchi_Dash")
                {
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath);
                    if(spawn.enemyPrefab!=prefab || spawn.referenceReadiness==ReferenceEnemyReadiness.ImplementationPending)
                    {
                        spawn.enemyPrefab=prefab;spawn.contactRadius=0;spawn.referenceReadiness=ReferenceEnemyReadiness.ValidationPending;
                        spawn.missingEvidence="Dash integrated from recovered data; rendered per-variant validation pending.";
                        spawn.recoveredEvidence="docs/evidence/hellmaiden-attacks/recovered-enemies.json#Brotchi_Dash";
                    }
                }
                EditorUtility.SetDirty(spawn);
            }
            MakeRules("Dash",timeline,389.6,false); MakeRules("DashValidation",timeline,389.6,true);
            for(int variant=0;variant<2;variant++)
            {
                string path=Root+"/Fixture"+variant+".playable";
                var fixture=AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
                if(fixture==null)
                {
                    fixture=ScriptableObject.CreateInstance<TimelineAsset>();AssetDatabase.CreateAsset(fixture,path);
                    var track=fixture.CreateTrack<NetworkEnemySpawnTrack>();var clip=track.CreateClip<NetworkEnemySpawnClip>();clip.start=1;clip.duration=1;
                    var spawn=(NetworkEnemySpawnClip)clip.asset;
                    spawn.enemyPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath);spawn.sourceEnemy="Brotchi_Dash";spawn.sourceVariant=variant;
                    spawn.referenceMode=ReferenceSpawnMode.CurveBudget;spawn.count=1;spawn.spawnCurve=AnimationCurve.Constant(0,1,1);
                    spawn.expiresOffscreen=false;spawn.resetOnReposition=true;spawn.contactRadius=0;
                    spawn.referenceReadiness=ReferenceEnemyReadiness.ValidationPending;spawn.missingEvidence="Explicit Dash mechanism fixture.";
                    EditorUtility.SetDirty(spawn);EditorUtility.SetDirty(track);EditorUtility.SetDirty(fixture);
                }
                MakeRules("DashFixture"+variant,fixture,150,true);
                MakeRules("DashBoundary"+variant,fixture,36,true);
            }
            var reuse=AssetDatabase.LoadAssetAtPath<TimelineAsset>(Root+"/Reuse.playable");
            if(reuse==null)
            {
                reuse=ScriptableObject.CreateInstance<TimelineAsset>();AssetDatabase.CreateAsset(reuse,Root+"/Reuse.playable");var track=reuse.CreateTrack<NetworkEnemySpawnTrack>();
                for(int v=0;v<2;v++)
                {
                    var clip=track.CreateClip<NetworkEnemySpawnClip>();clip.start=v==0?1:40;clip.duration=1;var spawn=(NetworkEnemySpawnClip)clip.asset;
                    spawn.enemyPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath);spawn.sourceEnemy="Brotchi_Dash";spawn.sourceVariant=v;spawn.count=1;
                    spawn.referenceMode=ReferenceSpawnMode.CurveBudget;spawn.spawnCurve=AnimationCurve.Constant(0,1,1);spawn.resetOnReposition=true;spawn.expiresOffscreen=false;
                    spawn.referenceReadiness=ReferenceEnemyReadiness.ValidationPending;spawn.missingEvidence="Explicit v0 to v1 attack pool reuse fixture.";EditorUtility.SetDirty(spawn);
                }
                EditorUtility.SetDirty(track);EditorUtility.SetDirty(reuse);
            }
            MakeRules("DashReuse",reuse,80,true);
            AssetDatabase.SaveAssets();Debug.Log("[LimboDash] source-backed assets prepared; Dash remains ValidationPending and Full is gated.");
        }

        private static void CreateEnemy(Data data)
        {
            if(File.Exists(EnemyPath)) return;
            AssetDatabase.CopyAsset(LimboStage2Assets.Root+"/ReferenceBrotchi.prefab",EnemyPath);
            var root=PrefabUtility.LoadPrefabContents(EnemyPath);
            try
            {
                root.name="ReferenceBrotchiDash";
                var controller=root.GetComponent<EnemyController>();
                var contact=root.GetComponent<EnemyContactDamage>();
                var hit=UnityEngine.Object.Instantiate(contact.DamageInteraction.gameObject,root.transform);hit.name="DashHitbox";hit.SetActive(true);
                contact.SetContactEnabled(false);
                foreach(var old in root.GetComponents<EnemyAttack>())UnityEngine.Object.DestroyImmediate(old);
                var attack=root.AddComponent<EnemyAttackDash>();controller.attackScript=attack;
                attack.rb=root.GetComponent<Rigidbody2D>();attack.sprite=controller.enemyAnimator.transform;
                ConfigurePhysics(root,data);
                attack.attackPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(ArrowPath).GetComponent<EnemyAttackPrefab>();
                attack.attackCollider=hit.GetComponent<CircleCollider2D>();attack.damageInteraction=hit.GetComponent<PlayerDamageInteraction>();
                attack.damageInteraction.damageType=DamageType.Thorns;attack.attackCollider.enabled=false;attack.damageInteraction.enabled=false;
                attack.distance=data.distance;attack.homingTarget=data.homing;attack.returnToInitialDashPosition=data.returnToStart;attack.dashExclusionLayerMask=data.exclusionMask;
                attack.movementCurve=new AnimationCurve(data.movementCurve.Select(k=>new Keyframe(k.time,k.value,k.inTangent,k.outTangent)).ToArray());
                attack.movementCurve.preWrapMode=attack.movementCurve.postWrapMode=WrapMode.ClampForever;
                controller.attackCooldown=data.cooldown;controller.attackDistance=data.triggerDistance;controller.selectedName="Brotchi_Dash";
                var so=new SerializedObject(controller);so.FindProperty("stopForAttack").boolValue=data.stopForAttack;
                so.FindProperty("hasAttackAnimation").boolValue=true;so.FindProperty("facingPlayerDuringWarning").boolValue=false;
                so.FindProperty("facingPlayerDuringAttack").boolValue=false;so.ApplyModifiedPropertiesWithoutUndo();
                so=new SerializedObject(attack);so.FindProperty("warningTime").floatValue=data.extraStageTimes.warningTime;
                so.FindProperty("attackTime").floatValue=data.extraStageTimes.attackTime;so.FindProperty("recoveryTime").floatValue=data.extraStageTimes.recoveryTime;
                so.ApplyModifiedPropertiesWithoutUndo();
                root.GetComponent<NetworkEnemySimulationAgent>().ConfigureProductSimulation(false);
                if(root.GetComponent<NetworkEnemyMeleeReplica>()==null)root.AddComponent<NetworkEnemyMeleeReplica>();
                BindAnimations(controller.enemyAnimator,data);
                foreach(var g in data.geometry.Where(g=>!g.arrow))
                {
                    var t=g.path==""?root.transform:g.path=="AttackCollider"?hit.transform:root.transform.Find(g.path);
                    if(t==null)throw new InvalidDataException("Missing Dash geometry: "+g.path);
                    if(g.type=="Transform")
                    {
                        if(g.path!="")t.localPosition=new Vector3(g.localPosition[0],g.localPosition[1],g.localPosition[2]);
                        t.localScale=new Vector3(g.localScale[0],g.localScale[1],g.localScale[2]);
                        t.localRotation=new Quaternion(g.localRotation[0],g.localRotation[1],g.localRotation[2],g.localRotation[3]);
                    }
                    else if(g.type=="CircleCollider2D")
                    {var circle=t.GetComponent<CircleCollider2D>();circle.radius=g.radius;circle.offset=new Vector2(g.offset[0],g.offset[1]);circle.isTrigger=g.trigger;circle.enabled=g.path!="AttackCollider";}
                    else if(g.type=="BoxCollider2D")
                    {var box=t.GetComponent<BoxCollider2D>();box.offset=new Vector2(g.offset[0],g.offset[1]);box.size=new Vector2(g.size[0],g.size[1]);box.isTrigger=g.trigger;}
                }
                var identity=new SerializedObject(root.GetComponent<NetworkIdentity>());
                identity.FindProperty("_assetId").longValue=NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(EnemyPath)));
                identity.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(root,EnemyPath);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }

        private static void BindAnimations(EnemyAnimator animator,Data data)
        {
            var so=new SerializedObject(animator);
            foreach(var b in data.bindings)
            {
                string quadrant=b.field.Contains("Left")?"Left":"Right";quadrant+=b.field.EndsWith("Up")?"Up":"Down";
                var source=so.FindProperty("move"+quadrant).FindPropertyRelative("_Clip").objectReferenceValue as AnimationClip;
                string path=Root+"/Adapted_"+b.field+".anim";
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path)??LimboStage2Assets.Retime(source,path,b.length,100);
                if(Mathf.Abs(clip.length-b.length)>.0001f)throw new InvalidDataException("Dash adaptation length mismatch: "+b.field+" actual="+clip.length);
                var transition=so.FindProperty(b.field);transition.FindPropertyRelative("_Clip").objectReferenceValue=clip;
                transition.FindPropertyRelative("_Speed").floatValue=b.speed;transition.FindPropertyRelative("_FadeDuration").floatValue=b.fade;
                transition.FindPropertyRelative("_NormalizedStartTime").floatValue=float.Parse(b.normalizedStart,CultureInfo.InvariantCulture);
                var events=transition.FindPropertyRelative("_Events");events.FindPropertyRelative("_Callbacks").arraySize=0;events.FindPropertyRelative("_Names").arraySize=0;
                var times=events.FindPropertyRelative("_NormalizedTimes");times.arraySize=b.eventTimes.Length;
                for(int i=0;i<times.arraySize;i++)times.GetArrayElementAtIndex(i).floatValue=float.Parse(b.eventTimes[i],CultureInfo.InvariantCulture);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Tools/MonsterSupergroup/Limbo/Update Dash reference physics from adaptation")]
        public static void UpdatePhysics()
        {
            var data=JsonUtility.FromJson<Data>(File.ReadAllText(Root+"/DashAdapted.json"));
            var root=PrefabUtility.LoadPrefabContents(EnemyPath);
            try { ConfigurePhysics(root,data);PrefabUtility.SaveAsPrefabAsset(root,EnemyPath); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }
        private static void ConfigurePhysics(GameObject root,Data data)
        {
            if(data.physics==null)throw new InvalidDataException("Dash physics extraction missing.");
            var body=root.GetComponent<Rigidbody2D>();body.mass=data.physics.mass;body.linearDamping=data.physics.linearDamping;
            body.angularDamping=data.physics.angularDamping;body.gravityScale=data.physics.gravityScale;body.constraints=(RigidbodyConstraints2D)data.physics.constraints;
            body.sharedMaterial=AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>("Assets/_Project/Content/NetworkCombat/Validation/HellMaiden/PhysicsMaterial2D/Slipery.physicsMaterial2D");
        }

        private static void CreateArrow(Data data)
        {
            if(File.Exists(ArrowPath)) return;
            var root=new GameObject("ReferenceDashArrow");
            try
            {
                var white=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Content/NetworkCombat/Validation/HellMaiden/Texture2D/white_1.png");
                if(white==null)throw new InvalidDataException("Existing white warning sprite missing.");
                var material=AssetDatabase.LoadAssetAtPath<GameObject>(LimboStage2Assets.Root+"/SkeletonAttack.prefab").GetComponentInChildren<SpriteRenderer>(true).sharedMaterial;
                var segments=new[]{(Vector2.zero,new Vector2(data.distance,0)),(new Vector2(data.distance-.9f,.55f),new Vector2(data.distance,0)),(new Vector2(data.distance,0),new Vector2(data.distance-.9f,-.55f))};
                for(int i=0;i<segments.Length;i++)
                {
                    var go=new GameObject("Stroke"+i);go.transform.SetParent(root.transform,false);var (a,b)=segments[i];
                    go.transform.localPosition=(a+b)*.5f;var delta=b-a;go.transform.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg);
                    go.transform.localScale=new Vector3(delta.magnitude/white.bounds.size.x,.12f/white.bounds.size.y,1);
                    var sprite=go.AddComponent<SpriteRenderer>();sprite.sprite=white;sprite.sharedMaterial=material;sprite.color=new Color(1,.42f,.06f,.8f);sprite.sortingLayerName="EnemyAttack";sprite.sortingOrder=15;
                }
                var animator=root.AddComponent<Animator>();var animancer=root.AddComponent<AnimancerComponent>();animancer.Animator=animator;
                var warning=root.AddComponent<MeleeAttackWarning>();var so=new SerializedObject(warning);so.FindProperty("animancer").objectReferenceValue=animancer;
                foreach(var item in new[]{("warningStart",data.warningStartLength,.15f,.8f),("warningEnd",data.warningEndLength,.8f,0f)})
                {
                    string path=Root+"/Arrow_"+item.Item1+".anim";var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if(clip==null)
                    {
                        clip=new AnimationClip{name="Arrow_"+item.Item1,frameRate=60};
                        for(int i=0;i<segments.Length;i++)AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("Stroke"+i,typeof(SpriteRenderer),"m_Color.a"),AnimationCurve.Linear(0,item.Item3,item.Item2,item.Item4));
                        AssetDatabase.CreateAsset(clip,path);
                    }
                    var transition=so.FindProperty(item.Item1);transition.FindPropertyRelative("_Clip").objectReferenceValue=clip;
                    transition.FindPropertyRelative("_Speed").floatValue=1;transition.FindPropertyRelative("_FadeDuration").floatValue=0;
                }
                so.ApplyModifiedPropertiesWithoutUndo();var attack=root.AddComponent<EnemyAttackPrefab>();attack.attackWarning=warning;
                PrefabUtility.SaveAsPrefabAsset(root,ArrowPath);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }

        private static void MakeRules(string name,TimelineAsset timeline,double end,bool validation)
        {
            string path=LimboReferenceAssets.ResourcesRoot+"/"+name+".asset";
            if(AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(path)!=null)return;
            var rules=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/Stage2Validation.asset"));
            AssetDatabase.CreateAsset(rules,path);var so=new SerializedObject(rules);so.FindProperty("timeline").objectReferenceValue=timeline;
            so.FindProperty("referenceEndTime").doubleValue=end;so.FindProperty("referenceValidationOnly").boolValue=validation;
            if(name.StartsWith("DashFixture")||name.StartsWith("DashBoundary")||name=="DashReuse")so.FindProperty("barriers").arraySize=0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Tools/MonsterSupergroup/Limbo/Approve recorded spatial validation")]
        public static void MarkSpatialValidated()
        {
            var timeline=AssetDatabase.LoadAssetAtPath<TimelineAsset>(LimboReferenceAssets.Root+"/Limbo.playable");
            foreach(var spawn in timeline.GetOutputTracks().SelectMany(t=>t.GetClips()).Select(c=>(NetworkEnemySpawnClip)c.asset))
            {
                if(spawn.referenceMode!=ReferenceSpawnMode.FormationBurst || spawn.referenceSpawnReadiness!=ReferenceEnemyReadiness.ValidationPending)continue;
                spawn.referenceSpawnReadiness=ReferenceEnemyReadiness.Ready;spawn.spawnReadinessNote="";EditorUtility.SetDirty(spawn);
            }
            foreach(var guid in AssetDatabase.FindAssets("t:GameplayWaveRules",new[]{LimboReferenceAssets.ResourcesRoot}))
            {
                var rules=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(AssetDatabase.GUIDToAssetPath(guid));var so=new SerializedObject(rules);
                var barriers=so.FindProperty("barriers");
                for(int i=0;i<barriers.arraySize;i++)
                {
                    var b=barriers.GetArrayElementAtIndex(i);
                    if(b.FindPropertyRelative("lifecycleVersion").intValue!=1 || (b.FindPropertyRelative("readiness").enumValueIndex!=(int)ReferenceEnemyReadiness.ValidationPending && !b.FindPropertyRelative("readinessNote").stringValue.StartsWith("Technical closure archived")))continue;
                    b.FindPropertyRelative("readiness").enumValueIndex=(int)ReferenceEnemyReadiness.Ready;
                    b.FindPropertyRelative("readinessNote").stringValue="";
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
        }

        // Explicit acceptance operation, separate from Create/Update. Run only after reviewing the recorded matrix.
        [MenuItem("Tools/MonsterSupergroup/Limbo/Approve recorded Dash and stage-two behavior validation")]
        public static void MarkValidated()
        {
            var timeline=AssetDatabase.LoadAssetAtPath<TimelineAsset>(LimboReferenceAssets.Root+"/Limbo.playable");
            string[] accepted={"Brotchi","Slime","Skeleton","Elite_Skeleton","Brotchi_Dash"};
            foreach(var spawn in timeline.GetOutputTracks().SelectMany(t=>t.GetClips()).Select(c=>(NetworkEnemySpawnClip)c.asset))
            {
                if(!accepted.Contains(spawn.sourceEnemy)||spawn.referenceReadiness!=ReferenceEnemyReadiness.ValidationPending)continue;
                spawn.referenceReadiness=ReferenceEnemyReadiness.Ready;spawn.missingEvidence="";EditorUtility.SetDirty(spawn);
            }
            AssetDatabase.SaveAssets();
            var preview=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/Dash.asset");
            if(!preview.TryCapture(out var playable,out var previewError) || playable.Reference.ReadinessError()!=null)
                throw new InvalidDataException("Dash preview must be ready after acceptance: "+previewError+" / "+playable.Reference?.ReadinessError());
            var full=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/Full.asset");
            if(!full.TryCapture(out var captured,out var error)||captured.Reference.ReadinessError()==null)
                throw new InvalidDataException("Full must retain the unimplemented enemy gate: "+error);
            Debug.Log("[LimboDash] reviewed behavior gates accepted; Full remains disabled. See docs/limbo-dash-integration.md.");
        }
    }
}
