using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using AstralShift.HellMaiden.Interactions;
using AstralShift.HellMaiden.Player;
using AstralShift.Managers;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    // Only the explicit fixture/validation profiles provide inputs. The playable profile is passive.
    public sealed partial class LimboDashObservation : MonoBehaviour
    {
        private StreamWriter log;
        private string run;
        private bool fixture, technical, positioned, resetDone, paused, pauseDone, assigned;
        private float resumeAt, nextSelection, nextSample;
        private uint slowMotion;
        private uint fixtureEnemyId;
        private int handoff, quadrant = -1;
        private Vector2 anchor;
        private readonly Dictionary<uint,string> phases = new();
        private readonly HashSet<string> pictures = new();
        private double Elapsed => NetworkCombatWorld.Instance != null ? NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot.Elapsed : 0;
        private void Start()
        {
            fixture = LimboReferenceLaunch.Profile == "dash-fixture";
            technical = fixture || LimboReferenceLaunch.Profile == "dash-validation";
            log = new StreamWriter(Path.Combine(LimboReferenceLaunch.OutputDirectory,"dash-observation.jsonl")){AutoFlush=true};
            Write("mode",fixture ? "ISOLATED: positioning, healing, XP pickup disabled, reset, pause, slow motion, handoffs; normal weapon only after 120s."
                : technical ? "TECHNICAL: healing below 300, ordinary auto-walk input and deterministic first offered card; not pressure evidence." : "PLAYABLE: passive observer; no fixture inputs.");
            if(LimboReferenceLaunch.Argument("--limbo-dash-case=")=="reuse")Write("mode-reuse","Same scene: v0 born at 1, normal weapons enabled 20–35; v1 born at 40, normal weapons enabled after 60. No forced death.");
            PlayerDamageInteraction.DamageAttempted += Hit;
        }
        private void Write(string kind,string detail,object data=null) => log?.WriteLine(JsonUtility.ToJson(new Row{kind=kind,detail=detail,run=run,
            combat=EnemySimulationClock.CombatNow,elapsed=Elapsed,realtime=Time.realtimeSinceStartupAsDouble,payload=data==null?null:JsonUtility.ToJson(data)}));
        private static EnemyActionState Action(NetworkEnemySimulationAgent e) => e.Authority.RunsCombatDecisions
            ? e.CaptureCurrentCheckpoint().Movement.Runtime.Action : e.LatestAttackPresentation.Checkpoint.Movement.Runtime.Action;
        private void Hit(PlayerDamageInteraction source,PlayerMovement player,int amount)
        {
            var e=source.GetComponentInParent<NetworkEnemySimulationAgent>();
            if(e!=null && e.Birth.SourceEnemy=="Brotchi_Dash") Write("damage-attempt",source.damageType.ToString(),new HitRow{id=e.netId,player=player.GetComponent<NetworkIdentity>().netId,
                damage=amount,epoch=e.Assignment.Epoch,action=Action(e),statsBound=ReferenceEquals(source.enemyStats,e.GetComponent<EnemyController>().stats)});
        }
        private void Update()
        {
            if(NetworkCombatWorld.Instance==null || NetworkClient.localPlayer==null) return;
            var progress=NetworkCombatWorld.Instance.GetComponent<NetworkWaveProgress>().Snapshot;
            if(progress.RunId!=run)
            {
                ReleaseClock();run=progress.RunId;phases.Clear();pictures.Clear();positioned=resetDone=pauseDone=assigned=false;handoff=0;quadrant=-1;fixtureEnemyId=0;
                Write("round",run);
            }
            var local=NetworkClient.localPlayer;var player=local.GetComponent<PlayerMovement>();
            if(technical && !BootGameplayNetworkManager.CombatHasEnded)
            {
                if(local.GetComponent<CombatantBehaviour>().CurrentHealth<300)
                { Write("test-heal","IncreaseHealth(500), max remains 500");player.IncreaseHealth(500); }
                var selection=local.GetComponent<ModifierSelectionController>();
                if(selection!=null && selection.Offers.Count>0 && selection.IsPresentationReady && !selection.IsRequestPending && Time.realtimeSinceStartup>=nextSelection)
                {
                    Write("test-card","index=0, level="+selection.EarnedLevel+", offer="+selection.Offers[0].OfferId);
                    selection.Select(0);nextSelection=Time.realtimeSinceStartup+.15f;
                }
            }
            var enemies=FindObjectsByType<NetworkEnemySimulationAgent>(FindObjectsSortMode.None)
                .Where(e=>e.ProductEnemyInitialized&&e.Birth.Enabled&&e.Birth.SourceEnemy=="Brotchi_Dash").ToArray();
            bool sample=Time.realtimeSinceStartup>=nextSample;if(sample)nextSample=Time.realtimeSinceStartup+.05f;
            foreach(var e in enemies)
            {
                var a=Action(e);var dash=e.GetComponent<EnemyAttackDash>();var body=e.GetComponent<Rigidbody2D>();
                string key=e.Assignment.Epoch+"/"+a.ActionId+"/"+a.Phase;
                bool changed=!phases.TryGetValue(e.netId,out var previous)||previous!=key;
                if(changed)phases[e.netId]=key;
                if(changed || sample && fixture || a.PhaseAt(EnemySimulationClock.CombatNow)==EnemyAttackPresentationPhase.Active)
                {
                    var instance=dash.SimulationAttackInstance ?? e.GetComponent<NetworkEnemyMeleeReplica>()?.ReplicaAttackInstance;
                    var circle=dash.attackCollider as CircleCollider2D;
                    Write(changed?"phase":"sample",e.Authority.Role.ToString(),new Sample{id=e.netId,epoch=e.Assignment.Epoch,reset=e.ReferenceResetVersion,
                        simulator=e.Authority.RunsCombatDecisions,target=e.Assignment.AggroTargetPlayerId,action=a,position=body.position,velocity=body.linearVelocity,
                        constraints=(int)body.constraints,simulated=body.simulated,exclusion=e.GetComponent<EnemyController>().collider.excludeLayers,
                        hitEnabled=circle.enabled&&dash.damageInteraction.enabled,hp=e.GetComponent<CombatantBehaviour>().CurrentHealth,
                        damage=dash.damageInteraction.enemyStats?.Damage??0,statsBound=ReferenceEquals(dash.damageInteraction.enemyStats,e.GetComponent<EnemyController>().stats),
                        center=circle.transform.TransformPoint(circle.offset),radius=circle.radius,warning=instance!=null?(Vector2)instance.transform.position:default,
                        warningInstance=instance!=null?instance.GetInstanceID():0});
                }
                if(fixture && changed && a.Phase==EnemyAttackPresentationPhase.Active)
                    Capture("q"+quadrant+"-"+a.Phase);
            }
            foreach(int at in fixture?new[]{12,25,38,51,67,80,94,103,112,125,149}:new[]{210,240,286,334,370,389})
                if(Elapsed>=at)Capture("time"+at);
            if(fixture && !BootGameplayNetworkManager.CombatHasEnded)
            {
                if(LimboReferenceLaunch.Argument("--limbo-dash-case=")=="boundary")DriveBoundary(local,player,enemies.FirstOrDefault(e=>e.IsCanonicalAlive));
                else Drive(local,player,enemies.FirstOrDefault(e=>e.IsCanonicalAlive));
            }
            if(BootGameplayNetworkManager.CombatHasEnded)ReleaseClock();
        }
        private void Capture(string tag)
        {
            if(!pictures.Add(tag))return;
            ScreenCapture.CaptureScreenshot(Path.Combine(LimboReferenceLaunch.OutputDirectory,"dash-"+run+"-"+tag+".png"));
        }
        private void Drive(NetworkIdentity local,PlayerMovement player,NetworkEnemySimulationAgent enemy)
        {
            if(!positioned)
            {
                var map=GameplayMapContext.Active;
                anchor=map!=null?map.FindSpawn(new Vector2(12,8),5.5f):(Vector2)local.transform.position;
                positioned=true;Write("fixture-open-anchor",anchor.ToString()+"; 5.5 unit map clearance");
            }
            player.StopMovement();player.PlayerStats.currentStats.xpModifier=0;
            bool deathProbe=LimboReferenceLaunch.Argument("--limbo-dash-case=")=="reuse" ? Elapsed>=20&&Elapsed<35 || Elapsed>=60 : Elapsed>=120;
            local.GetComponent<PlayerBuildRuntime>()?.SetWeaponExecutionEnabled(deathProbe);
            bool remoteTarget=LimboReferenceLaunch.Argument("--limbo-fixture-target=")=="client";
            bool isTarget=remoteTarget?!NetworkServer.active:NetworkServer.active;
            Vector2 position=anchor;
            if(!isTarget && Elapsed<62)position+=Vector2.left*12;
            // During this probe the target crosses the locked line after warning has begun.
            if(isTarget && enemy!=null && Elapsed>=54 && Elapsed<61 && Action(enemy).PhaseAt(EnemySimulationClock.CombatNow)==EnemyAttackPresentationPhase.Active)
                position+=Vector2.up*3;
            local.transform.position=position;var localBody=local.GetComponent<Rigidbody2D>();localBody.position=position;localBody.linearVelocity=Vector2.zero;
            if(enemy==null)return;
            if(fixtureEnemyId!=enemy.netId){fixtureEnemyId=enemy.netId;assigned=false;}
            var world=NetworkEnemySimulationWorld.Instance;
            var state=Action(enemy);var phase=state.PhaseAt(EnemySimulationClock.CombatNow);
            if(NetworkServer.active && Elapsed>=2 && !assigned)
            {
                var remote=NetworkServer.connections.Values.Select(c=>c.identity).FirstOrDefault(i=>i!=null&&i.netId!=local.netId);
                if(remoteTarget&&remote==null)return;
                world.RequestTargetChange(enemy.netId,remoteTarget?remote.netId:local.netId,EnemyTargetChangeReason.Forced);
                assigned=true;
            }
            // Reposition only the current simulator between attacks. Active motion stays entirely production-driven.
            if(enemy.Authority.RunsCombatDecisions && phase==EnemyAttackPresentationPhase.Inactive && Elapsed>=2 && Elapsed<120 &&
                NetworkClient.spawned.TryGetValue(enemy.Assignment.AggroTargetPlayerId,out var target))
            {
                int q=Math.Min(3,Math.Max(0,(int)((Elapsed-2)/13)));
                if(q!=quadrant){quadrant=q;Write("fixture-quadrant",q.ToString());}
                Vector2[] offsets={new(2,1.4f),new(-2,1.4f),new(-2,-1.4f),new(2,-1.4f)};
                Vector2 at=(Vector2)target.transform.position+offsets[q];
                var body=enemy.GetComponent<Rigidbody2D>();body.position=at;body.linearVelocity=Vector2.zero;enemy.transform.position=at;
            }
            if(!NetworkServer.active)return;
            var other=NetworkServer.connections.Values.Select(c=>c.identity).FirstOrDefault(i=>i!=null&&i.netId!=local.netId);
            if(other!=null && handoff<3 && Elapsed>=63+handoff*8)
            {
                var p=enemy.CaptureCurrentCheckpoint().Movement;
                if(!enemy.Authority.RunsCombatDecisions)world.Registry.TryGetLatestSnapshot(enemy.netId,out p);
                var expected=handoff==0?EnemyAttackPresentationPhase.Warning:handoff==1?EnemyAttackPresentationPhase.Active:EnemyAttackPresentationPhase.Recovery;
                if(p.Runtime.Action.PhaseAt(EnemySimulationClock.CombatNow)==expected)
                {
                    var next=enemy.Assignment.AggroTargetPlayerId==local.netId?other:local;
                    Write("test-handoff",expected+"/"+world.RequestTargetChange(enemy.netId,next.netId,EnemyTargetChangeReason.Forced),p);handoff++;
                }
            }
            if(!paused&&!pauseDone&&Elapsed>=89&&phase==EnemyAttackPresentationPhase.Active)
            {PauseManager.Instance.PauseGame();paused=true;resumeAt=Time.realtimeSinceStartup+2;Write("test-pause","Active, existing PauseManager");}
            if(paused&&Time.realtimeSinceStartup>=resumeAt)
            {PauseManager.Instance.ResumeGame();paused=false;pauseDone=true;Write("test-resume","");}
            if(Elapsed>=99&&Elapsed<105&&slowMotion==0)
            {slowMotion=PauseManager.Instance.StartSlowMo(true,.25f);Write("test-slow","Existing trap slow-motion lease, .25; dedicated barrier integration also runs in preview.");}
            if(Elapsed>=105&&slowMotion!=0){PauseManager.Instance.StopSlowMo(true,slowMotion);slowMotion=0;}
            if(Elapsed>=110&&!resetDone)
            {resetDone=true;Write("test-reposition-reset","Existing lifecycle, same network enemy; not pool reuse",enemy.Birth);world.RepositionReferenceEnemy(enemy,position+Vector2.right*3);}
        }
        private void ReleaseClock()
        {
            if(PauseManager.Instance!=null){if(paused)PauseManager.Instance.ResumeGame();if(slowMotion!=0)PauseManager.Instance.StopSlowMo(true,slowMotion);}
            paused=false;slowMotion=0;
        }
        private void OnDestroy(){ReleaseClock();PlayerDamageInteraction.DamageAttempted-=Hit;log?.Dispose();}
        [Serializable] private class Row{public string kind,detail,run,payload;public double elapsed,combat,realtime;}
        [Serializable] private class HitRow{public uint id,player,epoch;public int damage;public bool statsBound;public EnemyActionState action;}
        [Serializable] private class Sample{public uint id,epoch,reset,target;public bool simulator,simulated,hitEnabled,statsBound;public int hp,damage,constraints,exclusion,warningInstance;public EnemyActionState action;public Vector2 position,velocity,center,warning;public float radius;}
    }
}
