using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Newtonsoft.Json;
using Object = UnityEngine.Object;

// This assembly is loaded only by the isolated original-game runtime. It is not part of the Unity project build.
public sealed partial class RecoveryProbe : MonoBehaviour
{
    static RecoveryProbe instance;
    static readonly Dictionary<string,object> preferences = new Dictionary<string,object>();
    static StreamWriter events;
    static string output;
    static string phase = "boot";
    static readonly Assembly game = AppDomain.CurrentDomain.GetAssemblies().First(x => x.GetName().Name == "Assembly-CSharp");
    static readonly Dictionary<int,int> births = new Dictionary<int,int>();
    static readonly Dictionary<int,int> ownerByEffect = new Dictionary<int,int>();
    static readonly Dictionary<int,int> completed = new Dictionary<int,int>();
    static Component observedEnemy;
    static Component observedPlayer;
    static int previousHp=-1;
    static readonly Dictionary<int,string> colliderStates=new Dictionary<int,string>();
    static readonly Dictionary<int,Collider2D> knownColliders=new Dictionary<int,Collider2D>();
    static string fixtureLabel="";
    public static string Arg(string prefix, string fallback)
    { var a = Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(prefix)); return a == null ? fallback : a.Substring(prefix.Length); }
    public static string Output { get { return output ?? (output = Path.GetFullPath(Arg("--recovery-output=", "RecoveryOutput"))); } }
    public static string IsolatedDataPath() { string path = Path.Combine(Output,"profile"); Directory.CreateDirectory(path); return path; }
    public static void PrefsSetString(string k,string v) { preferences[k]=v; }
    public static void PrefsSetInt(string k,int v) { preferences[k]=v; }
    public static void PrefsSetFloat(string k,float v) { preferences[k]=v; }
    public static string PrefsGetString(string k) { return PrefsGetString(k,""); }
    public static string PrefsGetString(string k,string v) { object x; return preferences.TryGetValue(k,out x) ? Convert.ToString(x) : v; }
    public static int PrefsGetInt(string k) { return PrefsGetInt(k,0); }
    public static int PrefsGetInt(string k,int v) { object x; return preferences.TryGetValue(k,out x) ? Convert.ToInt32(x) : v; }
    public static float PrefsGetFloat(string k) { return PrefsGetFloat(k,0); }
    public static float PrefsGetFloat(string k,float v) { object x; return preferences.TryGetValue(k,out x) ? Convert.ToSingle(x) : v; }
    public static bool PrefsHasKey(string k) { return preferences.ContainsKey(k); }
    public static void PrefsDeleteKey(string k) { preferences.Remove(k); }
    public static void PrefsDeleteAll() { preferences.Clear(); }
    public static void PrefsSave() { }
    public static void Install()
    {
        if (instance != null) return;
        Directory.CreateDirectory(Output);
        events = new StreamWriter(Path.Combine(Output,"events.jsonl")); events.AutoFlush = true;
        instance = new GameObject("Enemy data recovery observer").AddComponent<RecoveryProbe>();
        DontDestroyOnLoad(instance.gameObject);
        Application.runInBackground = true; Application.targetFrameRate = 60;
        Log("bootstrap",new { unity=Application.unityVersion, company=Application.companyName, product=Application.productName,
            isolatedProfile=IsolatedDataPath(), note="Instrumented original runtime; preferences in memory; Steam disabled." });
    }
    public static object Get(object target,string name)
    {
        if (target == null) return null;
        Type type = target as Type ?? target.GetType(); object obj = target is Type ? null : target;
        for (var t=type;t!=null;t=t.BaseType)
        {
            var f=t.GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly);
            if (f!=null) return f.GetValue(obj);
            var p=t.GetProperty(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly);
            if(p!=null && p.GetIndexParameters().Length==0) return p.GetValue(obj,null);
        }
        return null;
    }
    public static void Set(object target,string name,object value)
    {
        for(var t=target.GetType();t!=null;t=t.BaseType)
        {
            var f=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
            if(f!=null) { f.SetValue(target,value); return; }
            var p=t.GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
            if(p!=null && p.CanWrite) {p.SetValue(target,value,null);return;}
        }
        throw new MissingFieldException(target.GetType().Name,name);
    }
    public static Type TypeNamed(string name) { return game.GetType(name,true); }
    public static object Singleton(string name) { return Get(TypeNamed(name),"Instance"); }
    public static object Invoke(object target,string name,params object[] args)
    {
        return target.GetType().GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)
            .Single(m=>m.Name==name && m.GetParameters().Length==args.Length).Invoke(target,args);
    }
    public static void Log(string kind,object value)
    {
        if(events==null)return;
        events.WriteLine(JsonConvert.SerializeObject(new { kind=kind,phase=phase,time=Time.time,fixedTime=Time.fixedTime,deltaTime=Time.deltaTime,frame=Time.frameCount,realtime=Time.realtimeSinceStartup,data=value }));
    }
    public static string ObjectPath(Object obj)
    {
        var c=obj as Component; var go=obj as GameObject; Transform t=c!=null?c.transform:go!=null?go.transform:null;
        if(t==null)return obj==null?null:obj.name;
        string s=t.name+"["+t.GetSiblingIndex()+"]";
        while(t.parent!=null){t=t.parent;s=t.name+"["+t.GetSiblingIndex()+"]/"+s;}
        return s;
    }
    public static void Born(object enemy,string method)
    {
        try {
            var component=enemy as Component; int id=component.GetInstanceID(); int n; births.TryGetValue(id,out n); births[id]=n+1;
            var stats=Get(enemy,"stats");
            Log("birth",new {id=id,reuse=n+1,source=Get(enemy,"selectedName"),position=Vec(component.transform.position),
                hp=Get(stats,"Health"),baseHp=Get(stats,"BaseHealth"),damage=Get(stats,"Damage"),speed=Get(stats,"Speed"),
                speedMultiplier=Get(stats,"SpeedMultiplier"),xp=Get(stats,"XP"),attack=Timing(Get(enemy,"attackScript"))});
        } catch(Exception e){Log("probe-error",e.ToString());}
    }
    public static object Timing(object attack)
    {
        if(attack==null)return null;
        try {return new {warning=Get(attack,"WarningTime"),active=Get(attack,"AttackTime"),recovery=Get(attack,"RecoveryTime"),index=Get(attack,"currentAttackCount")};}
        catch(Exception e){return new {unavailable=e.GetBaseException().Message};}
    }
    public static float[] Vec(Vector3 value) {return new[]{value.x,value.y,value.z};}
    public static void Event(object sender,string method)
    {
        try {
            var component=sender as Component; if(component==null)return;
            var controller=Get(sender,"controller") as Component ?? Get(sender,"ShooterController") as Component;
            if(controller==null) controller=component.GetComponentInParent(TypeNamed("AstralShift.HellMaiden.AI.Enemy.EnemyController"));
            int owner=controller!=null?controller.GetInstanceID():0;
            if(owner!=0)ownerByEffect[component.GetInstanceID()]=owner;
            else ownerByEffect.TryGetValue(component.GetInstanceID(),out owner);
            if(method=="EnemyAttack.RecoveryExit" && (Get(sender,"currentAttackCount")==null || Convert.ToInt32(Get(sender,"currentAttackCount"))==0))
            {int n;completed.TryGetValue(owner,out n);completed[owner]=n+1;}
            Log("attack-event",new {method=method,id=component.GetInstanceID(),owner=owner,path=ObjectPath(component),
                source=controller!=null?Get(controller,"selectedName"):null,position=Vec(component.transform.position),timing=Timing(sender),
                colliderStates=component.GetComponentsInChildren<Collider2D>(true).Select(c=>new {path=ObjectPath(c),enabled=c.enabled,active=c.gameObject.activeInHierarchy}).ToArray()});
        } catch(Exception e){Log("probe-error",e.ToString());}
    }
    IEnumerator Start()
    {
        float deadline=Time.realtimeSinceStartup+180;
        while(Time.realtimeSinceStartup<deadline)
        {
            object master=Singleton("AstralShift.HellMaiden.Scenes.SceneMaster");
            if(master!=null && SceneManager.GetActiveScene().name=="TitleScreen" && Get(master,"_mainOperationCoroutine")==null)
            {
                if(Arg("--recovery-mode=","capture")=="observe" || Arg("--recovery-mode=","capture")=="audio")
                {
                    ((IList)Get(Get(TypeNamed("AstralShift.HellMaiden.Data.GameData"),"Instance"),"viewedCutscenes")).Add("CUT_HUB_STR_INTRO2");
                    Log("fixture-skip-intro","Mark only CUT_HUB_STR_INTRO2 as viewed in the isolated in-memory profile. This is an attack fixture, not a complete new-game playthrough.");
                }
                Log("load-request", "Level_Limbo via original SceneMaster.LoadScene");
                var scene=Enum.Parse(TypeNamed("AstralShift.HellMaiden.Scenes.SceneEnum"),"Level_Limbo");
                Invoke(master,"LoadScene",scene,true,true); break;
            }
            yield return null;
        }
        while(Time.realtimeSinceStartup<deadline)
        {
            object progression=Singleton("AstralShift.HellMaiden.Combat.ProgressionManager");
            if(progression!=null && SceneManager.GetActiveScene().name=="Level_Limbo" && Get(progression,"MainProgressionTimeline")!=null)
            {
                // Wait until the source scene loader has initialized the original Timeline.
                var timeline=Get(progression,"MainProgressionTimeline");
                var director=Get(timeline,"playableDirector") as UnityEngine.Playables.PlayableDirector;
                if(director!=null && director.playableAsset is TimelineAsset)
                {
                    phase="asset-capture";
                    bool captured=false;
                    try { Capture(progression,(TimelineAsset)director.playableAsset); captured=true; }
                    catch(Exception e){Log("probe-error",e.ToString());File.WriteAllText(Path.Combine(Output,"capture.failed"),e.ToString());}
                    if(!captured)yield break;
                    if(Arg("--recovery-mode=","capture")=="art")
                    { yield return StartCoroutine(CaptureArt()); yield break; }
                    if(Arg("--recovery-mode=","capture")=="audio")
                    { yield return StartCoroutine(CaptureAudio(progression)); yield break; }
                    phase="observe";
                    if(Arg("--recovery-mode=","capture")=="capture")
                    {File.WriteAllText(Path.Combine(Output,"capture.complete"),"asset-capture completed"); yield return new WaitForSecondsRealtime(2); Application.Quit(); yield break;}
                    yield return StartCoroutine(Observe(progression)); yield break;
                }
            }
            yield return null;
        }
        Log("failed","Original title/Limbo initialization timed out.");
        File.WriteAllText(Path.Combine(Output,"capture.failed"),"Original title/Limbo initialization timed out.");
    }
    void Capture(object progression,TimelineAsset timeline)
    {
        var graph=new AssetGraph(); var clips=new List<object>();
        foreach(var track in timeline.GetOutputTracks())
        {
            if(track.muted)continue;
            foreach(var clip in track.GetClips())
            {
                var prefab=Get(clip.asset,"enemyPrefab") as Component;
                if(prefab==null)continue;
                clips.Add(new {start=clip.start,duration=clip.duration,track=track.name,clipType=clip.asset.GetType().FullName,
                    source=Get(prefab,"selectedName"),variant=Get(clip.asset,"variantIndex"),prefab=graph.Reference(prefab),clip=graph.Reference(clip.asset)});
            }
        }
        var data=new {schemaVersion=1,unity=Application.unityVersion,timeline=timeline.name,duration=timeline.duration,
            clips=clips,enemyDatabase=graph.Reference(Get(progression,"enemyDatabase") as Object),objects=graph.Complete()};
        File.WriteAllText(Path.Combine(Output,"assets.json"),JsonConvert.SerializeObject(data,Formatting.Indented));
        var player=Get(Singleton("AstralShift.HellMaiden.GameDirector"),"Player");
        var stats=Get(player,"stats");
        Log("assets-captured",new {clips=clips.Count,objects=graph.Count,playerHp=Get(stats,"Health"),playerSpeed=Get(stats,"Speed"),
            signatureWeapon=Get(Get(TypeNamed("AstralShift.HellMaiden.Data.GameData"),"Instance"),"signatureWeaponID")});
        Debug.Log("[EnemyRecovery] captured "+clips.Count+" enemy clips / "+graph.Count+" objects");
    }
    IEnumerator Observe(object progression)
    {
        float readyDeadline=Time.realtimeSinceStartup+180;
        var master=Singleton("AstralShift.HellMaiden.Scenes.SceneMaster");
        while(Get(master,"_mainOperationCoroutine")!=null && Time.realtimeSinceStartup<readyDeadline)yield return null;
        if(Get(master,"_mainOperationCoroutine")!=null){Log("failed","Scene loader did not finish.");yield break;}
        var timeline=Get(progression,"MainProgressionTimeline") as MonoBehaviour;
        Invoke(timeline,"Pause");
        foreach(var spawner in timeline.GetComponentsInChildren<MonoBehaviour>())
            if(spawner.GetType().Namespace=="AstralShift.HellMaiden.Combat.Spawners")spawner.StopAllCoroutines();
        observedPlayer=Get(Singleton("AstralShift.HellMaiden.GameDirector"),"Player") as Component;
        Log("fixture-clear-transition-invulnerability",new {before=Get(observedPlayer,"IsInvulnerable"),count=Get(observedPlayer,"_invulnerabilityCount"),note="Direct title-to-Limbo fixture bypasses the normal hub transition. Clear its leftover transition shield once, before damage observation."});
        Invoke(observedPlayer,"ResetInvulnerability");
        Invoke(Singleton("AstralShift.HellMaiden.Combat.Hand.PlayerHand"),"DeactivateWeapons");
        var enemyType=TypeNamed("AstralShift.HellMaiden.AI.Enemy.EnemyController");
        foreach(var existing in Object.FindObjectsByType(enemyType,FindObjectsSortMode.None).Cast<Component>())Invoke(existing,"Kill",true,false);
        var playerStats=Get(Get(observedPlayer,"PlayerStats"),"currentStats");
        Vector3 playerOrigin=observedPlayer.transform.position;
        Log("fixture-start",new {note="Isolated attack observation: main Timeline paused; weapons deactivated; one enemy at a time; player healed between completed cycles. Original attack/AI/damage logic unchanged.",stage=Get(progression,"StageTime"),hp=Get(playerStats,"HP"),speed=Get(playerStats,"moveSpeed"),xpModifier=Get(playerStats,"xpModifier"),signatureWeapon=Get(Get(TypeNamed("AstralShift.HellMaiden.Data.GameData"),"Instance"),"signatureWeaponID")});
        var director=Get(timeline,"playableDirector") as UnityEngine.Playables.PlayableDirector;
        var clips=((TimelineAsset)director.playableAsset).GetOutputTracks().Where(t=>!t.muted).SelectMany(t=>t.GetClips()).Where(c=>Get(c.asset,"enemyPrefab") is Component).ToList();
        string requested=Arg("--recovery-enemy=","Imp");int requestedVariant=int.Parse(Arg("--recovery-variant=","0"));
        var selections=clips.GroupBy(c=>((Component)Get(c.asset,"enemyPrefab")).GetInstanceID()+":"+Get(c.asset,"variantIndex"))
            .Select(g=>g.First()).Where(c=>requested=="all" || ((string)Get(Get(c.asset,"enemyPrefab"),"selectedName")==requested && (int)Get(c.asset,"variantIndex")==requestedVariant))
            .OrderBy(c=>(string)Get(Get(c.asset,"enemyPrefab"),"selectedName")=="Imp"?0:1).ThenBy(c=>c.start).ToList();
        foreach(var clip in selections)
        {
            var prefab=(Component)Get(clip.asset,"enemyPrefab");int variant=(int)Get(clip.asset,"variantIndex");
            string identity=(string)Get(prefab,"selectedName");
            for(int reuse=0;reuse<(identity=="LostSoul"?4:2);reuse++)
            {
                fixtureLabel=identity+" v"+variant+" / spawn "+(reuse+1);
                observedPlayer.transform.position=playerOrigin;
                Invoke(observedPlayer,"StopMovement");
                Invoke(observedPlayer,"IncreaseHealth",500);
                observedEnemy=Spawn(prefab,variant,observedPlayer.transform.position+new Vector3(identity=="Imp"?7:2,2,0));
                int id=observedEnemy.GetInstanceID();completed[id]=0;
                Log("fixture-enemy",new {id=id,identity=identity,variant=variant,reuseEpisode=reuse,position=Vec(observedEnemy.transform.position)});
                yield return null;
                Log("post-start-bindings",new {id=id,timing=Timing(Get(observedEnemy,"attackScript")),
                    damage=observedEnemy.GetComponentsInChildren(TypeNamed("AstralShift.HellMaiden.Interactions.PlayerDamageInteraction"),true).Select(c=>new {path=ObjectPath(c),damage=Get(Get(c,"enemyStats"),"Damage"),sameStats=Object.ReferenceEquals(Get(c,"enemyStats"),Get(observedEnemy,"stats"))}).ToArray()});
                float until=Time.realtimeSinceStartup+35;int last=0;
                bool contact=!(bool)Get(observedEnemy,"hasAttackAnimation");
                if(contact)observedEnemy.transform.position=observedPlayer.transform.position+new Vector3(.3f,0,0);
                while(Time.realtimeSinceStartup<until && observedEnemy!=null && observedEnemy.gameObject.activeInHierarchy)
                {
                    int n;completed.TryGetValue(id,out n);
                    if(n!=last)
                    {
                        last=n;Log("fixture-cycle-complete",new {id=id,cycle=n});
                        Invoke(observedPlayer,"IncreaseHealth",500);
                        if(n>=4)break;
                        float distance=identity=="Imp"?7:1.5f;
                        observedEnemy.transform.position=observedPlayer.transform.position+new Vector3((n%2==0?1:-1)*distance,(n<2?1:-1)*1.5f,0);
                    }
                    if(contact && Time.realtimeSinceStartup>until-32)break;
                    yield return null;
                }
                Log("fixture-enemy-end",new {id=id,cycles=last,active=observedEnemy!=null && observedEnemy.gameObject.activeInHierarchy,contact=contact});
                if(observedEnemy!=null && observedEnemy.gameObject.activeInHierarchy)Invoke(observedEnemy,"Kill",true,false);
                observedEnemy=null;
                yield return new WaitForSecondsRealtime(2);
            }
        }
        phase="observation-finished";fixtureLabel="Evidence recorded; enemies remain disabled in MonsterSupergroup";
        File.WriteAllText(Path.Combine(Output,"observe.complete"),"bounded mechanism observations finished; inspect individual results before accepting");
    }
    Component Spawn(Component prefab,int variant,Vector3 position)
    {
        var poolManager=Singleton("AstralShift.HellMaiden.Combat.PoolManager");
        var enemyType=TypeNamed("AstralShift.HellMaiden.AI.Enemy.EnemyController");
        var pool=poolManager.GetType().GetMethod("GetOrCreatePooler").MakeGenericMethod(enemyType).Invoke(poolManager,new object[]{prefab,-1});
        var args=Activator.CreateInstance(TypeNamed("AstralShift.HellMaiden.AI.Enemy.EnemySpawnParams"));
        Set(args,"Prefab",prefab);Set(args,"Pool",pool);Set(args,"AttackTarget",observedPlayer.transform);Set(args,"SpawnPosition",(Vector2)position);
        Set(args,"VariantIdx",variant);Set(args,"SpeedMultiplierRange",new Vector2(.9f,1.1f));Set(args,"AllowRubberBand",false);
        return (Component)TypeNamed("AstralShift.HellMaiden.AI.Enemy.EnemyFactory").GetMethod("CreateEnemy").Invoke(null,new[]{args});
    }
    void LateUpdate()
    {
        if(observedPlayer==null)return;
        var hp=(int)Get(Get(Get(observedPlayer,"PlayerStats"),"currentStats"),"HP");
        if(hp!=previousHp){Log("player-health",new {before=previousHp,after=hp});previousHp=hp;}
        var targets=new List<Component>();if(observedEnemy!=null)targets.Add(observedEnemy);
        foreach(string name in new[]{"BulletProjectile","EnemyExplosionAttackVFX","EnemyAttackPrefab"})
            targets.AddRange(Object.FindObjectsByType(TypeNamed("AstralShift.HellMaiden.AI.Enemy."+name),FindObjectsSortMode.None).Cast<Component>());
        foreach(var target in targets.Distinct())
        {
            foreach(var c in target.GetComponentsInChildren<Collider2D>(true))
            {
                knownColliders[c.GetInstanceID()]=c;
            }
            if(Time.frameCount%3==0)Log("motion",new {id=target.GetInstanceID(),type=target.GetType().Name,position=Vec(target.transform.position),fired=Get(target,"fired"),timing=Timing(Get(target,"attackScript"))});
        }
        foreach(var entry in knownColliders)
        {
            var c=entry.Value;if(c==null)continue;
            string state=c.enabled+":"+c.gameObject.activeInHierarchy;string previous;
            if(!colliderStates.TryGetValue(entry.Key,out previous) || state!=previous)
            {colliderStates[entry.Key]=state;Log("collider-change",new {id=entry.Key,path=ObjectPath(c),state=state,position=Vec(c.transform.position)});}
        }
    }
    void OnGUI()
    {
        var previous=GUI.matrix; float scale=Mathf.Max(1,Screen.width/1920f);GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
        GUI.Box(new Rect(12,12,630,86),"");
        GUI.Label(new Rect(24,20,900,90),"ORIGINAL GAME / ENEMY RECOVERY\n"+phase+" | isolated profile | original attack logic\n"+fixtureLabel,new GUIStyle(GUI.skin.label){fontSize=18});
        GUI.matrix=previous;
    }
    void OnDestroy(){if(events!=null){events.Dispose();events=null;}}

    sealed class AssetGraph
    {
        readonly Dictionary<int,string> ids=new Dictionary<int,string>();
        readonly Queue<Object> pending=new Queue<Object>();
        readonly List<object> objects=new List<object>();
        public int Count {get{return objects.Count;}}
        public object Reference(Object obj)
        {
            if(obj==null)return null;
            string id;if(!ids.TryGetValue(obj.GetInstanceID(),out id)){id="o"+(ids.Count+1).ToString("D5");ids.Add(obj.GetInstanceID(),id);pending.Enqueue(obj);}
            return new {reference=id,type=obj.GetType().FullName,name=obj.name,path=ObjectPath(obj)};
        }
        public List<object> Complete()
        {
            while(pending.Count>0)
            {
                if(objects.Count>20000)throw new InvalidOperationException("Asset graph exceeded bounded scope.");
                var obj=pending.Dequeue();
                objects.Add(new {id=ids[obj.GetInstanceID()],runtimeId=obj.GetInstanceID(),type=obj.GetType().FullName,name=obj.name,path=ObjectPath(obj),data=Describe(obj)});
            }
            return objects;
        }
        object Describe(Object obj)
        {
            var go=obj as GameObject;
            if(go!=null)return new {active=go.activeSelf,layer=go.layer,components=go.GetComponents<Component>().Select(c=>Reference(c)).ToArray(),children=Enumerable.Range(0,go.transform.childCount).Select(i=>Reference(go.transform.GetChild(i).gameObject)).ToArray()};
            var transform=obj as Transform;
            if(transform!=null)return new {gameObject=Reference(transform.gameObject),localPosition=Vec(transform.localPosition),localScale=Vec(transform.localScale),localRotation=new[]{transform.localRotation.x,transform.localRotation.y,transform.localRotation.z,transform.localRotation.w}};
            var anim=obj as AnimationClip;
            if(anim!=null)return new {length=anim.length,frameRate=anim.frameRate,wrapMode=anim.wrapMode.ToString(),legacy=anim.legacy,events=anim.events.Select(e=>new {time=e.time,function=e.functionName,floatParameter=e.floatParameter,intParameter=e.intParameter,stringParameter=e.stringParameter,objectParameter=Reference(e.objectReferenceParameter)}).ToArray()};
            var collider=obj as Collider2D;
            if(collider!=null)
            {
                var paths=new List<object>();var polygon=collider as PolygonCollider2D;
                if(polygon!=null)for(int i=0;i<polygon.pathCount;i++)paths.Add(polygon.GetPath(i).Select(v=>new[]{v.x,v.y}).ToArray());
                var circle=collider as CircleCollider2D;var box=collider as BoxCollider2D;
                return new {gameObject=Reference(collider.gameObject),enabled=collider.enabled,trigger=collider.isTrigger,offset=new[]{collider.offset.x,collider.offset.y},radius=circle!=null?(float?)circle.radius:null,size=box!=null?new[]{box.size.x,box.size.y}:null,paths=paths};
            }
            var particle=obj as ParticleSystem;
            if(particle!=null){var m=particle.main;return new {gameObject=Reference(particle.gameObject),duration=m.duration,loop=m.loop,simulationSpeed=m.simulationSpeed,startLifetime=Curve(m.startLifetime),startDelay=Curve(m.startDelay),stopAction=m.stopAction.ToString()};}
            if(obj is MonoBehaviour || obj is ScriptableObject)
            {
                var fields=Fields(obj,0,new HashSet<object>());var component=obj as Component;
                if(component!=null)fields["$gameObject"]=Reference(component.gameObject);
                return fields;
            }
            return new {kind="presentation-or-native-reference"};
        }
        object Curve(ParticleSystem.MinMaxCurve c){return new {mode=c.mode.ToString(),constant=c.constant,constantMin=c.constantMin,constantMax=c.constantMax,multiplier=c.curveMultiplier,curve=Value(c.curve,0,new HashSet<object>()),curveMin=Value(c.curveMin,0,new HashSet<object>()),curveMax=Value(c.curveMax,0,new HashSet<object>())};}
        Dictionary<string,object> Fields(object value,int depth,HashSet<object> chain)
        {
            var result=new Dictionary<string,object>();
            for(var type=value.GetType();type!=null && type!=typeof(MonoBehaviour) && type!=typeof(ScriptableObject) && type!=typeof(Object);type=type.BaseType)
            foreach(var f in type.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly).OrderBy(f=>f.Name,StringComparer.Ordinal))
            {
                if(f.IsNotSerialized || (!f.IsPublic && !f.GetCustomAttributes(false).Any(a=>a.GetType().Name=="SerializeField" || a.GetType().Name=="SerializeReference")))continue;
                try {result[type.FullName+"::"+f.Name]=Value(f.GetValue(value),depth+1,chain);}
                catch(Exception e){result[type.FullName+"::"+f.Name]=new {readError=e.GetBaseException().Message};}
            }
            return result;
        }
        object Value(object value,int depth,HashSet<object> chain)
        {
            if(value==null)return null; var type=value.GetType();
            if(value is Object)return Reference((Object)value);
            if(type.IsPrimitive || value is string || value is decimal)return value;
            if(type.IsEnum)return new {enumType=type.FullName,name=value.ToString(),value=Convert.ToInt64(value)};
            var curve=value as AnimationCurve;if(curve!=null)return new {keys=curve.keys.Select(k=>new {time=k.time,value=k.value,inTangent=k.inTangent,outTangent=k.outTangent,inWeight=k.inWeight,outWeight=k.outWeight,weightedMode=k.weightedMode.ToString()}).ToArray(),preWrap=curve.preWrapMode.ToString(),postWrap=curve.postWrapMode.ToString()};
            var callback=value as Delegate;if(callback!=null)return callback.GetInvocationList().Select(d=>new {method=d.Method.ToString(),declaringType=d.Method.DeclaringType.FullName,target=d.Target==null?null:d.Target.GetType().FullName}).ToArray();
            if(depth>24)return new {unresolved="depth-limit",type=type.FullName};
            if(chain.Contains(value))return new {managedCycle=type.FullName};
            chain.Add(value);object result;
            var list=value as IEnumerable;
            if(list!=null){var elements=new List<object>();foreach(var element in list)elements.Add(Value(element,depth+1,chain));result=elements;}
            else result=new {managedType=type.FullName,fields=Fields(value,depth+1,chain)};
            chain.Remove(value);return result;
        }
    }
}
