using System.Linq;
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
    public sealed class LimboDashTests
    {
        [Test]
        public void SourceAttackUsesIndependentBodyGeometryAndWarningWithoutDamage()
        {
            var p=AssetDatabase.LoadAssetAtPath<GameObject>(LimboDashAssets.EnemyPath);
            var c=p.GetComponent<EnemyController>();var d=p.GetComponent<EnemyAttackDash>();d.enemyAnimator=c.enemyAnimator;
            Assert.That(d.WarningTime,Is.EqualTo(.78f).Within(.000001));Assert.That(d.AttackTime,Is.EqualTo(.429999948f).Within(.000001));
            Assert.That(d.RecoveryTime,Is.EqualTo(.08f).Within(.000001));Assert.That(c.attackCooldown,Is.EqualTo(.5f));Assert.That(c.attackDistance,Is.EqualTo(5));
            Assert.That(d.distance,Is.EqualTo(6));Assert.That(d.homingTarget||d.returnToInitialDashPosition,Is.False);
            Assert.That(d.movementCurve.Evaluate(.5f),Is.EqualTo(.75f).Within(.000001));Assert.That(d.dashExclusionLayerMask.value,Is.EqualTo(64));
            var circle=(CircleCollider2D)d.attackCollider;
            Assert.That(circle.radius,Is.EqualTo(.55f));Assert.That(circle.transform.localPosition.y,Is.EqualTo(.46f));Assert.That(circle.offset,Is.EqualTo(Vector2.zero));
            Assert.That(circle.enabled||d.damageInteraction.enabled,Is.False);Assert.That(d.damageInteraction.damageType,Is.EqualTo(DamageType.Thorns));
            Assert.That(p.GetComponent<EnemyContactDamage>().ContactEnabled,Is.False);
            Assert.That(d.attackPrefab.damageInteraction,Is.Null);Assert.That(d.attackPrefab.GetComponentsInChildren<Collider2D>(true),Is.Empty);
            Assert.That(((CircleCollider2D)c.collider).radius,Is.EqualTo(.46f));Assert.That(p.transform.localScale,Is.EqualTo(Vector3.one));
            Assert.That(p.GetComponent<NetworkIdentity>().assetId,Is.Not.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(LimboStage2Assets.Root+"/ReferenceBrotchi.prefab").GetComponent<NetworkIdentity>().assetId));
            var body=p.GetComponent<Rigidbody2D>();Assert.That(body.gravityScale,Is.Zero);Assert.That(body.mass,Is.EqualTo(20));Assert.That(body.linearDamping,Is.EqualTo(10));
        }
        [TestCase(0,70,50)] [TestCase(1,180,80)]
        public void FixturesKeepVariantStatsAndNoContinuousContact(int v,int hp,int damage)
        {
            var rules=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/DashFixture"+v+".asset");
            Assert.That(rules.TryCapture(out var p,out var error),Is.True,error);Assert.That(p.Reference.ReadinessError(),Is.Null);
            var c=p.Reference.Clips.Single();Assert.That(c.Stats.BaseHealth,Is.EqualTo(hp));Assert.That(c.Stats.BaseDamage,Is.EqualTo(damage));
            Assert.That(c.Stats.BaseSpeed,Is.EqualTo(6));Assert.That(c.ContactRadius,Is.Zero);Assert.That(c.ResetOnReposition,Is.True);
        }
        [Test]
        public void PreviewRetainsElevenOriginalClipsAndExcludesLostSoulAtCutoff()
        {
            var rules=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/DashValidation.asset");
            Assert.That(rules.TryCapture(out var p,out var error),Is.True,error);Assert.That(p.Reference.ReadinessError(),Is.Null);
            Assert.That(p.Reference.EndTime,Is.EqualTo(389.6));var clips=p.Reference.Clips.Where(c=>c.Start<p.Reference.EndTime).ToArray();
            Assert.That(clips.Length,Is.EqualTo(11));Assert.That(clips.Where(c=>c.Mode==ReferenceSpawnMode.CurveBudget).Sum(c=>c.Count),Is.EqualTo(917));
            Assert.That(clips.Any(c=>c.SourceEnemy=="LostSoul"),Is.False);Assert.That(clips.Single(c=>c.SourceEnemy=="Brotchi_Dash").End,Is.EqualTo(309.4).Within(.000001));
            Assert.That(p.Reference.SourceDuration,Is.EqualTo(841.5766649882).Within(.00001));
        }
        [Test]
        public void CheckpointCarriesDashGeometryAndRejectsNonFiniteAndReversedDeadlines()
        {
            var a=new EnemyActionState{Dash=true,ActionId=42,Phase=EnemyAttackPresentationPhase.Active,WarningStartedAt=1,WarningUntil=1.78,
                ActiveUntil=2.21,RecoveryUntil=2.29,NextAttackAt=2.79,DashStart=new(1,2),DashEnd=new(7,2),DashLastPosition=new(4,2),DashWarningOrigin=new(1,2)};
            var writer=new NetworkWriter();writer.Write(a);var reader=new NetworkReader(writer.ToArraySegment());Assert.That(reader.Read<EnemyActionState>(),Is.EqualTo(a));
            // The additional flag/vectors are present even in non-Dash checkpoints; estimate their fixed field cost explicitly.
            var fields=new NetworkWriter();fields.WriteBool(a.Dash);fields.WriteVector2(a.DashStart);fields.WriteVector2(a.DashEnd);fields.WriteVector2(a.DashLastPosition);fields.WriteVector2(a.DashWarningOrigin);
            Assert.That(fields.Position,Is.EqualTo(CombatBandwidthEstimator.EnemyActionDashProgressBytes));
            var runtime=new EnemySimulationRuntimeState{Action=a};Assert.That(runtime.IsFinite,Is.True);
            runtime.Action.DashEnd.x=float.NaN;Assert.That(runtime.IsFinite,Is.False);runtime.Action=a;runtime.Action.ActiveUntil=1.5;Assert.That(runtime.IsFinite,Is.False);
        }

        [TestCase("distance")] [TestCase("deadline")] [TestCase("curve-position")] [TestCase("missing-dash")]
        public void ServerRejectsClientChangesToConfiguredDashParameters(string mutation)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(LimboDashAssets.EnemyPath);
            var agent=prefab.GetComponent<NetworkEnemySimulationAgent>();var enemy=prefab.GetComponent<EnemyController>();
            var field=typeof(NetworkEnemySimulationAgent).GetField("enemyController",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            var previous=field.GetValue(agent);
            try
            {
                field.SetValue(agent,enemy);enemy.attackScript.enemyAnimator=enemy.enemyAnimator;
                var method=typeof(NetworkEnemySimulationAgent).GetMethod("ValidateDashAction",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                var a=new EnemyActionState{Dash=true,ActionId=1,Phase=EnemyAttackPresentationPhase.Active,WarningStartedAt=1,WarningUntil=1.78,
                    ActiveUntil=2.209999948,RecoveryUntil=2.289999948,DashStart=Vector2.zero,DashEnd=Vector2.right*6,DashLastPosition=Vector2.right*3};
                Assert.That((bool)method.Invoke(agent,new object[]{a}),Is.True);
                switch(mutation){case "distance":a.DashEnd.x=12;break;case "deadline":a.ActiveUntil+=1;break;case "curve-position":a.DashLastPosition.y=1;break;default:a.Dash=false;break;}
                Assert.That((bool)method.Invoke(agent,new object[]{a}),Is.False);
            }
            finally{field.SetValue(agent,previous);}
        }
    }
}
