using System.Linq;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat
{
    public sealed partial class LimboDashObservation
    {
        private ulong boundaryAction;
        private int boundaryIndex;
        private Vector2 boundaryPosition;
        private bool boundaryMoved, boundaryArmed;
        private float minimumGap=float.PositiveInfinity;

        private void DriveBoundary(NetworkIdentity local,PlayerMovement player,NetworkEnemySimulationAgent enemy)
        {
            if(!positioned){anchor=GameplayMapContext.Active.FindSpawn(new Vector2(12,8),5.5f);positioned=true;boundaryIndex=0;boundaryAction=0;boundaryArmed=false;}
            player.StopMovement();player.PlayerStats.currentStats.xpModifier=0;local.GetComponent<PlayerBuildRuntime>().SetWeaponExecutionEnabled(false);
            bool remoteTarget=LimboReferenceLaunch.Argument("--limbo-fixture-target=")=="client";
            bool targetLocal=remoteTarget?!NetworkServer.active:NetworkServer.active;
            Vector2 at=targetLocal?anchor:anchor+Vector2.left*12;
            if(enemy!=null)
            {
                var world=NetworkEnemySimulationWorld.Instance;
                if(NetworkServer.active&&!assigned&&Elapsed>=2)
                {
                    var remote=NetworkServer.connections.Values.Select(c=>c.identity).FirstOrDefault(i=>i!=null&&i.netId!=local.netId);
                    if(!remoteTarget||remote!=null){world.RequestTargetChange(enemy.netId,remoteTarget?remote.netId:local.netId,EnemyTargetChangeReason.Forced);assigned=true;}
                }
                var action=Action(enemy);var phase=action.PhaseAt(EnemySimulationClock.CombatNow);
                // The first lease may still carry an attack locked toward the previous target. Let it finish before measurement.
                if(targetLocal&&!boundaryArmed&&enemy.Authority.RunsCombatDecisions&&enemy.Assignment.AggroTargetPlayerId==local.netId&&phase==EnemyAttackPresentationPhase.Inactive&&Elapsed>=3)
                {boundaryArmed=true;Write("boundary-ready","Target lease acknowledged; previous-target attack finished.");}
                if(targetLocal&&boundaryArmed)
                {
                    var hitbox=player.GetComponentsInChildren<PlayerHitbox>().Select(h=>h.GetComponent<CircleCollider2D>()).First(c=>c!=null&&c.enabled);
                    if(action.ActionId!=boundaryAction && phase==EnemyAttackPresentationPhase.Warning)
                    {
                        if(boundaryAction!=0) Write("boundary-result",(boundaryIndex%2==1?"outside":"inside")+"; minimum collider separation",new Gap{gap=minimumGap,action=boundaryAction});
                        boundaryAction=action.ActionId;boundaryIndex++;boundaryMoved=false;minimumGap=float.PositiveInfinity;
                    }
                    if(phase==EnemyAttackPresentationPhase.Warning&&!boundaryMoved&&EnemySimulationClock.CombatNow>=action.WarningStartedAt+.15)
                    {
                        // Use the actual locked direction: a remote target's last pose can make it slightly non-horizontal.
                        var dash=enemy.GetComponent<EnemyAttackDash>();var circle=(CircleCollider2D)dash.attackCollider;
                        float separation=GameplayMapContext.Radius(circle)+GameplayMapContext.Radius(hitbox)+(boundaryIndex%2==1?.10f:-.10f);
                        Vector2 hitOffset=(Vector2)hitbox.bounds.center-(Vector2)player.transform.position;
                        Vector2 dashOffset=(Vector2)circle.transform.TransformPoint(circle.offset)-(Vector2)enemy.transform.position;
                        Vector2 direction=(action.DashEnd-action.DashStart).normalized;
                        Vector2 normal=new Vector2(-direction.y,direction.x);
                        boundaryPosition=action.DashStart+direction*2.5f+dashOffset+normal*separation-hitOffset;
                        boundaryMoved=true;Write("boundary-position",boundaryIndex%2==1?"outside":"inside",action);
                    }
                    if(boundaryMoved&&(phase==EnemyAttackPresentationPhase.Warning||phase==EnemyAttackPresentationPhase.Active))at=boundaryPosition;
                    var area=enemy.GetComponent<EnemyAttackDash>().attackCollider;
                    if(phase==EnemyAttackPresentationPhase.Active&&area.enabled)
                    {
                        var distance=area.Distance(hitbox);minimumGap=Mathf.Min(minimumGap,distance.distance);
                        Write("boundary-gap",boundaryIndex%2==1?"outside":"inside",new Gap{gap=distance.distance,action=action.ActionId});
                    }
                }
                if(enemy.Authority.RunsCombatDecisions&&phase==EnemyAttackPresentationPhase.Inactive&&Elapsed>=2&&NetworkClient.spawned.TryGetValue(enemy.Assignment.AggroTargetPlayerId,out var target))
                {
                    Vector2 origin=(Vector2)target.transform.position+Vector2.left*2.5f;
                    var body=enemy.GetComponent<Rigidbody2D>();body.position=origin;body.linearVelocity=Vector2.zero;enemy.transform.position=origin;
                }
            }
            local.transform.position=at;var rb=local.GetComponent<Rigidbody2D>();rb.position=at;rb.linearVelocity=Vector2.zero;
        }
        [System.Serializable] private class Gap{public float gap;public ulong action;}
    }
}
