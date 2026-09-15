using System.Linq;
using System.Reflection;
using AstralShift.HellMaiden.AI;
using AstralShift.HellMaiden.AI.Enemy;
using AstralShift.HellMaiden.Player.Attacks;
using Mirror;
using MonsterSupergroup.NetworkCombat.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class LimboLostSoulTests
    {
        [Test]
        public void RecoveredTimingGeometryAndOriginalArtAreBound()
        {
            var p=AssetDatabase.LoadAssetAtPath<GameObject>(LimboLostSoulAssets.EnemyPath);
            var c=p.GetComponent<EnemyController>();var a=p.GetComponent<EnemyAttackExplosion>();a.enemyAnimator=c.enemyAnimator;
            Assert.That(a.WarningTime,Is.EqualTo(1.0333333f).Within(.000001));
            Assert.That(a.AttackTime,Is.EqualTo(.146666661f).Within(.000001));Assert.That(a.RecoveryTime,Is.EqualTo(.75333333f).Within(.000001));
            Assert.That(c.attackCooldown,Is.EqualTo(1));Assert.That(c.attackDistance,Is.EqualTo(2));Assert.That(a.ChaseSpeed,Is.EqualTo(6.2f));
            Assert.That(((CircleCollider2D)c.collider).radius,Is.EqualTo(.34f));Assert.That(c.collider.offset,Is.EqualTo(new Vector2(0,.3f)));
            Assert.That(p.GetComponent<EnemyContactDamage>(),Is.Null);
            var body=p.GetComponent<Rigidbody2D>();Assert.That(body.mass,Is.EqualTo(20));Assert.That(body.linearDamping,Is.EqualTo(10));
            Assert.That(body.gravityScale,Is.Zero);Assert.That(body.constraints,Is.EqualTo(RigidbodyConstraints2D.FreezeRotation));
            var explosion=AssetDatabase.LoadAssetAtPath<GameObject>(LimboLostSoulAssets.ExplosionPath).GetComponent<EnemyExplosionAttackVFX>();
            var poly=explosion.colliders.GetComponent<PolygonCollider2D>();
            Assert.That(poly.points.Length,Is.EqualTo(9));Assert.That(poly.transform.localScale,Is.EqualTo(Vector3.one*.5f));
            Assert.That(poly.points[0].x,Is.EqualTo(2.92970228f).Within(.000001));Assert.That(poly.points[6].y,Is.EqualTo(-5.04151535f).Within(.000001));
            Assert.That(explosion.damageInteraction.damageType,Is.EqualTo(DamageType.Normal));Assert.That(explosion.colliders.activeSelf,Is.False);
            Assert.That(p.transform.Find("ExplosionPosition").localPosition.y,Is.EqualTo(1.74000025f).Within(.000001));
            var s=new SerializedObject(c.enemyAnimator);
            Assert.That(s.FindProperty("moveLeftDown").FindPropertyRelative("_Clip").objectReferenceValue.name,Is.EqualTo("LostSoul_walk_left"));
            Assert.That(s.FindProperty("hurtLeftDown").FindPropertyRelative("_Clip").objectReferenceValue,Is.EqualTo(s.FindProperty("moveLeftDown").FindPropertyRelative("_Clip").objectReferenceValue));
        }
        [TestCase(0,150)] [TestCase(1,250)]
        public void FixturesKeepSourceVariantsWithExplicitSingleAliveTarget(int v,int hp)
        {
            var rules=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/LostSoulFixture"+v+".asset");
            Assert.That(rules.TryCapture(out var p,out var error),Is.True,error);Assert.That(p.Reference.ReadinessError(),Is.Null);
            var c=p.Reference.Clips.Single();Assert.That(c.Count,Is.EqualTo(1));Assert.That(c.Stats.BaseHealth,Is.EqualTo(hp));
            Assert.That(c.Stats.BaseDamage,Is.EqualTo(50));Assert.That(c.Stats.BaseSpeed,Is.EqualTo(3.6f));Assert.That(c.ContactRadius,Is.Zero);
        }
        [Test]
        public void RegisteredReferencePrefabsHaveDistinctSourceGuidIds()
        {
            var rules=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/LostSoulValidation.asset");
            Assert.That(rules.TryCapture(out var p,out var error),Is.True,error);
            var ids=new System.Collections.Generic.HashSet<long>();
            foreach(var prefab in p.Prefabs.Distinct())
            {
                var identity=prefab.GetComponent<NetworkIdentity>();
                var id=new SerializedObject(identity).FindProperty("_assetId").longValue;
                var expected=NetworkIdentity.AssetGuidToUint(new System.Guid(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab))));
                Assert.That(id,Is.EqualTo(expected),prefab.name);
                Assert.That(ids.Add(id),Is.True,"Duplicate network prefab ID: "+prefab.name);
            }
        }
        [Test]
        public void ExplicitBarrierFixtureCanRunWithoutAcceptingTheEnemyOrFullStage()
        {
            var rules=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/LostSoulBarrier.asset");
            Assert.That(rules.TryCapture(out var p,out var error),Is.True,error);
            Assert.That(p.Reference.ValidationOnly,Is.True);Assert.That(p.Reference.ReadinessError(),Is.Null);
            Assert.That(p.Reference.Barriers.Single().start,Is.EqualTo(5));
            Assert.That(p.Reference.Barriers.Single().sides,Is.EqualTo(40));
            Assert.That(p.Reference.Clips.Single().Readiness,Is.EqualTo(ReferenceEnemyReadiness.ValidationPending));
        }
        [Test]
        public void PreviewUsesExactGhoulBoundaryWithoutStartingGhoul()
        {
            var rules=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/LostSoulValidation.asset");
            Assert.That(rules.TryCapture(out var p,out var error),Is.True,error);Assert.That(p.Reference.ReadinessError(),Is.Null);
            Assert.That(p.Reference.EndTime,Is.EqualTo(449.9166666666667));
            var clips=p.Reference.Clips.Where(c=>c.Start<p.Reference.EndTime).ToArray();Assert.That(clips.Length,Is.EqualTo(14));
            Assert.That(clips.Where(c=>c.Mode==ReferenceSpawnMode.CurveBudget).Sum(c=>c.Count),Is.EqualTo(967));
            Assert.That(clips.Any(c=>c.SourceEnemy=="Ghoul"),Is.False);Assert.That(clips.Single(c=>c.SourceEnemy=="LostSoul").Count,Is.EqualTo(46));
            Assert.That(p.Reference.SourceDuration,Is.EqualTo(841.5766649882).Within(.00001));
            var full=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/Full.asset");
            Assert.That(full.TryCapture(out var f,out error),Is.True,error);f.Reference.FlowReadiness = ReferenceEnemyReadiness.ImplementationPending; Assert.That(f.Reference.ReadinessError(),Is.Not.Null);
            Assert.That(f.Reference.FlowReadiness,Is.EqualTo(ReferenceEnemyReadiness.ImplementationPending));
        }
        private static EnemyActionState Action()=>new(){ActionId=1,Explosion=true,ExplosionTriggered=true,SelfDestructPending=true,
            Phase=EnemyAttackPresentationPhase.Recovery,WarningStartedAt=1,WarningUntil=2.0333333,ActiveUntil=2.179999961,RecoveryUntil=2.933333291,NextAttackAt=3.933333291,ExplosionPosition=new(0,1.74000025f)};
        [Test]
        public void ExpiredCheckpointKeepsPendingSelfDestructAndRoundTrips()
        {
            var a=Action();Assert.That(a.PhaseAt(10),Is.EqualTo(EnemyAttackPresentationPhase.Inactive));Assert.That(a.SelfDestructPending,Is.True);
            var w=new NetworkWriter();w.Write(a);Assert.That(new NetworkReader(w.ToArraySegment()).Read<EnemyActionState>(),Is.EqualTo(a));
            var fields=new NetworkWriter();fields.WriteBool(a.Explosion);fields.WriteBool(a.ExplosionTriggered);fields.WriteBool(a.SelfDestructPending);fields.WriteVector2(a.ExplosionPosition);
            Assert.That(fields.Position,Is.EqualTo(CombatBandwidthEstimator.EnemyActionExplosionProgressBytes));
            var state=new EnemySimulationRuntimeState{Action=a};Assert.That(state.IsFinite,Is.True);
            state.Action.ExplosionPosition.x=float.NaN;Assert.That(state.IsFinite,Is.False);state.Action=a;state.Action.ExplosionTriggered=false;Assert.That(state.IsFinite,Is.False);
        }
        [TestCase("position")] [TestCase("duration")] [TestCase("wrong-type")]
        public void ServerRejectsChangedExplosionParameters(string mutation)
        {
            var p=AssetDatabase.LoadAssetAtPath<GameObject>(LimboLostSoulAssets.EnemyPath);var e=p.GetComponent<NetworkEnemySimulationAgent>();var c=p.GetComponent<EnemyController>();
            var field=typeof(NetworkEnemySimulationAgent).GetField("enemyController",BindingFlags.Instance|BindingFlags.NonPublic);var previous=field.GetValue(e);
            try
            {
                field.SetValue(e,c);c.attackScript.enemyAnimator=c.enemyAnimator;
                var m=typeof(NetworkEnemySimulationAgent).GetMethod("ValidateExplosionAction",BindingFlags.Instance|BindingFlags.NonPublic);var a=Action();
                Assert.That((bool)m.Invoke(e,new object[]{a,Vector2.zero}),Is.True);
                if(mutation=="position")a.ExplosionPosition.x+=10;else if(mutation=="duration")a.ActiveUntil+=1;else a.Dash=true;
                Assert.That((bool)m.Invoke(e,new object[]{a,Vector2.zero}),Is.False);
            }
            finally{field.SetValue(e,previous);}
        }
    }
}
