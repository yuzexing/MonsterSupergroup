using System.Collections;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using DamageType = AstralShift.HellMaiden.Player.Attacks.DamageType;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.Gameplay.Combat;
using MonsterSupergroup.GAS;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed partial class EnemyPrefabVariantPlayModeTests
    {
        private EnemyController SpawnLostSoul()
        {
            var rules=Resources.Load<GameplayWaveRules>("LimboReference/LostSoulFixture0");
            Assert.That(rules.TryCapture(out var plan,out var error),Is.True,error);
            var root=Object.Instantiate(plan.Prefabs[plan.Reference.Clips[0].PrefabIndex],Owner.transform.position+new Vector3(15,10,0),Quaternion.identity);
            var agent=root.GetComponent<NetworkEnemySimulationAgent>();
            agent.ConfigureBirth(new EnemyBirthParameters{Enabled=true,SourceEnemy="LostSoul",Health=150,Damage=50,Speed=3.6f,SpeedMultiplier=1,Wind=1,Knockback=1,Xp=7,Counted=true,ResetOnReposition=true});
            agent.ConfigureInitialServerTarget(Owner.netId);NetworkServer.Spawn(root);
            return root.GetComponent<EnemyController>();
        }
        private static EnemyActionState LostSoulState(EnemyController enemy,double age)
        {
            var epoch=enemy.GetComponent<NetworkEnemySimulationAgent>().Assignment.Epoch;double start=EnemySimulationClock.CombatNow-age;
            return new EnemyActionState{ActionId=((ulong)epoch<<32)|7,Phase=EnemyAttackPresentationPhase.Recovery,
                Explosion=true,ExplosionTriggered=true,SelfDestructPending=true,ExplosionPosition=enemy.transform.position+new Vector3(0,1.74000025f),
                WarningStartedAt=start,WarningUntil=start+1.0333333,ActiveUntil=start+1.179999961,RecoveryUntil=start+1.933333291,NextAttackAt=start+2.933333291,Facing=Vector2.right};
        }
        [UnityTest] public IEnumerator LostSoulExpiredHandoffDisposesOnceWithoutConfirmedKill()
        {
            var enemy=SpawnLostSoul();yield return WaitFor(()=>Ready(enemy));
            int kills=0;enemy.OnConfirmedKill+=_=>kills++;
            enemy.RestoreSimulationAction(LostSoulState(enemy,3),EnemySimulationClock.CombatNow);
            Assert.That(enemy.GetComponent<EnemyAttackExplosion>().ExplosionInstance,Is.Null);
            var action=enemy.CaptureSimulationAction(EnemySimulationClock.CombatNow);
            enemy.RunUpdate();enemy.RunLateUpdate();
            Assert.That(enemy.CaptureSimulationAction(EnemySimulationClock.CombatNow).ActionId,Is.EqualTo(action.ActionId),"Expiry cannot enter Moving and start a new attack before server disposal.");
            yield return WaitFor(()=>enemy==null);Assert.That(kills,Is.Zero);
        }
        [UnityTest] public IEnumerator LostSoulCanonicalDeathDuringRecoveryCancelsParticlesAndPendingDisposal()
        {
            var enemy=SpawnLostSoul();yield return WaitFor(()=>Ready(enemy));
            int kills=0;enemy.OnConfirmedKill+=_=>kills++;
            var state=LostSoulState(enemy,1.3);enemy.RestoreSimulationAction(state,EnemySimulationClock.CombatNow);
            var attack=enemy.GetComponent<EnemyAttackExplosion>();Assert.That(attack.ExplosionInstance,Is.Not.Null);
            // A canonical damage event also covers damage that bypasses the disabled hurtbox (e.g. an existing status).
            var execution=new LegacyCombatExecution(Owner.GetComponent<CombatRuntimeServiceProvider>().Services);
            enemy.Damage(150,DamageType.Normal,new LegacyDamageSource(execution,execution.BeginAttack(1,CombatTags.Attack),1));
            yield return null; yield return null;
            Assert.That(kills,Is.EqualTo(1));Assert.That(attack.ExplosionInstance,Is.Null);
            Assert.That(enemy.CaptureSimulationAction(EnemySimulationClock.CombatNow).SelfDestructPending,Is.False);
            // Death animation/reclamation is exercised by the rendered weapon probe.
            // This test isolates cancellation after canonical death, including while
            // the existing upgrade UI can pause the death animation.
        }
    }
}
