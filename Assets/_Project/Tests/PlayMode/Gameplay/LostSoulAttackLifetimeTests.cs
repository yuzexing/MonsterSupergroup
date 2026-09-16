using System.Collections.Generic;
using System.Reflection;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Combat;
using MonsterSupergroup.NetworkCombat;
using NUnit.Framework;
using UnityEngine;

namespace MonsterSupergroup.Gameplay.Tests
{
    public sealed class LostSoulAttackLifetimeTests
    {
        private readonly List<GameObject> owned=new();
        private EnemyController controller;
        private EnemyAttackExplosion attack;
        private Rigidbody2D body;
        private GameObject Create(string name,bool active=true)
        {var go=new GameObject(name);go.SetActive(active);owned.Add(go);return go;}
        [SetUp] public void Setup()
        {
            Create("LostSoul test pool").AddComponent<PoolManager>().Init();
            var rules=Resources.Load<GameplayWaveRules>("LimboReference/LostSoulFixture0");
            Assert.That(rules.TryCapture(out var plan,out var error),Is.True,error);
            var source=plan.Prefabs[plan.Reference.Clips[0].PrefabIndex].GetComponent<EnemyController>();
            var root=Create("Same LostSoul owner",false);
            controller=root.AddComponent<EnemyController>();controller.enabled=false;
            controller.stats=new EnemyStats();SetStats(150);
            body=root.AddComponent<Rigidbody2D>();body.gravityScale=0;
            controller.collider=root.AddComponent<CircleCollider2D>();
            var hurt=Create("HurtBox");hurt.transform.SetParent(root.transform,false);hurt.AddComponent<BoxCollider2D>();
            controller.hurtBox=hurt.AddComponent<EnemyHurtbox>();controller.hurtBox.Reset();
            var movement=root.AddComponent<EnemyDefaultMovement>();movement.SetTransform(root.transform);movement.SetRigidBody(body);movement.Init(controller);
            controller._currentMovementScript=movement;
            attack=root.AddComponent<EnemyAttackExplosion>();attack.controller=controller;attack.enemyAnimator=source.enemyAnimator;controller.attackScript=attack;
            var point=Create("ExplosionPosition");point.transform.SetParent(root.transform,false);point.transform.localPosition=new Vector3(0,1.74000025f);
            typeof(EnemyAttackExplosion).GetField("explosionPosition",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(attack,point.transform);
            foreach(string field in new[]{"_attackWarningPrefab","_attackVFXPrefab"})
            {var info=typeof(EnemyAttackExplosion).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic);info.SetValue(attack,info.GetValue(source.attackScript));}
            root.SetActive(true);
        }
        private void SetStats(int hp)
        {controller.stats=new EnemyStats();controller.stats.Init(new EnemyStatsValues{Health=hp,Damage=50,Speed=3.6f,XP=7,KnockBackMultiplier=1,WindMultiplier=1});}
        private static EnemyActionState State(ulong id=1)=>new(){ActionId=id,Explosion=true,ExplosionTriggered=true,SelfDestructPending=true,
            Phase=EnemyAttackPresentationPhase.Active,WarningStartedAt=1,WarningUntil=2.0333333,ActiveUntil=2.179999961,RecoveryUntil=2.933333291,ExplosionPosition=new(3,4)};
        [TearDown] public void Teardown()
        {
            attack?.CancelAttack();
            for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)Object.DestroyImmediate(owned[i]);
            owned.Clear();PoolManager.Instance=null;
        }
        [Test] public void LateRecoverySeeksTheSameStoppedEmissionAsSequentialPlayback()
        {
            var state=State();
            attack.ApplyLocalFrame(state,state.WarningUntil,true);
            var visual=attack.ExplosionInstance;
            var particles=visual.particleSystem;
            particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=particles.main;main.loop=true;main.duration=2;main.startLifetime=2;main.maxParticles=2000;
            var emission=particles.emission;emission.enabled=true;emission.rateOverTime=1000;emission.SetBursts(System.Array.Empty<ParticleSystem.Burst>());
            float active=(float)(state.ActiveUntil-state.WarningUntil);
            visual.TriggerVisual();
            visual.SampleCombatTimeline(.06f,active);
            visual.SampleCombatTimeline(active,active);
            visual.SampleCombatTimeline(.6f,active);
            int sequential=particles.particleCount;
            Assert.That(sequential,Is.GreaterThan(40),"The diagnostic emitter must actually produce particles.");
            Assert.That(visual.colliders.activeSelf,Is.False);
            visual.TriggerVisual();
            visual.SampleCombatTimeline(.6f,active);
            Assert.That(particles.particleCount,Is.EqualTo(sequential).Within(2),
                "A late Recovery may age the tail, but cannot emit during it.");
            Assert.That(visual.colliders.activeSelf,Is.False);
        }
        [Test] public void ReusedAttackRebindsFreshStatsOnSameOwnerAcrossVariants()
        {
            EnemyExplosionAttackVFX previous=null;
            foreach(int hp in new[]{150,250,150})
            {
                SetStats(hp);var current=controller.stats;
                attack.RestoreExplosion(State((ulong)hp),EnemyAttackPresentationPhase.Active,2.06,true);
                Assert.That(attack.ExplosionInstance.damageInteraction.enemyStats,Is.SameAs(current));
                if(previous!=null)Assert.That(attack.ExplosionInstance,Is.SameAs(previous),"Actual VFX pool reuse, same owner GameObject.");
                previous=attack.ExplosionInstance;Assert.That(previous.colliders.activeInHierarchy,Is.True);
                attack.CancelAttack();Assert.That(previous.gameObject.activeSelf,Is.False);Assert.That(previous.colliders.activeSelf,Is.False);
                Assert.That(body.constraints,Is.EqualTo(RigidbodyConstraints2D.FreezeRotation));
            }
        }
        [Test] public void ExpiredActiveNeverReopensDamageAndExpiredRecoveryKeepsDisposalPending()
        {
            var state=State();
            attack.RestoreExplosion(state,state.PhaseAt(2.3),2.3,true);
            Assert.That(attack.ExplosionInstance.colliders.activeSelf,Is.False);
            Assert.That(controller.collider.enabled,Is.False);Assert.That(controller.hurtBox.IsActive(),Is.False);
            Assert.That(body.constraints,Is.EqualTo(RigidbodyConstraints2D.FreezeAll));
            attack.SuspendExplosion();attack.RestoreExplosion(state,state.PhaseAt(4),4,true);
            Assert.That(attack.ExplosionInstance,Is.Null);Assert.That(body.constraints,Is.EqualTo(RigidbodyConstraints2D.FreezeAll));
            var captured=state;attack.CaptureExplosion(ref captured);Assert.That(captured.SelfDestructPending,Is.True);
            attack.CancelAttack();attack.CaptureExplosion(ref captured);Assert.That(captured.SelfDestructPending,Is.False);
        }
        [Test] public void WarningRestoresSpeedMultiplierAndFollowingPoolInstanceWithoutDamage()
        {
            var state=State();state.ExplosionTriggered=state.SelfDestructPending=false;
            controller.stats.SpeedMultiplier=1.07f;
            attack.RestoreExplosion(state,EnemyAttackPresentationPhase.Warning,1.4,true);
            Assert.That(controller.stats.Speed,Is.EqualTo(6.2f*1.07f).Within(.00001));
            var warning=attack.WarningInstance;Assert.That(warning.transform.parent,Is.EqualTo(controller.transform));Assert.That(attack.ExplosionInstance,Is.Null);
            attack.SuspendExplosion();Assert.That(controller.stats.Speed,Is.EqualTo(3.6f*1.07f).Within(.00001));
            attack.RestoreExplosion(state,EnemyAttackPresentationPhase.Warning,1.7,true);
            Assert.That(attack.WarningInstance,Is.SameAs(warning));
            Assert.That(warning.GetComponent<Animancer.AnimancerComponent>().Layers[0].CurrentState.Time,Is.EqualTo(.7/attack.WarningTime).Within(.0001));
        }
        [Test] public void NineVertexWorldGeometrySeparatesInsideOutsideAndClosesAtDeadline()
        {
            attack.RestoreExplosion(State(),EnemyAttackPresentationPhase.Active,2.1,true);
            var poly=attack.ExplosionInstance.colliders.GetComponent<PolygonCollider2D>();Physics2D.SyncTransforms();
            Assert.That(poly.points.Length,Is.EqualTo(9));
            Assert.That(poly.OverlapPoint(new Vector2(3,3)),Is.True);
            Assert.That(poly.OverlapPoint(new Vector2(6,3)),Is.False);
            Assert.That(poly.OverlapPoint(new Vector2(3,4.8f)),Is.False);
            attack.CloseExpiredWindow();Assert.That(poly.isActiveAndEnabled,Is.False);
        }
        [TestCase(2.1,true)] [TestCase(2.4,false)] [TestCase(4,false)]
        public void WarningHandoffCrossingDeadlineCommitsDisposalWithoutExtendingWindow(double now,bool hit)
        {
            var state=State();state.Phase=EnemyAttackPresentationPhase.Warning;state.ExplosionTriggered=state.SelfDestructPending=false;
            attack.RestoreExplosion(state,state.PhaseAt(now),now,true);
            var captured=state;attack.CaptureExplosion(ref captured);
            Assert.That(captured.SelfDestructPending,Is.True);
            Assert.That(captured.ExplosionPosition,Is.EqualTo(new Vector2(0,1.74000025f)));
            Assert.That(attack.ExplosionInstance!=null&&attack.ExplosionInstance.colliders.activeInHierarchy,Is.EqualTo(hit));
        }
    }
}
