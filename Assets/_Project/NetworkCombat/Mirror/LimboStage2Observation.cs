using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Interactions;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    // Passive outside the explicitly named fixture profile. No alternate combat or enemy lifecycle.
    public sealed class LimboStage2Observation : MonoBehaviour
    {
        private StreamWriter log;
        private bool fixture,positioned,paused,pauseFinished,destroyed,weaponProbe,deathProbe;
        private readonly HashSet<uint> knockbacks=new HashSet<uint>();
        private float pauseEnd;
        private ulong boundaryAction;
        private int boundaryIndex;
        private bool boundaryMoved,boundaryMeasured;
        private Vector2 boundaryPosition;
        private Vector2 anchor;
        private int quadrant=-1,handoff;
        private uint replacement;
        private string runId;
        private float cleanupAfter;
        private bool cleanupRecorded;
        private readonly HashSet<int> screenshots=new HashSet<int>();
        private readonly Dictionary<uint,string> phases=new Dictionary<uint,string>();
        private readonly Dictionary<uint,int> health=new Dictionary<uint,int>();
        private readonly Dictionary<CombatantBehaviour,Action<int,int>> healthListeners=new();
        private readonly Dictionary<CombatantBehaviour,Action<MonsterSupergroup.GAS.ConfirmedKill>> killListeners=new();
        private readonly Dictionary<uint,double> firstHit=new Dictionary<uint,double>();
        private readonly HashSet<uint> ended=new HashSet<uint>();
        private readonly Dictionary<NetworkEnemySimulationAgent,Action<EnemyAttackPresentationEdge>> listeners=new Dictionary<NetworkEnemySimulationAgent,Action<EnemyAttackPresentationEdge>>();
        private readonly HashSet<string> geometry=new HashSet<string>();
        private double Elapsed=>NetworkCombatWorld.Instance!=null?NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot.Elapsed:0;
        private void Start()
        {
            fixture=LimboReferenceLaunch.Profile=="stage2-fixture";
            Directory.CreateDirectory(LimboReferenceLaunch.OutputDirectory);
            log=new StreamWriter(Path.Combine(LimboReferenceLaunch.OutputDirectory,"stage2-observation.jsonl")){AutoFlush=true};
            PlayerDamageInteraction.DamageAttempted+=DamageAttempted;
            Write("mode",fixture?"Explicit fixture: XP pickup multiplier 0, healing, positioning, reset, pause and handoff; weapons enabled only during the logged 78–87 second probe. Not normal play.":"Passive continuous preview observation.");
        }
        private void Write(string kind,string detail,object payload=null)=>log?.WriteLine(JsonUtility.ToJson(new Row{kind=kind,detail=detail,
            realtime=Time.realtimeSinceStartupAsDouble,combat=EnemySimulationClock.CombatNow,elapsed=Elapsed,payload=payload!=null?JsonUtility.ToJson(payload):null}));
        private void DamageAttempted(PlayerDamageInteraction source,PlayerMovement player,int amount)
        {
            var agent=source.GetComponentInParent<NetworkEnemySimulationAgent>();
            Write("damage-attempt",source.damageType.ToString(),new HitRow{enemy=agent!=null?agent.netId:0,player=player.GetComponent<NetworkIdentity>().netId,
                damage=amount,instance=source.GetInstanceID(),active=source.gameObject.activeInHierarchy,
                action=agent!=null?ObservedAction(agent):default});
        }
        private void Update()
        {
            var world=NetworkEnemySimulationWorld.Instance;
            if(world==null||NetworkClient.localPlayer==null||NetworkCombatWorld.Instance==null)
            {
                if(runId!=null&&!cleanupRecorded)
                {
                    if(cleanupAfter==0)cleanupAfter=Time.realtimeSinceStartup+.3f;
                    if(Time.realtimeSinceStartup>=cleanupAfter)
                    {
                        cleanupRecorded=true;
                        Write("cleanup",runId,new CleanupRow{
                            damageAreas=FindObjectsByType<PlayerDamageInteraction>(FindObjectsSortMode.None).Count(p=>p.isActiveAndEnabled),
                            warnings=FindObjectsByType<EnemyAttackWarning>(FindObjectsSortMode.None).Count(w=>w.GetComponentsInChildren<Renderer>().Any(r=>r.enabled))});
                    }
                }
                return;
            }
            string currentRun=NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot.RunId;
            if(runId!=currentRun)
            {
                if(paused)Time.timeScale=1;
                runId=currentRun;positioned=paused=pauseFinished=destroyed=weaponProbe=deathProbe=false;quadrant=-1;handoff=0;replacement=0;cleanupAfter=0;cleanupRecorded=false;
                boundaryAction=0;boundaryIndex=0;boundaryMoved=boundaryMeasured=false;screenshots.Clear();
                foreach(var pair in listeners)if(pair.Key!=null)pair.Key.AttackPresentationChanged-=pair.Value;
                listeners.Clear();ClearHealthObservers();phases.Clear();health.Clear();firstHit.Clear();ended.Clear();geometry.Clear();knockbacks.Clear();
                Write("round",runId);
            }
            // Capture the player's rendered frame, as in the existing gameplay process probes.
            // These are observation artifacts; all gameplay inputs still use the ordinary components.
            foreach(int at in fixture?new[]{10,20,30,40,48,76,85,96}:new[]{30,60,100,110,150,180,200,209})
                if(Elapsed>=at&&screenshots.Add(at))
                {
                    string name="render-"+runId+"-"+at+".png";
                    ScreenCapture.CaptureScreenshot(Path.Combine(LimboReferenceLaunch.OutputDirectory,name));
                    Write("render-capture",name);
                }
            var enemies=NetworkClient.spawned.Values.Where(i=>i!=null).Select(i=>i.GetComponent<NetworkEnemySimulationAgent>())
                .Where(e=>e!=null&&e.ProductEnemyInitialized&&e.Birth.Enabled).ToArray();
            foreach(var enemy in enemies)
            {
                var controller=enemy.GetComponent<EnemyController>();var action=ObservedAction(enemy);
                if(fixture&&controller.isElite&&action.Facing.x>0&&action.PhaseAt(EnemySimulationClock.CombatNow)==EnemyAttackPresentationPhase.Recovery
                    &&EnemySimulationClock.CombatNow-action.ActiveUntil>.36&&geometry.Add("right-recovery/"+enemy.netId+"/"+action.ActionId))
                {
                    var visual=controller.enemyAnimator.animancer.Layers[0].CurrentState;
                    if(visual!=null)Write("elite-right-recovery-tail",enemy.netId.ToString(),new VisualRow{
                        clipLength=visual.Length,animationTime=visual.TimeD,playing=visual.IsPlaying,action=action});
                }
                if(fixture&&controller.IsInKnockbackState&&knockbacks.Add(enemy.netId))Write("knockback-observed",enemy.netId.ToString(),enemy.CaptureCurrentCheckpoint());
                string phase=enemy.Assignment.Epoch+"/"+action.ActionId+"/"+action.Phase;
                if(!phases.TryGetValue(enemy.netId,out var old)||phase!=old)
                {phases[enemy.netId]=phase;Write("phase",enemy.netId+"/"+(enemy.Authority.RunsCombatDecisions?"simulator":"replica"),action);}
                if(!listeners.ContainsKey(enemy))
                {
                    Action<EnemyAttackPresentationEdge> listener=edge=>Write("presentation-edge",enemy.netId.ToString(),edge);
                    listeners[enemy]=listener;enemy.AttackPresentationChanged+=listener;
                    var attack=controller.attackScript;
                    Write("configuration",enemy.netId.ToString(),new ConfigRow{birth=enemy.Birth, w=attack!=null?attack.WarningTime:0,a=attack!=null?attack.AttackTime:0,r=attack!=null?attack.RecoveryTime:0,
                        cooldown=controller.attackCooldown,distance=controller.attackDistance,elite=controller.isElite});
                }
                WatchHealth(enemy);
                if(fixture)
                {
                    var instance=controller.GetComponent<EnemyAttackMelee>()?.SimulationAttackInstance??enemy.GetComponent<NetworkEnemyMeleeReplica>()?.ReplicaAttackInstance;
                    var interaction=instance!=null?instance.damageInteraction:enemy.GetComponent<EnemyContactDamage>()?.DamageInteraction;
                    if(interaction!=null)
                    {
                        string token=enemy.netId+"/"+interaction.GetInstanceID()+"/"+phase+"/"+interaction.gameObject.activeInHierarchy;
                        if(geometry.Add(token)) WriteGeometry(enemy,interaction,action);
                    }
                }
            }
            if(fixture&&NetworkServer.active&&!deathProbe&&LimboReferenceLaunch.Argument("--limbo-fixture-mode=")=="melee-cleanup"&&Elapsed>=20)
            {
                // Explicit Active-phase network recycling, not a fabricated combat kill. Normal
                // weapon deaths are observed through the existing Combatant lifecycle below.
                var victim=enemies.FirstOrDefault(e=>e.IsCanonicalAlive&&e.Birth.ContactRadius==0
                    &&ObservedAction(e).Phase==EnemyAttackPresentationPhase.Active
                    &&ObservedAction(e).PhaseAt(EnemySimulationClock.CombatNow)==EnemyAttackPresentationPhase.Active);
                if(victim!=null)
                {
                    deathProbe=true;uint id=victim.netId;
                    var instance=victim.GetComponent<EnemyAttackMelee>().SimulationAttackInstance??victim.GetComponent<NetworkEnemyMeleeReplica>().ReplicaAttackInstance;
                    Write("fixture-active-recycle",id.ToString(),ObservedAction(victim));
                    NetworkServer.Destroy(victim.gameObject);
                    Write("fixture-recycle-cleanup",id.ToString(),new CleanupRow{
                        damageAreas=instance!=null&&instance.damageInteraction.isActiveAndEnabled?1:0,
                        warnings=instance!=null&&instance.gameObject.activeInHierarchy?1:0});
                }
            }
            if(fixture&&Elapsed>=2&&Elapsed<42)
            {
                // Isolated direction fixture: hold the simulator's position only between attacks.
                // Without this, fast elites walk through the fixed player during cooldown and the
                // nominal quadrant no longer describes the observed attack. No reset or epoch change.
                int q=Math.Min(3,(int)((Elapsed-2)/10));
                Vector2[] offsets={new(.9f,.6f),new(-.9f,.6f),new(-.9f,-.6f),new(.9f,-.6f)};
                foreach(var enemy in enemies.Where(e=>e.IsCanonicalAlive&&e.Birth.ContactRadius==0&&e.Authority.RunsCombatDecisions))
                {
                    var action=enemy.CaptureCurrentCheckpoint().Movement.Runtime.Action;
                    if(action.PhaseAt(EnemySimulationClock.CombatNow)!=EnemyAttackPresentationPhase.Inactive)continue;
                    if(!NetworkClient.spawned.TryGetValue(enemy.Assignment.AggroTargetPlayerId,out var target))continue;
                    Vector2 at=(Vector2)target.transform.position+offsets[q];
                    enemy.transform.position=at;
                    var body=enemy.GetComponent<Rigidbody2D>();if(body!=null){body.position=at;body.linearVelocity=Vector2.zero;}
                }
            }
            if(NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot.Phase==WavePhase.Completed&&!cleanupRecorded)
            {
                if(cleanupAfter==0)cleanupAfter=Time.realtimeSinceStartup+.3f;
                if(Time.realtimeSinceStartup>=cleanupAfter)
                {
                    cleanupRecorded=true;
                    Write("completed-cleanup",runId,new CleanupRow{
                        damageAreas=FindObjectsByType<PlayerDamageInteraction>(FindObjectsSortMode.None).Count(p=>p.isActiveAndEnabled),
                        warnings=FindObjectsByType<EnemyAttackWarning>(FindObjectsSortMode.None).Count(w=>w.GetComponentsInChildren<Renderer>().Any(r=>r.enabled))});
                }
            }
            if(fixture&&Elapsed>1&&Elapsed<109)
            {
                if(LimboReferenceLaunch.Argument("--limbo-fixture-mode=")?.StartsWith("l-")==true)DriveLimitedFixture();
                else DriveFixture(world,enemies.FirstOrDefault(e=>e.IsCanonicalAlive));
            }
        }
        private void WatchHealth(NetworkEnemySimulationAgent enemy)
        {
            var combatant=enemy.GetComponent<CombatantBehaviour>();
            if(healthListeners.ContainsKey(combatant))return;
            uint id=enemy.netId;health[id]=combatant.CurrentHealth;
            Action<int,int> changed=(current,maximum)=>
            {
                int previous=health[id];
                if(current<previous)
                {
                    if(!firstHit.ContainsKey(id))firstHit[id]=EnemySimulationClock.CombatNow;
                    Write("enemy-health",id.ToString(),new HealthRow{previous=previous,current=current,action=enemy!=null?ObservedAction(enemy):default});
                }
                else if(current>previous)
                {
                    firstHit.Remove(id);Write("enemy-health-restored",id.ToString(),new HealthRow{previous=previous,current=current});
                }
                health[id]=current;
            };
            Action<MonsterSupergroup.GAS.ConfirmedKill> killed=kill=>
            {
                if(ended.Add(id))Write("enemy-death",id.ToString(),new DeathRow{hasFirstHit=firstHit.ContainsKey(id),
                    seconds=firstHit.TryGetValue(id,out double start)?EnemySimulationClock.CombatNow-start:0});
            };
            healthListeners[combatant]=changed;killListeners[combatant]=killed;
            combatant.HealthChanged+=changed;combatant.ConfirmedKillReceived+=killed;
        }
        private void ClearHealthObservers()
        {
            foreach(var p in healthListeners)if(p.Key!=null)p.Key.HealthChanged-=p.Value;
            foreach(var p in killListeners)if(p.Key!=null)p.Key.ConfirmedKillReceived-=p.Value;
            healthListeners.Clear();killListeners.Clear();
        }
        private void DriveLimitedFixture()
        {
            var local=NetworkClient.localPlayer;var player=local.GetComponent<PlayerMovement>();
            bool kill=LimboReferenceLaunch.Argument("--limbo-fixture-mode=")=="l-kill";
            local.GetComponent<PlayerBuildRuntime>()?.SetWeaponExecutionEnabled(kill);player.StopMovement();
            if(!positioned){positioned=true;anchor=local.transform.position;Write("fixture-l-mode",kill?"Normal weapon attacks enabled; XP pickup multiplier zero in fixture only.":"Weapons disabled; no kills.");}
            // Prevent upgrade dialogs from pausing this isolated population experiment. Birth XP is untouched.
            player.PlayerStats.currentStats.xpModifier=0;
            if(local.GetComponent<CombatantBehaviour>().CurrentHealth<250){Write("fixture-heal","IncreaseHealth(500)");player.IncreaseHealth(500);}
            if(NetworkServer.active&&!destroyed&&Elapsed>=20.6&&LimboReferenceLaunch.Argument("--limbo-fixture-mode=")=="l-boundary")
            {
                // Explicit lifecycle probe: free 50 slots just before the original segment ends.
                // This is not a combat kill measurement or normal play.
                destroyed=true;
                var removed=NetworkServer.spawned.Values.Where(i=>i!=null&&i.GetComponent<NetworkEnemySimulationAgent>() is { Birth: { Enabled:true } })
                    .OrderBy(i=>i.netId).Take(50).ToArray();
                Write("fixture-l-boundary-despawn",string.Join(",",removed.Select(i=>i.netId)));
                foreach(var identity in removed)NetworkServer.Destroy(identity.gameObject);
            }
        }
        private void WriteGeometry(NetworkEnemySimulationAgent enemy,PlayerDamageInteraction interaction,EnemyActionState action)
        {
            var polygon=interaction.GetComponent<PolygonCollider2D>();var circle=interaction.GetComponent<CircleCollider2D>();
            var row=new GeometryRow{enemy=enemy.netId,instance=interaction.GetInstanceID(),active=interaction.gameObject.activeInHierarchy,
                action=action,position=interaction.transform.position,scale=interaction.transform.lossyScale,
                damage=interaction.enemyStats!=null?interaction.enemyStats.Damage:0,
                currentStatsBound=ReferenceEquals(interaction.enemyStats,enemy.GetComponent<EnemyController>().stats),resetVersion=enemy.ReferenceResetVersion};
            if(polygon!=null)row.points=polygon.GetPath(0).Select(p=>(Vector2)polygon.transform.TransformPoint(p+polygon.offset)).ToArray();
            if(circle!=null){row.radius=circle.radius;row.position=circle.transform.TransformPoint(circle.offset);}
            Write("geometry",enemy.Authority.Role.ToString(),row);
        }
        private static EnemyActionState ObservedAction(NetworkEnemySimulationAgent enemy)=>enemy.Authority.RunsCombatDecisions
            ?enemy.CaptureCurrentCheckpoint().Movement.Runtime.Action:enemy.LatestAttackPresentation.Checkpoint.Movement.Runtime.Action;
        private void DriveFixture(NetworkEnemySimulationWorld world,NetworkEnemySimulationAgent enemy)
        {
            var local=NetworkClient.localPlayer;var player=local.GetComponent<PlayerMovement>();
            if(LimboReferenceLaunch.Argument("--limbo-fixture-mode=")=="art-death")
            {
                local.GetComponent<PlayerBuildRuntime>()?.SetWeaponExecutionEnabled(true);
                player.StopMovement();player.PlayerStats.currentStats.xpModifier=0;
                if(local.GetComponent<CombatantBehaviour>().CurrentHealth<250)
                {Write("fixture-heal","IncreaseHealth(500), art death fixture only");player.IncreaseHealth(500);}
                if(!positioned&&NetworkServer.active&&enemy!=null)
                {
                    var observer=NetworkServer.connections.Values.Select(c=>c.identity).FirstOrDefault(i=>i!=null&&i.netId!=local.netId);
                    bool remoteTarget=LimboReferenceLaunch.Argument("--limbo-fixture-target=")=="client";
                    if(remoteTarget&&observer==null)return;
                    var deathTarget=remoteTarget?observer:local;
                    world.RepositionReferenceEnemy(enemy,(Vector2)deathTarget.transform.position+new Vector2(.8f,-.4f));
                    world.RequestTargetChange(enemy.netId,deathTarget.netId,EnemyTargetChangeReason.Forced);
                    positioned=true;Write("fixture-art-death","One production reposition; ordinary equipped weapon; XP pickup zero and explicit healing; no forced kill or enemy pool reuse.");
                }
                return;
            }
            local.GetComponent<PlayerBuildRuntime>()?.SetWeaponExecutionEnabled(Elapsed>=78&&Elapsed<87);player.StopMovement();
            if(Elapsed>=78&&!weaponProbe){weaponProbe=true;Write("fixture-weapon-probe","Normal equipped weapon enabled for knockback/death check; XP pickup multiplier zero in fixture only.");}
            player.PlayerStats.currentStats.xpModifier=0;
            if(!positioned){anchor=local.transform.position;positioned=true;Write("fixture-position",anchor.ToString());}
            if(local.GetComponent<CombatantBehaviour>().CurrentHealth<250){Write("fixture-heal","IncreaseHealth(500); maximum stays 500");player.IncreaseHealth(500);}
            bool targetRemote=LimboReferenceLaunch.Argument("--limbo-fixture-target=")=="client";
            Vector2 position=anchor;
            // Keep the non-target observer away during directional hit measurements.
            if(Elapsed<49&&NetworkClient.connection!=null&&((NetworkServer.active&&targetRemote)||(!NetworkServer.active&&!targetRemote)))position+=new Vector2(-10,0);
            bool isTarget=targetRemote?!NetworkServer.active:NetworkServer.active;
            if(isTarget&&enemy!=null&&Elapsed>=42&&Elapsed<52)position=ProbeBoundary(enemy,player,position);
            local.transform.position=position;var body=local.GetComponent<Rigidbody2D>();body.position=position;body.linearVelocity=Vector2.zero;
            if(!NetworkServer.active)return;
            var remote=NetworkServer.connections.Values.Select(c=>c.identity).FirstOrDefault(i=>i!=null&&i.netId!=local.netId);
            if(remote==null&&targetRemote)return;
            var target=targetRemote?remote:local;
            int q=Math.Min(3,(int)((Elapsed-2)/10));
            if(enemy!=null&&Elapsed>=2&&Elapsed<42&&q!=quadrant)
            {
                quadrant=q;Vector2[] offsets={new(.9f,.6f),new(-.9f,.6f),new(-.9f,-.6f),new(.9f,-.6f)};
                bool contact=enemy.Birth.ContactRadius>0;
                world.RepositionReferenceEnemy(enemy,(Vector2)target.transform.position+(contact?offsets[q]*.3f:offsets[q]));
                world.RequestTargetChange(enemy.netId,target.netId,EnemyTargetChangeReason.Forced);
                Write("fixture-quadrant",q+"; existing reposition/reset; not enemy pool reuse",enemy.Birth);
            }
            if(!paused&&!pauseFinished&&Elapsed>=44)
            {paused=true;pauseEnd=Time.realtimeSinceStartup+3;Time.timeScale=0;Write("fixture-pause", "3 real seconds");}
            if(paused&&Time.realtimeSinceStartup>=pauseEnd)
            {paused=false;pauseFinished=true;Time.timeScale=1;Write("fixture-resume", "");}
            if(remote!=null&&enemy!=null&&handoff<3&&Elapsed>=52+handoff*9)
            {
                var snapshot=enemy.CaptureCurrentCheckpoint().Movement;
                if(!enemy.Authority.RunsCombatDecisions)world.Registry.TryGetLatestSnapshot(enemy.netId,out snapshot);
                var expected=handoff==0?EnemyAttackPresentationPhase.Warning:handoff==1?EnemyAttackPresentationPhase.Active:EnemyAttackPresentationPhase.Recovery;
                if(enemy.Birth.ContactRadius>0||snapshot.Runtime.Action.PhaseAt(EnemySimulationClock.CombatNow)==expected)
                {
                    var next=enemy.Assignment.AggroTargetPlayerId==local.netId?remote:local;
                    Write("fixture-handoff",expected+"/"+world.RequestTargetChange(enemy.netId,next.netId,EnemyTargetChangeReason.Forced),snapshot);
                    handoff++;
                }
            }
            if(Elapsed>=89&&!destroyed){destroyed=true;if(enemy!=null){Write("fixture-destroy",enemy.netId.ToString());NetworkServer.Destroy(enemy.gameObject);}}
            if(enemy!=null&&Elapsed>=90&&replacement!=enemy.netId)
            {
                replacement=enemy.netId;world.RepositionReferenceEnemy(enemy,(Vector2)target.transform.position+new Vector2(.5f,.4f));
                world.RequestTargetChange(enemy.netId,target.netId,EnemyTargetChangeReason.Forced);
                Write("fixture-high-variant","New network enemy; existing attack pool can reuse attack object; not enemy pool reuse.",enemy.Birth);
            }
        }
        private Vector2 ProbeBoundary(NetworkEnemySimulationAgent enemy,PlayerMovement player,Vector2 fallback)
        {
            var hitbox=player.GetComponentsInChildren<PlayerHitbox>().Select(h=>h.GetComponent<Collider2D>()).FirstOrDefault(c=>c!=null&&c.enabled);
            if(hitbox==null)return fallback;
            var controller=enemy.GetComponent<EnemyController>();
            var instance=controller.GetComponent<EnemyAttackMelee>()?.SimulationAttackInstance??enemy.GetComponent<NetworkEnemyMeleeReplica>()?.ReplicaAttackInstance;
            var interaction=instance!=null?instance.damageInteraction:enemy.GetComponent<EnemyContactDamage>()?.DamageInteraction;
            var collider=interaction!=null?interaction.GetComponent<Collider2D>():null;
            Vector2 centerOffset=(Vector2)hitbox.bounds.center-(Vector2)player.transform.position;
            if(enemy.Birth.ContactRadius>0&&collider is CircleCollider2D circle)
            {
                // Keep the real player collider on either side of the contact boundary while enemies may chase.
                bool inside=Elapsed>=47;
                Vector2 center=circle.transform.TransformPoint(circle.offset);
                float distance=circle.radius+hitbox.bounds.extents.x+(inside?-.15f:.15f);
                if(boundaryIndex!=(inside?2:1)){boundaryIndex=inside?2:1;Write("fixture-boundary",inside?"contact inside":"contact outside");}
                return center+Vector2.right*distance-centerOffset;
            }
            var action=enemy.Authority.RunsCombatDecisions?enemy.CaptureCurrentCheckpoint().Movement.Runtime.Action:enemy.LatestAttackPresentation.Checkpoint.Movement.Runtime.Action;
            var phase=action.PhaseAt(EnemySimulationClock.CombatNow);
            if(phase==EnemyAttackPresentationPhase.Warning&&action.ActionId!=boundaryAction&&boundaryIndex<2)
            {boundaryAction=action.ActionId;boundaryIndex++;boundaryMoved=false;boundaryMeasured=false;}
            if(action.ActionId!=boundaryAction||boundaryIndex==0)return fallback;
            if(!boundaryMoved&&phase==EnemyAttackPresentationPhase.Warning&&EnemySimulationClock.CombatNow>=action.WarningStartedAt+.12)
            {
                float scale=controller.isElite?1.5f:1;
                // Move after direction lock: behind the polygon, then just inside its front tip.
                float reach=boundaryIndex==1?-1.5f:2.0658707f*scale+hitbox.bounds.extents.x-.12f;
                boundaryPosition=(Vector2)enemy.transform.position+action.Facing.normalized*reach-centerOffset;
                boundaryMoved=true;Write("fixture-boundary",boundaryIndex==1?"melee outside after lock":"melee tip inside after lock",action);
            }
            if(boundaryMoved&&!boundaryMeasured&&action.Phase==EnemyAttackPresentationPhase.Active&&collider!=null&&collider.isActiveAndEnabled)
            {
                boundaryMeasured=true;var distance=collider.Distance(hitbox);
                Write("fixture-boundary-distance",boundaryIndex==1?"outside":"inside",new BoundaryRow{overlapped=distance.isOverlapped,distance=distance.distance,action=action});
            }
            return boundaryMoved&&(phase==EnemyAttackPresentationPhase.Warning||phase==EnemyAttackPresentationPhase.Active)?boundaryPosition:fallback;
        }
        private void OnDestroy()
        {
            PlayerDamageInteraction.DamageAttempted-=DamageAttempted;
            foreach(var pair in listeners)if(pair.Key!=null)pair.Key.AttackPresentationChanged-=pair.Value;
            ClearHealthObservers();if(paused)Time.timeScale=1;log?.Dispose();
        }
        [Serializable] private class Row{public string kind,detail,payload;public double realtime,combat,elapsed;}
        [Serializable] private class ConfigRow{public EnemyBirthParameters birth;public float w,a,r,cooldown,distance;public bool elite;}
        [Serializable] private class HitRow{public uint enemy,player;public int damage,instance;public bool active;public EnemyActionState action;}
        [Serializable] private class VisualRow{public double clipLength,animationTime;public bool playing;public EnemyActionState action;}
        [Serializable] private class HealthRow{public int previous,current;public EnemyActionState action;}
        [Serializable] private class DeathRow{public bool hasFirstHit;public double seconds;}
        [Serializable] private class BoundaryRow{public bool overlapped;public float distance;public EnemyActionState action;}
        [Serializable] private class CleanupRow{public int damageAreas,warnings;}
        [Serializable] private class GeometryRow{public uint enemy,resetVersion;public int instance,damage;public bool active,currentStatsBound;public Vector3 position,scale;public Vector2[] points;public float radius;public EnemyActionState action;}
    }
}
