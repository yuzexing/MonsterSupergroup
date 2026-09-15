using System.Linq;
using AstralShift.HellMaiden.AI.Enemy;
using Mirror;
using MonsterSupergroup.NetworkCombat.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MonsterSupergroup.NetworkCombat.Tests
{
    public sealed class LimboStage2Tests
    {
        private static GameObject Prefab(string key)=>AssetDatabase.LoadAssetAtPath<GameObject>(LimboStage2Assets.Root+"/Reference"+key+".prefab");
        [TestCase("Skeleton",.29f,1.75f,false)]
        [TestCase("Elite_Skeleton",.429999977f,3f,true)]
        public void IndependentMeleeUsesSourceTimingsAndGeometry(string key,float recovery,float distance,bool elite)
        {
            var prefab=Prefab(key);var c=prefab.GetComponent<EnemyController>();var attack=prefab.GetComponent<EnemyAttackMelee>();attack.enemyAnimator=c.enemyAnimator;
            Assert.That(attack.WarningTime,Is.EqualTo(.57f).Within(.00001));Assert.That(attack.AttackTime,Is.EqualTo(.08f).Within(.00001));
            Assert.That(attack.RecoveryTime,Is.EqualTo(recovery).Within(.00001));Assert.That(c.attackCooldown,Is.EqualTo(.5f));Assert.That(c.attackDistance,Is.EqualTo(distance));
            Assert.That(c.isElite,Is.EqualTo(elite));Assert.That(prefab.GetComponent<EnemyContactDamage>().ContactEnabled,Is.False);
            Assert.That(prefab.transform.localScale,Is.EqualTo(Vector3.one));
            Assert.That(attack.attackPrefab.transform.localScale,Is.EqualTo(Vector3.one*(elite?1.5f:1)));
            Assert.That(attack.attackPrefab.damageInteraction.damageType,Is.EqualTo(AstralShift.HellMaiden.Player.Attacks.DamageType.Normal));
            var instance=Object.Instantiate(attack.attackPrefab.gameObject);
            try
            {
                instance.transform.position=Vector3.zero;instance.transform.rotation=Quaternion.identity;
                var polygon=instance.GetComponentInChildren<PolygonCollider2D>(true);var points=polygon.GetPath(0);
                Vector2[] source={new(1.415054f,1),new(0,1.74f),new(0,-1.74f),new(1.41505408f,-1),new(1.73587084f,.017290324f)};
                Assert.That(points.Length,Is.EqualTo(5));
                for(int i=0;i<points.Length;i++)
                {Assert.That(Vector2.Distance(points[i],source[i]),Is.LessThan(.00001));var expected=(source[i]+new Vector2(.3299999f,0))*(elite?1.5f:1);Assert.That(Vector2.Distance(polygon.transform.TransformPoint(points[i]),expected),Is.LessThan(.00002));}
            }
            finally{Object.DestroyImmediate(instance);}
            if(elite)Assert.That(c.enemyAnimator.RecoveryRightDown.Length,Is.EqualTo(.28f).Within(.00001));
            Assert.That(prefab.GetComponent<NetworkIdentity>().assetId,Is.Not.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabVariantMigration.SkeletonPath).GetComponent<NetworkIdentity>().assetId));
        }
        [TestCase("Brotchi",.55f,.46f)]
        [TestCase("Slime",.72f,.61f)]
        [TestCase("Rusher",.72f,.61f)]
        public void ContactGeometryKeepsCenterAndUsesCurrentStats(string key,float radius,float center)
        {
            var p=Prefab(key);var c=p.GetComponent<EnemyController>();var contact=p.GetComponent<EnemyContactDamage>();
            Assert.That(c.attackScript,Is.Null);Assert.That(contact.ContactEnabled,Is.True);
            var circle=contact.DamageInteraction.GetComponent<CircleCollider2D>();
            Assert.That(circle.radius,Is.EqualTo(radius));Assert.That(circle.transform.localPosition.y,Is.EqualTo(center));
            Assert.That(circle.offset,Is.EqualTo(Vector2.zero));Assert.That(contact.DamageInteraction.damageType,Is.EqualTo(AstralShift.HellMaiden.Player.Attacks.DamageType.Thorns));
        }
        [Test]
        public void PreviewIncludesFiveOriginalClipsAndStopsBeforeDash()
        {
            var rule=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/Stage2Validation.asset");
            Assert.That(rule.TryCapture(out var p,out var error),Is.True,error);Assert.That(p.Reference.ReadinessError(),Is.Null);
            var clips=p.Reference.Clips.Where(c=>c.Start<p.Reference.EndTime).OrderBy(c=>c.Start).ToArray();
            Assert.That(p.Reference.EndTime,Is.EqualTo(209.05));Assert.That(clips.Length,Is.EqualTo(5));
            Assert.That(clips.Select(c=>c.Start),Is.EqualTo(new[]{1d,60,105,135,180}));
            Assert.That(clips.Take(4).Sum(c=>c.Count),Is.EqualTo(416));Assert.That(clips[0].End,Is.EqualTo(136));
            Assert.That(clips[4].Mode,Is.EqualTo(ReferenceSpawnMode.AliveTarget));Assert.That(clips[4].Count,Is.EqualTo(100));
            Assert.That(clips[4].End,Is.EqualTo(200.0833333333333).Within(.000001));Assert.That(clips[4].Stats.BaseHealth,Is.EqualTo(8));
            Assert.That(clips[2].ResetOnReposition||clips[2].ExpiresOffscreen,Is.False);
            Assert.That(p.Reference.SourceDuration,Is.EqualTo(841.5766649882).Within(.00001));
            var playable=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/Stage2.asset");
            Assert.That(playable.TryCapture(out var ready,out error),Is.True,error);Assert.That(ready.Reference.ValidationOnly,Is.False);
            Assert.That(ready.Reference.ReadinessError(),Is.Null,"Only rendered-validated preview segments may be enabled.");
            var full=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/Full.asset");
            Assert.That(full.TryCapture(out var f,out error),Is.True,error);Assert.That(f.Reference.ReadinessError(),Is.Not.Null);
        }
        [Test]
        public void TenFixtureCasesResolveSourceVariantsWithoutSharingOrdinaryPrefabIds()
        {
            int[] hp={50,250,1200,2600,20,60,50,150,8,150};int[] damage={50,50,200,200,50,60,40,50,30,50};
            for(int i=0;i<LimboStage2Assets.Cases.Length;i++)
            {
                var rules=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/Stage2"+LimboStage2Assets.Cases[i]+".asset");
                Assert.That(rules.TryCapture(out var p,out var error),Is.True,error);Assert.That(p.Reference.ReadinessError(),Is.Null);
                Assert.That(p.Reference.Clips.First().Stats.BaseHealth,Is.EqualTo(hp[i]));Assert.That(p.Reference.Clips.First().Stats.BaseDamage,Is.EqualTo(damage[i]));
            }
        }
        [Test]
        public void ValidatedImpAttackCannotBypassUnvalidatedFormationGate()
        {
            var rule=AssetDatabase.LoadAssetAtPath<GameplayWaveRules>(LimboReferenceAssets.ResourcesRoot+"/Full.asset");
            Assert.That(rule.TryCapture(out var p,out var error),Is.True,error);
            var imp=p.Reference.Clips.Single(c=>c.SourceEnemy=="Imp"&&c.Mode==ReferenceSpawnMode.FormationBurst);
            Assert.That(imp.Readiness,Is.EqualTo(ReferenceEnemyReadiness.Ready));
            Assert.That(imp.SpawnReadiness,Is.EqualTo(ReferenceEnemyReadiness.Ready));
            // Inject a pending generator gate to continue testing independence after spatial acceptance.
            imp.SpawnReadiness=ReferenceEnemyReadiness.ValidationPending;
            var source=p.Reference;
            foreach(bool validation in new[]{false,true})
            {
                var isolated=new ReferenceWaveProgram(new[]{imp},System.Array.Empty<ReferenceBarrierDefinition>(),500,source.SourceDuration,
                    source.XpCurve,source.XpAmplitude,source.Seed,source.OffscreenDistance,source.RepositionDistance,source.RepositionGrace,
                    source.OffscreenTimeout,source.MaximumDistance,source.BurstRadius,source.BurstAspect,source.EffectsDelay,source.ActivationDelay,validation);
                if(validation) Assert.That(isolated.ReadinessError(),Is.Null);
                else Assert.That(isolated.ReadinessError(),Does.Contain("Reference spawn gate"));
                imp.SpawnReadiness=ReferenceEnemyReadiness.ImplementationPending;
                Assert.That(isolated.ReadinessError(),Does.Contain("Reference spawn gate"));
                imp.SpawnReadiness=ReferenceEnemyReadiness.ValidationPending;
            }
        }
        [Test]
        public void WarningAnimationAndDamageWindowHaveIndependentLengths()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<AnimationClip>(LimboStage2Assets.Root+"/MeleeWarning_BuildUp.anim").length,Is.EqualTo(1));
            Assert.That(AssetDatabase.LoadAssetAtPath<AnimationClip>(LimboStage2Assets.Root+"/MeleeWarning_BuildDown.anim").length,Is.EqualTo(.16666667f).Within(.00001));
            Assert.That(Prefab("Skeleton").GetComponent<EnemyAttackMelee>().attackPrefab,Is.Not.SameAs(Prefab("Elite_Skeleton").GetComponent<EnemyAttackMelee>().attackPrefab));
        }
    }
}
