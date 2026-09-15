using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Interactions;
using AstralShift.HellMaiden.Player;
using AstralShift.Managers;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    // Explicit test inputs live here only. The ordinary lostsoul profile is passive.
    public sealed class LimboLostSoulObservation : MonoBehaviour
    {
        private StreamWriter log;
        private string run;
        private bool fixture, technical, positioned, paused, pauseDone, cleanup, resetDone;
        private float nextSelection, resumeAt, nextSample;
        private int handoff;
        private uint slow;
        private Vector2 anchor;
        private readonly HashSet<uint> placed=new();
        private readonly HashSet<uint> targetRequested=new();
        private readonly HashSet<uint> boundaryRecorded=new();
        private readonly Dictionary<uint,string> phases=new();
        private readonly HashSet<string> pictures=new();
        private double Elapsed=>NetworkCombatWorld.Instance!=null?NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot.Elapsed:0;
        public static string Case=>LimboReferenceLaunch.Argument("--limbo-lostsoul-case=")??"main";
        public static string FixtureRulesName=>Case switch{"burst"=>"LostSoulBurst","limited"=>"LostSoulLimited","boundary"=>"LostSoulBoundary","reuse"=>"LostSoulReuse","expiry"=>"LostSoulExpiry","barrier"=>"LostSoulBarrier",_=>"LostSoulFixture"+(LimboReferenceLaunch.Argument("--limbo-lostsoul-variant=")??"0")};
        private static EnemyActionState Action(NetworkEnemySimulationAgent e)=>e.Authority.RunsCombatDecisions
            ?e.CaptureCurrentCheckpoint().Movement.Runtime.Action:e.LatestAttackPresentation.Checkpoint.Movement.Runtime.Action;
        private void Start()
        {
            fixture=LimboReferenceLaunch.Profile=="lostsoul-fixture";technical=fixture||LimboReferenceLaunch.Profile=="lostsoul-validation";
            log=new StreamWriter(Path.Combine(LimboReferenceLaunch.OutputDirectory,"lostsoul-observation.jsonl")){AutoFlush=true};
            Write("mode",fixture?"ISOLATED: production reposition/target change, healing, weapons disabled before 125s, pause/slow. Fresh enemies are not enemy-pool reuse.":technical?"TECHNICAL: ordinary auto-walk, deterministic first card, healing below 300; not pressure evidence.":"PLAYABLE: passive observation.");
            PlayerDamageInteraction.DamageAttempted+=Hit;
        }
        private void Write(string kind,string detail,object data=null)=>log?.WriteLine(JsonUtility.ToJson(new Row{kind=kind,detail=detail,run=run,elapsed=Elapsed,combat=EnemySimulationClock.CombatNow,realtime=Time.realtimeSinceStartupAsDouble,payload=data==null?null:JsonUtility.ToJson(data)}));
        private void Hit(PlayerDamageInteraction source,PlayerMovement player,int amount)
        {
            var e=source.GetComponentInParent<NetworkEnemySimulationAgent>();
            if(e!=null&&e.Birth.SourceEnemy=="LostSoul")Write("damage-attempt",source.damageType.ToString(),new HitRow{id=e.netId,player=player.GetComponent<NetworkIdentity>().netId,position=player.transform.position,amount=amount,action=Action(e)});
        }
        private void Update()
        {
            if(NetworkCombatWorld.Instance==null||NetworkClient.localPlayer==null)
            {
                if(run!=null&&!cleanup)
                {cleanup=true;Write("cleanup","No active world",new Cleanup{areas=FindObjectsByType<PlayerDamageInteraction>(FindObjectsSortMode.None).Count(x=>x.isActiveAndEnabled),warnings=FindObjectsByType<EnemyAttackWarning>(FindObjectsSortMode.None).Length,explosions=FindObjectsByType<EnemyExplosionAttackVFX>(FindObjectsSortMode.None).Length});}
                return;
            }
            var progress=NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot;
            if(progress.RunId!=run)
            {ReleaseClock();run=progress.RunId;placed.Clear();targetRequested.Clear();boundaryRecorded.Clear();phases.Clear();pictures.Clear();handoff=0;positioned=pauseDone=cleanup=resetDone=false;Write("round",run);}
            var local=NetworkClient.localPlayer;var player=local.GetComponent<PlayerMovement>();
            if(technical&&!BootGameplayNetworkManager.CombatHasEnded)
            {
                if(local.GetComponent<CombatantBehaviour>().CurrentHealth<300){Write("test-heal","IncreaseHealth 500; max unchanged");player.IncreaseHealth(500);}
                var selection=local.GetComponent<ModifierSelectionController>();
                if(selection!=null&&selection.Offers.Count>0&&selection.IsPresentationReady&&!selection.IsRequestPending&&Time.realtimeSinceStartup>=nextSelection)
                {Write("test-card","index=0 level="+selection.EarnedLevel+" offer="+selection.Offers[0].OfferId);selection.Select(0);nextSelection=Time.realtimeSinceStartup+.15f;}
            }
            var enemies=FindObjectsByType<NetworkEnemySimulationAgent>(FindObjectsSortMode.None).Where(e=>e.ProductEnemyInitialized&&e.Birth.SourceEnemy=="LostSoul").ToArray();
            bool sample=fixture&&Time.realtimeSinceStartup>=nextSample;if(sample)nextSample=Time.realtimeSinceStartup+.05f;
            foreach(var e in enemies)
            {
                var a=Action(e);var attack=e.GetComponent<EnemyAttackExplosion>();var c=e.GetComponent<EnemyController>();
                string token=e.Assignment.Epoch+"/"+a.ActionId+"/"+a.Phase;
                bool changed=!phases.TryGetValue(e.netId,out var last)||last!=token;
                if(changed)phases[e.netId]=token;
                if(changed||sample||fixture&&a.PhaseAt(EnemySimulationClock.CombatNow)==EnemyAttackPresentationPhase.Active)
                {
                    var vfx=attack.ExplosionInstance;var poly=vfx!=null?vfx.colliders.GetComponent<PolygonCollider2D>():null;
                    Write(changed?"phase":a.PhaseAt(EnemySimulationClock.CombatNow)==EnemyAttackPresentationPhase.Active?"active-frame":"sample",e.Authority.Role.ToString(),new Sample{id=e.netId,epoch=e.Assignment.Epoch,variant=e.Birth.Variant,reset=e.ReferenceResetVersion,
                        target=e.Assignment.AggroTargetPlayerId,simulator=e.Authority.RunsCombatDecisions,action=a,position=e.transform.position,speed=c.stats.Speed,speedMultiplier=c.stats.SpeedMultiplier,
                        hp=e.GetComponent<CombatantBehaviour>().CurrentHealth,damage=c.stats.Damage,hurt=c.hurtBox.GetComponentsInChildren<Collider2D>(true).Any(x=>x.isActiveAndEnabled),
                        hit=poly!=null&&poly.isActiveAndEnabled,statsBound=vfx==null||ReferenceEquals(vfx.damageInteraction.enemyStats,c.stats),
                        warningInstance=attack.WarningInstance!=null?attack.WarningInstance.GetInstanceID():0,explosionInstance=vfx!=null?vfx.GetInstanceID():0,
                        vertices=poly==null?Array.Empty<Vector2>():poly.GetPath(0).Select(p=>(Vector2)poly.transform.TransformPoint(p+poly.offset)).ToArray()});
                }
                if(fixture&&changed&&a.Phase==EnemyAttackPresentationPhase.Active)Capture("q"+Math.Min(3,(int)(Elapsed/20))+"-active");
            }
            foreach(int at in fixture?new[]{10,30,50,70,90,110,130,154}:new[]{390,405,420,435,449})if(Elapsed>=at)Capture("time"+at);
            if(fixture&&!BootGameplayNetworkManager.CombatHasEnded)Drive(local,player,enemies);
            if(BootGameplayNetworkManager.CombatHasEnded)ReleaseClock();
        }
        private void Drive(NetworkIdentity local,PlayerMovement player,NetworkEnemySimulationAgent[] enemies)
        {
            if(!positioned){anchor=GameplayMapContext.Active!=null?GameplayMapContext.Active.FindSpawn(new Vector2(12,8),5.5f):(Vector2)local.transform.position;positioned=true;}
            bool targetRemote=LimboReferenceLaunch.Argument("--limbo-fixture-target=")=="client";
            bool target=targetRemote?!NetworkServer.active:NetworkServer.active;
            player.StopMovement();
            local.GetComponent<PlayerBuildRuntime>()?.SetWeaponExecutionEnabled(Elapsed>=125);
            Vector2 at=anchor+(target?Vector2.zero:Vector2.left*12);
            if(Case=="boundary"&&target)
            {
                var e=enemies.FirstOrDefault(x=>x.IsCanonicalAlive);
                if(e!=null)
                {
                    var a=Action(e);var phase=a.PhaseAt(EnemySimulationClock.CombatNow);
                    // Keep the escape pose through the frame between the nominal
                    // deadline and AttackEnter; returning to anchor there creates
                    // a real collision before the "outside" pose is sampled.
                    if(a.ActionId!=0&&!a.ExplosionTriggered&&a.Phase!=EnemyAttackPresentationPhase.Inactive&&a.Phase!=EnemyAttackPresentationPhase.Cancelled&&EnemySimulationClock.CombatNow-a.WarningStartedAt>.65)at=anchor+Vector2.right*10;
                    if(a.ExplosionTriggered&&(phase==EnemyAttackPresentationPhase.Active||phase==EnemyAttackPresentationPhase.Recovery))
                    {
                        bool inside=e.netId%2==0;at=a.ExplosionPosition+(inside?Vector2.down:Vector2.right*5);
                        if(boundaryRecorded.Add(e.netId)){Write("boundary-placement",inside?"inside":"outside",new Boundary{id=e.netId,player=local.netId,position=at,center=a.ExplosionPosition});Capture("boundary-"+(inside?"inside":"outside"));}
                    }
                }
            }
            local.transform.position=at;var rb=local.GetComponent<Rigidbody2D>();rb.position=at;rb.linearVelocity=Vector2.zero;
            if(!NetworkServer.active)return;
            var world=NetworkEnemySimulationWorld.Instance;
            var other=NetworkServer.connections.Values.Select(c=>c.identity).FirstOrDefault(i=>i!=null&&i.netId!=local.netId);
            var targetId=targetRemote?other:local;if(targetId==null)return;
            int q=Math.Min(3,(int)(Elapsed/20));Vector2[] offsets={new(.9f,.6f),new(-.9f,.6f),new(-.9f,-.6f),new(.9f,-.6f)};
            foreach(var e in enemies.Where(e=>e.IsCanonicalAlive))
            {
                if(Elapsed>=1.5&&!placed.Contains(e.netId))
                {
                    // A target request may be queued. Reposition only after its assignment
                    // has applied; otherwise reposition captures the previous target.
                    if(e.Assignment.AggroTargetPlayerId!=targetId.netId)
                    {
                        if(targetRequested.Add(e.netId))Write("fixture-target",world.RequestTargetChange(e.netId,targetId.netId,EnemyTargetChangeReason.Forced).ToString());
                        continue;
                    }
                    if(e.AppliedHandoffEpoch!=e.Assignment.Epoch)continue;
                    placed.Add(e.netId);
                    if(Case!="burst")world.RepositionReferenceEnemy(e,anchor+(Case=="expiry"?Vector2.right*42:offsets[q]+(Elapsed>=125?Vector2.right*3:Vector2.zero)));
                    Write("fixture-placement","quadrant="+q+" case="+Case,e.Birth);
                }
                if(Case=="expiry"){e.GetComponent<EnemyController>().Movement.StopMovement();continue;}
                var checkpoint=e.CaptureCurrentCheckpoint().Movement;
                if(!resetDone&&Case=="main"&&Elapsed>=125&&e.GetComponent<CombatantBehaviour>().CurrentHealth<e.Birth.Health)
                {Write("test-reset","Same enemy after real weapon damage; production reposition/reset",new ResetSample{id=e.netId,hp=e.GetComponent<CombatantBehaviour>().CurrentHealth,version=e.ReferenceResetVersion});world.RepositionReferenceEnemy(e,e.transform.position);resetDone=true;}
                if(!e.Authority.RunsCombatDecisions)world.Registry.TryGetLatestSnapshot(e.netId,out checkpoint);
                var phase=checkpoint.Runtime.Action.PhaseAt(EnemySimulationClock.CombatNow);
                if(other!=null&&handoff<3&&Elapsed>=82+handoff*7)
                {
                    var wanted=handoff==0?EnemyAttackPresentationPhase.Warning:handoff==1?EnemyAttackPresentationPhase.Active:EnemyAttackPresentationPhase.Recovery;
                    if(phase==wanted){var next=e.Assignment.AggroTargetPlayerId==local.netId?other:local;Write("test-handoff",wanted+"/"+world.RequestTargetChange(e.netId,next.netId,EnemyTargetChangeReason.Forced),checkpoint);handoff++;}
                }
                if(!paused&&!pauseDone&&Elapsed>=107&&phase==EnemyAttackPresentationPhase.Active)
                {PauseManager.Instance.PauseGame();paused=true;resumeAt=Time.realtimeSinceStartup+2;Write("test-pause","Active");}
            }
            if(paused&&Time.realtimeSinceStartup>=resumeAt){PauseManager.Instance.ResumeGame();paused=false;pauseDone=true;Write("test-resume","");}
            if(Elapsed>=114&&Elapsed<120&&slow==0){slow=PauseManager.Instance.StartSlowMo(true,.25f);Write("test-slow","Existing slow-motion lease 0.25");}
            if(Elapsed>=120&&slow!=0){PauseManager.Instance.StopSlowMo(true,slow);slow=0;}
        }
        private void Capture(string tag){if(pictures.Add(tag))ScreenCapture.CaptureScreenshot(Path.Combine(LimboReferenceLaunch.OutputDirectory,"lostsoul-"+run+"-"+tag+".png"));}
        private void ReleaseClock(){if(PauseManager.Instance!=null){if(paused)PauseManager.Instance.ResumeGame();if(slow!=0)PauseManager.Instance.StopSlowMo(true,slow);}paused=false;slow=0;}
        private void OnDestroy(){ReleaseClock();PlayerDamageInteraction.DamageAttempted-=Hit;log?.Dispose();}
        [Serializable] private class Row{public string kind,detail,run,payload;public double elapsed,combat,realtime;}
        [Serializable] private class HitRow{public uint id,player;public int amount;public Vector2 position;public EnemyActionState action;}
        [Serializable] private class Cleanup{public int areas,warnings,explosions;}
        [Serializable] private class Boundary{public uint id,player;public Vector2 position,center;}
        [Serializable] private class ResetSample{public uint id,version;public int hp;}
        [Serializable] private class Sample{public uint id,epoch,reset,target;public int variant,hp,damage,warningInstance,explosionInstance;public bool simulator,hurt,hit,statsBound;public float speed,speedMultiplier;public EnemyActionState action;public Vector2 position;public Vector2[] vertices;}
    }
}
