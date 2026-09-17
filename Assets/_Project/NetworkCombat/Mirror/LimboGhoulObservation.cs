using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Interactions;
using AstralShift.HellMaiden.Player;
using AstralShift.HellMaiden.Player.Attacks;
using AstralShift.Managers;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    // Explicit technical inputs only; ghoul itself remains a passive playable profile.
    public sealed class LimboGhoulObservation : MonoBehaviour
    {
        private StreamWriter log;
        private string run;
        private bool fixture,technical,positioned,paused,pauseDone,cleanup;
        private bool motionPreview;
        private float nextSelection,resumeAt;
        private uint slow;
        private int handoff;
        private Vector2 anchor;
        private readonly Dictionary<uint,int> placed=new();
        private readonly HashSet<uint> requested=new();
        private readonly Dictionary<uint,string> phases=new();
        private readonly HashSet<string> pictures=new();
        private readonly HashSet<string> boundaries=new();
        private KnockbackSettings fixtureKnockback;
        private float originalDistance,originalStagger;
        private double Elapsed=>NetworkCombatWorld.Instance!=null?NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot.Elapsed:0;
        public static string Case=>LimboReferenceLaunch.Argument("--limbo-ghoul-case=")??"main";
        public static string FixtureRulesName=>Case switch{"interrupt"=>"GhoulInterrupt","boundary"=>"GhoulBoundary","limited"=>"GhoulLimited","curve"=>"GhoulCurve","imp-burst"=>"GhoulImpBurst","soul-burst"=>"GhoulSoulBurst","rusher"=>"GhoulRusher",_=>"GhoulFixture"};
        private static EnemyActionState Action(NetworkEnemySimulationAgent e)=>e.Authority.RunsCombatDecisions
            ?e.CaptureCurrentCheckpoint().Movement.Runtime.Action:e.GetComponent<NetworkEnemyMeleeReplica>().AppliedSequenceState;
        private void Start()
        {
            motionPreview = LimboReferenceLaunch.Profile == "ghoul-motion";
            fixture=LimboReferenceLaunch.Profile=="ghoul-fixture";technical=fixture||LimboReferenceLaunch.Profile=="ghoul-validation"||motionPreview;
            Directory.CreateDirectory(LimboReferenceLaunch.OutputDirectory);
            log=new StreamWriter(Path.Combine(LimboReferenceLaunch.OutputDirectory,"ghoul.jsonl")){AutoFlush=true};
            Write("configuration",motionPreview?"MOTION PREVIEW: manual movement, weapon suppression, healing below 300; no screenshots; not pressure evidence.":fixture?"FIXTURE: placement, weapon suppression, healing, forced handoffs and clock leases; not pressure evidence.":technical?"TECHNICAL: ordinary auto-walk, first-card selection, healing below 300; not pressure evidence.":"PLAYABLE: passive observation.");
            PlayerDamageInteraction.DamageAttempted+=Hit;
            if (motionPreview) Write("motion-preview", "65-second isolated Ghoul: weapons suppressed, health restored below 300; manual movement, no placement or screenshots. Not pressure evidence.");
        }
        private void Write(string kind,string detail,object data=null)=>log?.WriteLine(JsonUtility.ToJson(new Row{kind=kind,detail=detail,run=run,elapsed=Elapsed,combat=EnemySimulationClock.CombatNow,realtime=Time.realtimeSinceStartupAsDouble,payload=data==null?null:JsonUtility.ToJson(data)}));
        private void Hit(PlayerDamageInteraction source,PlayerMovement player,int amount)
        {
            var e=source.GetComponentInParent<NetworkEnemySimulationAgent>();
            if(e!=null&&e.Birth.SourceEnemy=="Ghoul")Write("damage-attempt",source.damageType.ToString(),new HitRow{id=e.netId,player=player.GetComponent<NetworkIdentity>().netId,amount=amount,position=player.transform.position,action=Action(e)});
        }
        private void Update()
        {
            if(NetworkCombatWorld.Instance==null||NetworkClient.localPlayer==null)
            {
                if(run!=null&&!cleanup){cleanup=true;Write("cleanup","World released",new Cleanup{areas=FindObjectsByType<PlayerDamageInteraction>(FindObjectsSortMode.None).Count(x=>x.isActiveAndEnabled),warnings=FindObjectsByType<EnemyAttackWarning>(FindObjectsSortMode.None).Length});}
                return;
            }
            var progress=NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot;
            if(run!=progress.RunId){ReleaseClock();RestoreFixtureKnockback();run=progress.RunId;placed.Clear();requested.Clear();phases.Clear();pictures.Clear();boundaries.Clear();handoff=0;positioned=pauseDone=cleanup=false;Write("round",run);}
            var local=NetworkClient.localPlayer;var player=local.GetComponent<PlayerMovement>();
            if (motionPreview && !BootGameplayNetworkManager.CombatHasEnded) local.GetComponent<PlayerBuildRuntime>()?.SetWeaponExecutionEnabled(false);
            if(technical&&!BootGameplayNetworkManager.CombatHasEnded)
            {
                if(local.GetComponent<CombatantBehaviour>().CurrentHealth<300){Write("test-heal","IncreaseHealth 500; max unchanged");player.IncreaseHealth(500);}
                var selection=local.GetComponent<ModifierSelectionController>();
                if(selection!=null&&selection.Offers.Count>0&&selection.IsPresentationReady&&!selection.IsRequestPending&&Time.realtimeSinceStartup>=nextSelection)
                {Write("test-card","index=0 level="+selection.EarnedLevel+" offer="+selection.Offers[0].OfferId);selection.Select(0);nextSelection=Time.realtimeSinceStartup+.15f;}
            }
            var enemies=FindObjectsByType<NetworkEnemySimulationAgent>(FindObjectsSortMode.None).Where(e=>e.ProductEnemyInitialized&&e.Birth.SourceEnemy=="Ghoul").ToArray();
            foreach(var e in enemies)
            {
                var a=Action(e);var attack=e.GetComponent<SequenceEnemyAttack>();var c=e.GetComponent<EnemyController>();
                string token=e.Assignment.Epoch+"/"+a.ActionId+"/"+a.StrikeIndex+"/"+a.Phase;
                bool changed=!phases.TryGetValue(e.netId,out var last)||last!=token;if(changed)phases[e.netId]=token;
                if(changed||fixture&&a.Phase==EnemyAttackPresentationPhase.Active)
                {
                    var instance=attack.SimulationAttackInstance;var poly=instance!=null?instance.damageInteraction.GetComponent<PolygonCollider2D>():null;
                    Write(changed?"phase":"active-frame",e.Authority.Role.ToString(),new Sample{id=e.netId,epoch=e.Assignment.Epoch,target=e.Assignment.AggroTargetPlayerId,
                        action=a,scheduledPhase=a.PhaseAt(EnemySimulationClock.CombatNow),position=e.transform.position,simulator=e.Authority.RunsCombatDecisions,hp=e.GetComponent<CombatantBehaviour>().CurrentHealth,
                        damage=c.stats.Damage,speed=c.stats.Speed,hit=poly!=null&&poly.isActiveAndEnabled,instance=instance!=null?instance.GetInstanceID():0,
                        statsBound=instance==null||ReferenceEquals(instance.damageInteraction.enemyStats,c.stats),
                        vertices=poly==null?Array.Empty<Vector2>():poly.GetPath(0).Select(p=>(Vector2)poly.transform.TransformPoint(p+poly.offset)).ToArray()});
                    if(fixture&&changed&&a.Phase==EnemyAttackPresentationPhase.Active)Capture("q"+Math.Min(3,(int)(Elapsed/20))+"-strike"+a.StrikeIndex);
                }
            }
            foreach(int at in fixture?new[]{10,30,50,70,100,140,180,210}:new[]{450,480,510,541,555,580,599})if(Elapsed>=at)Capture("time"+at);
            if(fixture&&!BootGameplayNetworkManager.CombatHasEnded)Drive(local,player,enemies);
            if(BootGameplayNetworkManager.CombatHasEnded){ReleaseClock();RestoreFixtureKnockback();Capture("end");}
        }
        private void Drive(NetworkIdentity local,PlayerMovement player,NetworkEnemySimulationAgent[] enemies)
        {
            if(!positioned){anchor=GameplayMapContext.Active!=null?GameplayMapContext.Active.FindSpawn(new Vector2(12,8),5.5f):(Vector2)local.transform.position;positioned=true;}
            bool remote=LimboReferenceLaunch.Argument("--limbo-fixture-target=")=="client";
            bool target=remote?!NetworkServer.active:NetworkServer.active;
            player.StopMovement();var build=local.GetComponent<PlayerBuildRuntime>();
            build?.SetWeaponExecutionEnabled(Case=="interrupt"?Elapsed>=5:Elapsed>=185);
            if(Case=="interrupt")
            {
                if(Elapsed<25&&fixtureKnockback==null&&build?.InitialWeapon?.KnockbackSettings!=null)
                {
                    fixtureKnockback=build.InitialWeapon.KnockbackSettings;originalDistance=fixtureKnockback.distance;originalStagger=fixtureKnockback.staggerTime;
                    fixtureKnockback.distance=fixtureKnockback.staggerTime=0;
                    Write("test-no-knockback","Temporary in-memory weapon presentation: distance/stagger 0; real weapon hits and unchanged enemy Stats.");
                }
                if(Elapsed>=25&&fixtureKnockback!=null){RestoreFixtureKnockback();Write("test-knockback-restored","Original weapon knockback/stagger restored.");}
            }
            bool twoSources=Case=="interrupt"&&Elapsed>=40;
            if(twoSources&&boundaries.Add("two-sources"))Write("test-two-sources","Both players placed in ordinary weapon reach; admitted hits still use GAS/Mirror.");
            int q=Math.Min(3,(int)(Elapsed/20));Vector2[] offsets={new(.9f,.6f),new(-.9f,.6f),new(-.9f,-.6f),new(.9f,-.6f)};
            var at=anchor+(target ? Vector2.zero : Vector2.left*(twoSources ? .7f : 12));
            if(Case=="main"&&target&&Elapsed<80)
            {
                var e=enemies.FirstOrDefault(e=>e.IsCanonicalAlive);
                if(e!=null)
                {
                    // Test-only placement keeps the requested bearing for several complete combos.
                    // One initial teleport otherwise lets chase movement collapse all later bearings.
                    at=(Vector2)e.transform.position-offsets[q];
                    if(boundaries.Add("bearing-"+q))Write("test-bearing","Fixed relative target bearing, quadrant="+q+"; fixture assistance, not ordinary input.");
                }
            }
            if(Case=="boundary"&&target)
            {
                var e=enemies.FirstOrDefault(e=>e.IsCanonicalAlive);
                if(e!=null)
                {
                    var a=Action(e);var instance=e.GetComponent<SequenceEnemyAttack>().SimulationAttackInstance;
                    if(instance!=null&&EnemySequenceTimeline.HasPose(a)&&EnemySimulationClock.CombatNow>a.WarningStartedAt+.04&&
                        (a.Phase==EnemyAttackPresentationPhase.Warning||a.Phase==EnemyAttackPresentationPhase.Active))
                    {
                        var poly=instance.damageInteraction.GetComponent<PolygonCollider2D>();
                        Vector2 center=Vector2.zero;foreach(var p in poly.points)center+=(Vector2)poly.transform.TransformPoint(p+poly.offset);center/=poly.points.Length;
                        bool inside=((a.ActionId+(ulong)a.StrikeIndex)&1)==0;
                        at=center+(inside?Vector2.zero:Vector2.right*8);
                        string key=e.netId+"/"+a.ActionId+"/"+a.StrikeIndex;
                        if(boundaries.Add(key))Write("boundary-placement",inside?"inside":"outside",new HitRow{id=e.netId,player=local.netId,position=at,action=a});
                    }
                }
            }
            local.transform.position=at;var rb=local.GetComponent<Rigidbody2D>();rb.position=at;rb.linearVelocity=Vector2.zero;
            // Population fixtures exercise the real spawn path without an immediate reset
            // racing the endpoints' first birth observation.
            if(Case!="main"&&Case!="boundary"&&Case!="interrupt")return;
            if(!NetworkServer.active)return;
            var world=NetworkEnemySimulationWorld.Instance;
            var other=NetworkServer.connections.Values.Select(c=>c.identity).FirstOrDefault(i=>i!=null&&i.netId!=local.netId);
            var targetId=remote?other:local;if(targetId==null)return;
            foreach(var e in enemies.Where(e=>e.IsCanonicalAlive))
            {
                if(Elapsed>=1.5&&(!placed.TryGetValue(e.netId,out int last)||last!=q))
                {
                    if(e.Assignment.AggroTargetPlayerId!=targetId.netId){if(requested.Add(e.netId))Write("fixture-target",world.RequestTargetChange(e.netId,targetId.netId,EnemyTargetChangeReason.Forced).ToString());continue;}
                    if(e.AppliedHandoffEpoch!=e.Assignment.Epoch)continue;
                    placed[e.netId]=q;world.RepositionReferenceEnemy(e,anchor+offsets[q]);Write("fixture-placement","q="+q,e.Birth);
                }
                var action=Action(e);
                if(other!=null&&handoff<7&&Elapsed>=82+handoff*11)
                {
                    var wanted=handoff==6?EnemyAttackPresentationPhase.Recovery:handoff%2==0?EnemyAttackPresentationPhase.Warning:EnemyAttackPresentationPhase.Active;
                    if(action.Phase==wanted&&(handoff==6||action.StrikeIndex==handoff/2))
                    {var next=e.Assignment.AggroTargetPlayerId==local.netId?other:local;Write("test-handoff",world.RequestTargetChange(e.netId,next.netId,EnemyTargetChangeReason.Forced).ToString(),action);handoff++;}
                }
                if(!pauseDone&&!paused&&Elapsed>=162&&action.Phase==EnemyAttackPresentationPhase.Active)
                {PauseManager.Instance.PauseGame();paused=true;resumeAt=Time.realtimeSinceStartup+2;Write("test-pause","Active",action);}
            }
            if(paused&&Time.realtimeSinceStartup>=resumeAt){PauseManager.Instance.ResumeGame();paused=false;pauseDone=true;Write("test-resume","");}
            if(Elapsed>=170&&Elapsed<179&&slow==0){slow=PauseManager.Instance.StartSlowMo(true,.25f);Write("test-slow","Existing 0.25 slow-motion lease");}
            if(Elapsed>=179&&slow!=0){PauseManager.Instance.StopSlowMo(true,slow);slow=0;}
        }
        private void Capture(string tag){if(!motionPreview&&pictures.Add(tag))ScreenCapture.CaptureScreenshot(Path.Combine(LimboReferenceLaunch.OutputDirectory,"ghoul-"+run+"-"+tag+".png"));}
        private void ReleaseClock(){if(PauseManager.Instance!=null){if(paused)PauseManager.Instance.ResumeGame();if(slow!=0)PauseManager.Instance.StopSlowMo(true,slow);}paused=false;slow=0;}
        private void RestoreFixtureKnockback(){if(fixtureKnockback!=null){fixtureKnockback.distance=originalDistance;fixtureKnockback.staggerTime=originalStagger;fixtureKnockback=null;}}
        private void OnDestroy(){ReleaseClock();RestoreFixtureKnockback();PlayerDamageInteraction.DamageAttempted-=Hit;log?.Dispose();}
        [Serializable] private class Row{public string kind,detail,run,payload;public double elapsed,combat,realtime;}
        [Serializable] private class HitRow{public uint id,player;public int amount;public Vector2 position;public EnemyActionState action;}
        [Serializable] private class Cleanup{public int areas,warnings;}
        [Serializable] private class Sample{public uint id,epoch,target;public int hp,damage,instance;public bool simulator,hit,statsBound;public float speed;public EnemyActionState action;public EnemyAttackPresentationPhase scheduledPhase;public Vector2 position;public Vector2[] vertices;}
    }
}
